using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Game.Services;
using Game.Services.Events;
using Game.Services.Llm;
using Game.Services.Saves;
using R3;
using UnityEngine;

namespace Game
{
    /// <summary>
    /// Automated event generator for testing event flow and history generation.
    /// Uses its own state and services, focusing only on event generation and history summarization.
    /// </summary>
    public class AutomatedEventGenerator : MonoBehaviour
    {
        private ReactiveProperty<SaveData> _state = new(null);

        [Header("Configuration")]
        [Tooltip("Number of turns to process automatically")]
        public int turnsToProgress = 10;

        [Tooltip("Delay in seconds between turns (for observation)")]
        public float delayBetweenTurns = 0.5f;

        [Tooltip("Use mock events instead of LLM-generated events")]
        public bool useMockEvents = false;

        [Header("Runtime Status")]
        [SerializeField] private int currentTurn = 0;
        [SerializeField] private int totalTurnsCompleted = 0;
        [SerializeField] private bool isRunning = false;
        [SerializeField] private string lastEventProcessed = "";

        private CancellationTokenSource _cancellationTokenSource;

        // Own service instances
        private EventService _eventService;
        private MemoryService _memoryService;
        private EffectService _effectService;
        private EventLogger _eventLogger;
        private HttpClient _httpClient;

        private void Awake()
        {
            // Initialize our own services
            _httpClient = new HttpClient();
            var llmService = new LlmService(_httpClient, UserSettingsStore.GetApiKey(), LlmService.Model.Gemini25Flash);
            _eventService = new EventService(llmService);
            _memoryService = new MemoryService(llmService);
            _effectService = new EffectService();
            _eventLogger = new EventLogger();

            // Initialize EventService
            _eventService.Init();

            // Load save data into our own state
            _state.Value = SaveService.CurrentSave;
        }

        private void Start()
        {
            StartAutomation();
        }

        private void OnValidate()
        {
            if (turnsToProgress < 1)
                turnsToProgress = 1;
            if (delayBetweenTurns < 0)
                delayBetweenTurns = 0;
        }

        /// <summary>
        /// Start the automated event generation process
        /// </summary>
        public void StartAutomation()
        {
            var save = SaveService.Load("whole_1_5_3");
            _state.Value = save;

            if (isRunning)
            {
                Debug.LogWarning("Automation is already running!");
                return;
            }

            if (_state.CurrentValue == null)
            {
                Debug.LogError("No save data loaded! Cannot start automation.");
                return;
            }

            _cancellationTokenSource = new CancellationTokenSource();
            _ = RunAutomationAsync(_cancellationTokenSource.Token);
        }

        /// <summary>
        /// Stop the automated event generation process
        /// </summary>
        public void StopAutomation()
        {
            if (!isRunning)
            {
                Debug.LogWarning("Automation is not running!");
                return;
            }

            Debug.Log("Stopping automation...");
            _cancellationTokenSource?.Cancel();
        }

        private async Task RunAutomationAsync(CancellationToken ct)
        {
            isRunning = true;
            totalTurnsCompleted = 0;

            Debug.Log($"Starting automated event generation for {turnsToProgress} turns...");
            Debug.Log($"Initial state - Turn: {_state.CurrentValue.Game.Turn}, Date: {_state.CurrentValue.Game.CurrentMonth}/{_state.CurrentValue.Game.CurrentYear}");

            try
            {
                for (int i = 0; i < turnsToProgress; i++)
                {
                    if (ct.IsCancellationRequested)
                    {
                        Debug.Log("Automation cancelled by user.");
                        break;
                    }

                    currentTurn = i + 1;
                    Debug.Log($"========== Processing Turn {currentTurn}/{turnsToProgress} ==========");

                    await ProcessTurnAutomatically(ct);
                    totalTurnsCompleted++;

                    if (delayBetweenTurns > 0 && i < turnsToProgress - 1)
                    {
                        await Task.Delay((int)(delayBetweenTurns * 1000), ct);
                    }
                }

                Debug.Log($"========== Automation Complete! ==========");
                Debug.Log($"Total turns processed: {totalTurnsCompleted}");
                Debug.Log($"Final game turn: {_state.CurrentValue.Game.Turn}");
                Debug.Log($"Final date: {_state.CurrentValue.Game.CurrentMonth}/{_state.CurrentValue.Game.CurrentYear}");
            }
            catch (OperationCanceledException)
            {
                Debug.Log("Automation was cancelled.");
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error during automation: {ex}");
            }
            finally
            {
                isRunning = false;
                currentTurn = 0;
            }
        }

