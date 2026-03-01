using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Extensions;
using Game.Services.Events;
using Game.Services.Llm;
using Game.Services.Saves;
using Game.UI;
using Game.World.Map;
using R3;
using UnityEngine;

namespace Game.Services
{
    public class GameService : MonoBehaviour
    {
        public static GameService Main { get; private set; }
        public ReactiveProperty<SaveData> State = new(null);
        [NonSerialized] public bool UseMockEvents = false;

        public bool UseCustomSave;
        public string DefaultSaveName;

        public enum GamePhase
        {
            PlayerAction,
            EventGeneration
        }

        public ReactiveProperty<GamePhase> currentPhase = new(GamePhase.PlayerAction);

        public CountryData PlayerCountry => State.CurrentValue.Country.ContainsKey(State.CurrentValue.Game.PlayerCountryId)
            ? State.CurrentValue.Country[State.CurrentValue.Game.PlayerCountryId]
            : null;

        private CancellationTokenSource _loopCts;

        // Services
        private LoggingService _loggingService;
        private LlmService _llmService;
        public CommanderyActionService CommanderyAction => _commanderyActionService;
        private CommanderyActionService _commanderyActionService;
        private BattleService _battleService;
        private AIService _aiService;
        private TurnService _turnService;
        private MemoryService _memoryService;
        private RebelService _rebelService;

        // Refactored Services
        private MapService _mapService;
        private EffectService _effectService;
        private TerritoryService _territoryService;
        private DiplomacyService _diplomacyService;
        private EventService _eventService;

        private HttpClient _httpClient;
        private SynchronizationContext _mainThreadContext;

        private EventLogger _eventLogger;

        // Queue for pending history updates (e.g., from missions)
        private readonly List<GameEvent> _pendingHistoryEvents = new();
        private readonly List<EventChoice> _pendingHistoryChoices = new();

        private void Awake()
        {
            Main = this;
            _mainThreadContext = SynchronizationContext.Current;
            UseMockEvents = false;
        }

        private void Start()
        {
            // Initialize infrastructure
            _httpClient = new HttpClient();
            _loggingService = new LoggingService();
            _llmService = new LlmService(_httpClient, UserSettingsStore.GetApiKey(), LlmService.Model.Gemini25Flash);

            // Initialize Refactored Services (order matters - dependencies first)
            _mapService = new MapService();
            _effectService = new EffectService();
            _eventService = new EventService(_llmService);
            _eventLogger = new EventLogger();

            // TerritoryService needs ReactiveProperty<SaveData>, GameMap, and MapService
            // We'll initialize it in StartFromSave when State is available

            // DiplomacyService needs State, TerritoryService, and MapService
            // Also initialized in StartFromSave

            // Initialize Domain Services
            _commanderyActionService = new CommanderyActionService();

            _battleService = new BattleService(_llmService, _loggingService);
            // AIService needs DiplomacyService - initialize in StartFromSave
            // TurnService needs TerritoryService - initialize in StartFromSave
            _memoryService = new MemoryService(_llmService);

            _eventService.Init();
            MissionService.Init();
            _rebelService = new RebelService(_llmService, _loggingService);

            //var save = SaveService.Load("whole_1_5_1_alliance");
            //StartFromSave(save);

            if (UseCustomSave)
            {
                var save = SaveService.Load(DefaultSaveName);
                StartFromSave(save);
            }
            else
            {
                StartFromSave(SaveService.CurrentSave);
            }
        }

        private void OnDestroy()
        {
            _httpClient?.Dispose();
        }

