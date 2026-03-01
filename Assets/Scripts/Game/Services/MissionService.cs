using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Extensions;
using Game.Services.Events;
using Game.Services.Llm;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using UnityEngine;

namespace Game.Services
{
    public static class MissionService
    {
        private static HttpClient _httpClient;
        private static LlmService _llmService;
        private static readonly LoggingService _loggingService = new();
        private static string _systemInstruction;

        private static readonly JsonSerializerSettings _jsonSettings = new()
        {
            ContractResolver = new DefaultContractResolver
            {
                NamingStrategy = new SnakeCaseNamingStrategy()
            }
        };

        public static void Init()
        {
            _systemInstruction = ResourcesService.LoadPrompt("sys_mission");
            _httpClient = new();
            _llmService = new(_httpClient, UserSettingsStore.GetApiKey(), LlmService.Model.Gemini25Flash);
        }

        public static async Task<GameEvent> HandleMissionAction(string title, string description, int selectedPersonId)
        {
            var save = GameService.Main.State.CurrentValue;
            var playerCountry = GameService.Main.PlayerCountry;

            if (playerCountry.MissionPoint <= 0)
            {
                _loggingService.LogWarningForService("MissionService", "No mission points left.");
                return null;
            }

            // 2. Prepare Payload
            var flattenedSave = new
            {
                Game = save.Game,
                Country = save.Country.Values,
                Relation = save.Relation,
                Person = save.Person,
                Commandery = save.Commandery.Values,
                Army = save.Army,
                Battle = save.Battle
            };

            var missionPayload = new
            {
                mission = new[]
                {
                    new
                    {
                        mission = title,
                        mission_desc = description,
                        origin_country_id = playerCountry.Id,
                        assigned_person_id = selectedPersonId
                    }
                },
                game_stats = new[] { flattenedSave }
            };

            var payloadJson = JsonConvert.SerializeObject(missionPayload, _jsonSettings);
            Debug.Log($"Mission payload: {payloadJson}");

            // 4. Call LLM
            _loggingService.LogForService("MissionService", "Generating mission outcome...");
            try
            {
                var prompt = _systemInstruction + "\n\nUser Input:\n" + payloadJson;
                var response = await _llmService.AskTextAsync(prompt);

                if (response == null || response.Choices == null || response.Choices.Count == 0)
                {
                    _loggingService.LogErrorForService("MissionService", "LLM returned no response for mission.");
                    return null;
                }

                var content = response.Choices[0]?.Message?.Content;

                var json = ExtractJson(content);
                var root = JObject.Parse(json);

                // Check if it's a failure response
                if (root.TryGetValue("mission", out var missionProp) && missionProp.ToString() == "False")
                {
                    var reason = root.TryGetValue("reason", out var r) ? r.ToString() : "Unknown reason";
                    _loggingService.LogWarningForService("MissionService", $"Mission failed to start: {reason}");
                    return null;
                }

                // Map to GameEvent
                var gameEvent = new GameEvent
                {
                    EventName = title,
                    EventDesc = description,
                    EventCountry = playerCountry.Id,
                    Outcomes = new List<EventOutcome>()
                };

                if (root.TryGetValue("outcome", out var outcomeArr) && outcomeArr is JArray arr)
                {
                    foreach (var outElem in arr)
                    {
                        var outcome = outElem.ToObject<EventOutcome>();
                        if (outcome != null)
                        {
                            gameEvent.Outcomes.Add(outcome);

                            // Use first outcome's name and desc for the event
                            gameEvent.EventName = outcome.OutcomeName ?? title;
                            gameEvent.EventDesc = outcome.OutcomeDesc ?? description;

                            // Log mission effects
                            _loggingService.LogForService("MissionService", $"Outcome: {outcome.OutcomeName}");
                            if (outcome.Effects != null)
                            {
                                foreach (var effectCategory in outcome.Effects)
                                {
                                    foreach (var effect in effectCategory.Value)
                                    {
                                        var effectJson = JsonConvert.SerializeObject(effect);
                                        _loggingService.LogForService("MissionService", $"  [{effectCategory.Key}] {effectJson}");
                                    }
                                }
                            }

                            // Only use the first valid outcome to avoid conflicting results
                            break;
                        }
                    }
                }

                playerCountry.MissionPoint--;
                GameService.Main.State.ApplyInnerMutations();

                return gameEvent;
            }
            catch (Exception ex)
            {
                _loggingService.LogErrorForService("MissionService", "Error handling mission action", ex);
                return null;
            }
        }

        private static string ExtractJson(string content)
        {
            var start = content.IndexOf('{');
            var end = content.LastIndexOf('}');
            if (start != -1 && end != -1 && end > start)
            {
                return content.Substring(start, end - start + 1);
            }
            return "{}";
        }
    }
}