        /// <summary>
        /// Process a single turn - event generation and history summarization only
        /// </summary>
        private async Task ProcessTurnAutomatically(CancellationToken ct)
        {
            var save = _state.CurrentValue;
            var playerCid = save.Game.PlayerCountryId;

            // Event processing
            var eventChannel = System.Threading.Channels.Channel.CreateUnbounded<GameEvent>();
            var processedEvents = new List<GameEvent>();
            var choicesMade = new List<EventChoice>();
            var gameEventsAndChoices = new List<GameEventAndChoice>();

            // Producer Task - Generate events in background
            _ = Task.Run(async () =>
            {
                try
                {
                    // Generate LLM or mock events
                    if (useMockEvents)
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

            // Consumer Loop - Process events and automatically choose first option
            await foreach (var gameEvent in eventChannel.Reader.ReadAllAsync(ct))
            {
                // Apply unconditional outcomes first (matching GameService pattern)
                foreach (var outcome in gameEvent.Outcomes)
                {
                    _effectService.ApplyEffects(save, outcome.Effects);
                }

                // Always choose the first choice (index 0) for all events
                if (gameEvent.Choices.Count > 0)
                {
                    var choice = gameEvent.Choices[0];
                    _effectService.ApplyEffects(save, choice.Effects);

                    choicesMade.Add(choice);
                    processedEvents.Add(gameEvent);

                    gameEventsAndChoices.Add(new GameEventAndChoice
                    {
                        GameEvent = gameEvent,
                        ChoiceMade = choice
                    });

                    lastEventProcessed = gameEvent.EventName;
                    bool isPlayerEvent = gameEvent.EventCountry == playerCid || gameEvent.RelatedCountryIds.Contains(playerCid);
                    string actorType = isPlayerEvent ? "PLAYER" : $"AI({gameEvent.EventCountry})";
                    Debug.Log($"[{actorType}] Event: '{gameEvent.EventName}' -> Chose: '{choice.ChoiceName}'");
                }

                // Apply state changes after each event (matching GameService pattern)
                _state.OnNext(save);
            }

            // Log events to files
            if (gameEventsAndChoices.Count > 0)
            {
                _eventLogger.LogGameEventsAndChoices(save, gameEventsAndChoices.ToArray());
                Debug.Log($"Logged {gameEventsAndChoices.Count} events to disk.");
            }

            // History Updates - WAIT for completion before moving to next turn
            if (processedEvents.Count > 0 && !useMockEvents)
            {
                Debug.Log($"Running history summarization for {processedEvents.Count} events...");
                await RunHistorySummarizationAsync(save, processedEvents, choicesMade);
                Debug.Log("History summarization complete.");
            }

            // Advance turn counter
            save.Game.Turn += 1;
            save.Game.CurrentMonth++;
            if (save.Game.CurrentMonth > 12)
            {
                save.Game.CurrentMonth = 1;
                save.Game.CurrentYear++;
            }

            // Apply state changes
            _state.OnNext(save);

            Debug.Log($"Turn {save.Game.Turn - 1} completed. Date: {save.Game.CurrentMonth}/{save.Game.CurrentYear}");
        }

        private async Task RunHistorySummarizationAsync(SaveData save, List<GameEvent> events, List<EventChoice> choices)
        {
            try
            {
                var historyUpdates = await _memoryService.GenerateHistoryUpdates(save, events, choices);

                if (historyUpdates != null && historyUpdates.Count > 0)
                {
                    _effectService.ApplyEffects(save, historyUpdates);
                    Debug.Log($"Applied {historyUpdates.Count} history updates.");
                }
                else
                {
                    Debug.Log("No history updates generated.");
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"History Generation Failed: {e.Message}\n{e.StackTrace}");
            }
        }

        private void OnDestroy()
        {
            _cancellationTokenSource?.Cancel();
            _cancellationTokenSource?.Dispose();
            _httpClient?.Dispose();
        }

        // Unity Editor Buttons (visible in Inspector)
        [ContextMenu("Start Automation")]
        private void StartAutomationContextMenu()
        {
            StartAutomation();
        }

        [ContextMenu("Stop Automation")]
        private void StopAutomationContextMenu()
        {
            StopAutomation();
        }
    }
}