        public void StartFromSave(SaveData save)
        {
            State.Value = save;

            // Initialize services that depend on State
            _territoryService = new TerritoryService(State, World.Map.GameMap.Main, _mapService);
            _diplomacyService = new DiplomacyService(State, _territoryService, _mapService);
            _aiService = new AIService(_commanderyActionService, _loggingService, _diplomacyService);
            _turnService = new TurnService(_territoryService, _battleService);

            // Initialize Map Data mapping
            _mapService.InitializeMapNodes(save);
            _mapService.UpdateBorderStatus(save); // Initialize border caches

            _territoryService.UpdateFogOfWar(PlayerCountry.Id);

            _loopCts?.Cancel();
            _loopCts = new CancellationTokenSource();
            _ = GameLoop(_loopCts.Token);
        }

        private async Task GameLoop(CancellationToken ct)
        {
            if (ct.IsCancellationRequested) return;
            while (!ct.IsCancellationRequested)
            {
                await ProcessTurn(ct);
            }
        }

        private async Task ProcessTurn(CancellationToken ct)
        {
            try
            {
                currentPhase.Value = GamePhase.PlayerAction;

                var save = State.CurrentValue;
                _battleService.Initialize(save);
                var playerCid = save.Game.PlayerCountryId;

                GameUI.Main.SetFinishTurnButtonEnabled(true);
                // GameUI.Main.SetPlayerActionsEnabled(true);

                // Wait for finish turn button press
                await GameUI.Main.WaitForFinishTurnAsync();

                currentPhase.Value = GamePhase.EventGeneration;

                GameUI.Main.SetFinishTurnButtonEnabled(false);
                // GameUI.Main.SetPlayerActionsEnabled(false);
                GameUI.Main.SetLoading(true);

                // 1. Process Turn Logic (Income, Pop Growth, etc.)
                var turnResult = _turnService.ProcessTurn(save);
                foreach (var log in turnResult.Log) Debug.Log(log);

                // 2. AI Turns
                _mapService.UpdateBorderStatus(save); // Ensure borders are up to date before AI thinks
                var diplomacyEvents = _aiService.ProcessAiTurns(save);

                // 3. Battles
                var battleEvents = await _battleService.ProcessBattles(save);
                ApplyStateChanges();

                try
                {
                    // 4. Generate random events (Streamed)
                    // We use a Channel to stream events from the producer (LLM or Mock) to the consumer (UI & Game Logic)
                    // This allows us to display events as they come in, instead of waiting for all of them.
                    var eventChannel = System.Threading.Channels.Channel.CreateUnbounded<GameEvent>();
                    var processedEvents = new List<GameEvent>();
                    var choicesMade = new List<EventChoice>();
                    var gameEventsAndChoices = new List<GameEventAndChoice>();

                    // 4.1 Producer Task (Background)
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            // Enqueue diplomacy proposals first (so player can decide before other events)
                            foreach (var de in diplomacyEvents)
                                await eventChannel.Writer.WriteAsync(de, ct);

                            // Enqueue Pre-determined events (battles)
                            foreach (var be in battleEvents)
                                await eventChannel.Writer.WriteAsync(be, ct);

                            // Process Turn Events (Death, Succession, New Characters, Rebellions)
                            if (!UseMockEvents)
                            {
                                // Succession
                                foreach (var king in turnResult.DeadKings)
                                {
                                    var successionEvent = await _eventService.HandleSuccessionAsync(save, king, ct: ct);
                                    if (successionEvent != null)
                                    {
                                        if (successionEvent.RelatedCountryIds == null)
                                            successionEvent.RelatedCountryIds = new List<int>();

                                        if (!successionEvent.RelatedCountryIds.Contains(playerCid))
                                            successionEvent.RelatedCountryIds.Add(playerCid);

                                        await eventChannel.Writer.WriteAsync(successionEvent, ct);
                                    }
                                }

                                // Funerals / Death Events
                                var deathEvents = await _eventService.HandleDeathEventsAsync(save, turnResult.DeadPersons, ct: ct);
                                foreach (var de in deathEvents)
                                    await eventChannel.Writer.WriteAsync(de, ct);

                                // New Characters (auto-added to save, returns log and events)
                                var (newCharLogs, newCharEvents) = await _eventService.AddNewCharactersAsync(save, ct: ct);
                                foreach (var log in newCharLogs)
                                    _loggingService.LogForService("GameService", log);
                                foreach (var nce in newCharEvents)
                                    await eventChannel.Writer.WriteAsync(nce, ct);

                                // Rebellions
                                foreach (var rebelReq in turnResult.Rebellions)
                                {
                                    var rebelEvent = await _rebelService.HandleRebellion(
                                        save,
                                        rebelReq.CountryId,
                                        rebelReq.CommanderyId,
                                        rebelReq.RebellionType,
                                        rebelReq.PersonId,
                                        rebelReq.ArmyId,
                                        ct: ct
                                    );
                                    if (rebelEvent != null)
                                        await eventChannel.Writer.WriteAsync(rebelEvent, ct);
                                }
                            }

                            // LLM / Mock Events
                            if (UseMockEvents)
                            {
                                var mockEvent = await _eventService.GenerateExampleAsync();
                                await eventChannel.Writer.WriteAsync(mockEvent, ct);
                            }
                            else
                            {
                                await foreach (var gameEvent in _eventService.StreamGameEvents(save, ct: ct)
                                                   .WithCancellation(ct)
                                                   .ConfigureAwait(false))
                                {
                                    await eventChannel.Writer.WriteAsync(gameEvent, ct);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.LogError($"Event Producer Error: {ex}");
                        }
                        finally
                        {
                            eventChannel.Writer.TryComplete();
                        }
                    }, ct);

                    // 4.2 Consumer Loop (Main Thread)
                    // We process events on the main thread to safely interact with UI
                    await foreach (var gameEvent in eventChannel.Reader.ReadAllAsync(ct))
                    {
                        // Check if player is related to event to see it
                        bool isRelated = gameEvent.RelatedCountryIds.Contains(playerCid) || gameEvent.EventCountry == playerCid;

                        // Apply unconditional outcomes first
                        foreach (var outcome in gameEvent.Outcomes)
                        {
                            _effectService.ApplyEffects(save, outcome.Effects);
                        }
                        ApplyStateChanges();

                        if (gameEvent.EventCountry == playerCid || gameEvent.RelatedCountryIds.Contains(playerCid))
                        {
                            // Player Choice
                            var choice = await GameUI.Main.DisplayEventAsync(gameEvent);
                            if (choice != null)
                            {
                                EffectService.Main.ApplyEffects(save, choice.Effects);

                                // Check if this is a diplomacy event and handle the deal
                                if (IsDiplomacyEvent(gameEvent))
                                {
                                    _aiService.HandleDiplomacyChoice(save, gameEvent, choice);
                                }

                                ApplyStateChanges();
                                choicesMade.Add(choice);
                                processedEvents.Add(gameEvent);

                                gameEventsAndChoices.Add(new GameEventAndChoice
                                {
                                    GameEvent = gameEvent,
                                    ChoiceMade = choice
                                });
                            }
                        }
                        else
                        {
                            // AI Choice
                            if (gameEvent.Choices.Count > 0)
                            {
                                var choice = gameEvent.Choices[0]; // Default first choice for AI for now
                                EffectService.Main.ApplyEffects(save, choice.Effects);
                                choicesMade.Add(choice);
                                processedEvents.Add(gameEvent);

                                gameEventsAndChoices.Add(new GameEventAndChoice
                                {
                                    GameEvent = gameEvent,
                                    ChoiceMade = choice
                                });

                                Debug.Log($"AI {gameEvent.EventCountry} chose {choice.ChoiceName} for {gameEvent.EventName}");
                            }
                        }

                        ApplyStateChanges();
                    }

                    GameUI.Main.SetLoading(false);

                    _eventLogger.LogGameEventsAndChoices(State.CurrentValue, gameEventsAndChoices.ToArray());

                    // 6. Memory / History Updates (include any pending mission events)
                    lock (_pendingHistoryEvents)
                    {
                        processedEvents.AddRange(_pendingHistoryEvents);
                        choicesMade.AddRange(_pendingHistoryChoices);
                        _pendingHistoryEvents.Clear();
                        _pendingHistoryChoices.Clear();
                    }

                    if (processedEvents.Count > 0 && !UseMockEvents)
                    {
                        RunHistorySummarizationInBackground(save, processedEvents, choicesMade);
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError($"Error during event processing: {e}");
                    GameUI.Main.SetLoading(false); // Ensure loading is disabled even on error
                }

                ApplyStateChanges();

                // Next turn
                save.Game.Turn += 1;
                save.Game.CurrentMonth++;
                if (save.Game.CurrentMonth > 12)
                {
                    save.Game.CurrentMonth = 1;
                    save.Game.CurrentYear++;
                }

                ApplyStateChanges();
            }
            catch (Exception e)
            {
                Debug.LogError(e);
            }
        }

        // public async Task StartMission(string title, string desc, int personId)
        // {
        //     var playerCid = state.CurrentValue.Game.PlayerCountryId;
        //     var playerCountry = PlayerCountry;
        //     var evt = await _missionService.HandleMissionAction(state.CurrentValue, playerCountry, title, desc, personId);
        //     if (evt != null)
        //     {
        //         // Show result event
        //         var choice = await GameUI.Main.DisplayEventAsync(evt);
        //         if (choice != null)
        //         {
        //             EffectService.ApplyEffects(state.CurrentValue, choice.Effects);
        //             ApplyStateChanges();
        //         }
        //     }
        // }

        private bool IsDiplomacyEvent(GameEvent gameEvent)
        {
            // Diplomacy events have names containing "Treaty", "Alliance Proposal", or "Diplomatic Proposal"
            var name = gameEvent.EventName ?? "";
            return name.Contains("Peace Treaty") ||
                   name.Contains("Alliance Proposal") ||
                   name.Contains("Diplomatic Proposal");
        }

        private void ApplyStateChanges()
        {
            State.OnNext(State.CurrentValue);
        }

        private void RunHistorySummarizationInBackground(SaveData save, List<GameEvent> events, List<EventChoice> choices)
        {
            // Fire-and-forget task
            _ = Task.Run(async () =>
            {
                try
                {
                    // Note: GenerateHistoryUpdates captures the state synchronously at the beginning of the method
                    // So it is safe to call, even if the main thread moves on.
                    // However, we must ensure we are back on the main context to apply changes if we touch Unity objects.
                    // Since SaveData and EffectService are pure C# data, it is mostly thread-safe if no one else is writing to the exact same history fields.
                    // But to be safe and consistent with "ApplyStateChanges", we should marshal back.

                    var historyUpdates = await _memoryService.GenerateHistoryUpdates(save, events, choices);

                    // Marshal to main thread
                    _mainThreadContext.Post(_ =>
                    {
                        if (historyUpdates != null && historyUpdates.Count > 0)
                        {
                            EffectService.Main.ApplyEffects(save, historyUpdates);
                            ApplyStateChanges();
                            Debug.Log("Background history update applied.");
                        }
                    }, null);
                }
                catch (Exception e)
                {
                    Debug.LogError($"Background History Generation Failed: {e.Message}");
                }
            });
        }

        /// <summary>
        /// Queues a mission event for history update at end of turn.
        /// Called from MissionsUI after mission completion.
        /// Events are batched with end-of-turn events for a single MemoryService call.
        /// </summary>
        public void RunMissionHistoryUpdate(GameEvent missionEvent, EventChoice choice)
        {
            if (missionEvent == null || choice == null) return;
            lock (_pendingHistoryEvents)
            {
                _pendingHistoryEvents.Add(missionEvent);
                _pendingHistoryChoices.Add(choice);
            }
            Debug.Log($"Queued mission '{missionEvent.EventName}' for history update at end of turn.");
        }
    }
}