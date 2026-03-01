using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Game.Services.Llm;
using Game.Services.Saves;
using Newtonsoft.Json;
using UnityEngine;

namespace Game.Services.Events
{
    public class EventService
    {
        public static EventService Main { get; private set; }

        private HttpClient _httpClient;
        private LlmService _llmService;
        private readonly LoggingService _logging = new();
        private readonly Dictionary<string, string> _promptCache = new();

        public EventService(LlmService llmService)
        {
            _llmService = llmService;
            Main = this;
        }

        public void Init()
        {
            LoadCachedPrompt("sys_instruction");
            LoadCachedPrompt("sys_event_single");
            LoadCachedPrompt("sys_new_char");
            LoadCachedPrompt("sys_succession");
            LoadCachedPrompt("sys_funeral");
            LoadCachedPrompt("sys_event_evaluation");

            _httpClient = new();
            _llmService = new(_httpClient, UserSettingsStore.GetApiKey(), LlmService.Model.Gemini25Flash);
        }

        private string LoadCachedPrompt(string promptName)
        {
            if (!_promptCache.TryGetValue(promptName, out string prompt))
            {
                prompt = ResourcesService.LoadPrompt(promptName);
                _promptCache[promptName] = prompt;
            }
            return prompt;
        }

        public async IAsyncEnumerable<GameEvent> StreamGameEvents(
            SaveData state,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            // Build selective context to reduce token usage
            var rng = new System.Random();
            const int maxPerCountry = 3;

            // Sample persons, commanderies, armies per country
            var sampledPersons = new List<PersonData>();
            var sampledCommanderies = new List<CommanderyData>();
            var sampledArmies = new List<ArmyData>();

            foreach (var country in state.Country.Values)
            {
                // Persons for this country
                var countryPersons = state.Person.Where(p => p.CountryId == country.Id).ToList();
                if (countryPersons.Count <= maxPerCountry)
                    sampledPersons.AddRange(countryPersons);
                else
                    sampledPersons.AddRange(countryPersons.OrderBy(_ => rng.Next()).Take(maxPerCountry));

                // Commanderies for this country
                var countryCommanderies = state.Commandery.Values.Where(c => c.CountryId == country.Id).ToList();
                if (countryCommanderies.Count <= maxPerCountry)
                    sampledCommanderies.AddRange(countryCommanderies);
                else
                    sampledCommanderies.AddRange(countryCommanderies.OrderBy(_ => rng.Next()).Take(maxPerCountry));

                // Armies for this country
                var countryArmies = state.Army.Where(a => a.CountryId == country.Id).ToList();
                if (countryArmies.Count <= maxPerCountry)
                    sampledArmies.AddRange(countryArmies);
                else
                    sampledArmies.AddRange(countryArmies.OrderBy(_ => rng.Next()).Take(maxPerCountry));
            }

            var contextData = new
            {
                game = state.Game,
                country = state.Country.Values,
                relation = state.Relation,
                battle = state.Battle,
                person = sampledPersons,
                commandery = sampledCommanderies,
                army = sampledArmies
            };

            var stateJson = JsonConvert.SerializeObject(contextData);
            var prompt = LoadCachedPrompt("sys_instruction") + "\n\nUser Input:\n" + stateJson;

            // Source text chunks from LLM
            IAsyncEnumerable<string> chunks = _llmService.AskStreamAsync(prompt, ct: ct);

            // Extract each { ... } object from the top-level JSON array
            await foreach (var objJson in JsonArrayObjectStreamer.ExtractObjectsFromArray(chunks, ct))
            {
                if (ct.IsCancellationRequested) yield break;

                // Log raw JSON object for debugging
                Debug.Log($"Stream received object: {objJson.Substring(0, Math.Min(objJson.Length, 100))}...");

                GameEvent ev;
                try
                {
                    ev = JsonConvert.DeserializeObject<GameEvent>(objJson);
                    _logging.LogForService("EventService", $"Received event:\n{objJson}");
                }
                catch (Exception ex)
                {
                    // If you want: log bad event JSON and continue
                    // (don’t spam logs too much in release)
                    Debug.LogError($"Bad GameEvent JSON chunk:\n{objJson}\n{ex}");
                    continue;
                }

                if (ev != null)
                    yield return ev;
            }
        }

        public async Task<GameEvent> GenerateAsync(SaveData state, CancellationToken ct = default)
        {
            var prompt = LoadCachedPrompt("sys_event_single");
            var stateJson = JsonConvert.SerializeObject(state);

            prompt = $"{prompt}\nUser input: {stateJson}";
            Debug.Log(prompt);

            var response = await _llmService.AskTextAsync(prompt, ct: ct);
            var content = response.Choices[0].Message.Content;
            Debug.Log(content);

            var gameEvent = JsonConvert.DeserializeObject<GameEvent>(content)
                ?? throw new System.Exception("Failed to deserialize response content");
            return gameEvent;
        }

        public async Task<GameEvent> GenerateExampleAsync()
        {
            await Task.Delay(500);
            return new GameEvent()
            {
                EventCountry = 1,
                EventName = "Test event",
                EventDesc = "Test desc",
                Choices = {
                    new()
                    {
                        ChoiceName = "Test choice",
                        ChoiceDesc = "Choice desc",
                        Effects = new()
                        {
                            { "country", new() { new() { { "id", 1 }, { "stability", "+10" }, { "prestige", "+10" } } } }
                        }
                    }
                },
                Outcomes =
                {
                    new()
                    {
                        OutcomeName = "WOW!",
                        OutcomeDesc = "WOWZ!",
                        Effects = new()
                        {
                            { "country", new() { new() { { "id", 1 }, { "treasury", "+200" } } } }
                        }
                    }
                },
                RelatedCountryIds = { 1, 2, 3 }
            };
        }

        public async Task<(List<string> Log, List<GameEvent> Events)> AddNewCharactersAsync(
            SaveData save,
            string promptName = "sys_new_char",
            CancellationToken ct = default)
        {
            var newCharLog = new List<string>();
            var newCharEvents = new List<GameEvent>();
            var playerCountryId = save.Game.PlayerCountryId;

            // 1) Identify countries with < 3 alive characters
            var lowCharCountries = save.Country.Values
                .Where(c => save.Person.Count(p => p.CountryId == c.Id && p.IsAlive) < 3)
                .ToList();

            if (lowCharCountries.Count == 0)
                return (newCharLog, newCharEvents);

            _logging.LogForService("EventService", $"Found {lowCharCountries.Count} countries needing new characters.");

            // 2) Prepare context (filtered)
            var contextData = new
            {
                game = save.Game,
                country = save.Country.Values,
                person = save.Person
            };
            var contextJson = JsonConvert.SerializeObject(contextData);

            // 3) Load instruction
            var systemInstruction = LoadCachedPrompt(promptName);
            var prompt = systemInstruction + "\n\nData Context:\n" + contextJson;

            // 4) Call LLM
            try
            {
                var response = await _llmService.AskTextAsync(prompt, ct: ct);
                var content = response.Choices[0].Message.Content;

                // 5) Extract multiple JSON objects from response text
                var jsonBlocks = ExtractJsonBlocks(content);
                var maxId = save.Person.Any() ? save.Person.Max(p => p.Id) : 0;

                foreach (var json in jsonBlocks)
                {
                    try
                    {
                        var newPerson = JsonConvert.DeserializeObject<PersonData>(json);
                        if (newPerson == null) continue;

                        // Basic validation
                        if (newPerson.CountryId == 0 && string.IsNullOrWhiteSpace(newPerson.Name))
                            continue;

                        maxId++;
                        newPerson.Id = maxId;
                        save.Person.Add(newPerson);

                        var countryName = save.Country.TryGetValue(newPerson.CountryId, out var c) ? c.Name : "Unknown";
                        newCharLog.Add($"[cyan]New Character[/]: {newPerson.Name} ({newPerson.Role}) joined {countryName}.");

                        // Create event for player's new characters
                        if (newPerson.CountryId == playerCountryId)
                        {
                            var evt = new GameEvent
                            {
                                EventCountry = playerCountryId,
                                EventName = "New Character Joined the Court",
                                EventDesc = !string.IsNullOrWhiteSpace(newPerson.History)
                                    ? newPerson.History
                                    : $"{newPerson.Name}, a new {newPerson.Role}, has joined your court.",
                                RelatedCountryIds = new List<int> { playerCountryId }
                            };
                            newCharEvents.Add(evt);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logging.LogErrorForService("EventService", "Failed to parse individual character JSON", ex);
                    }
                }
            }
            catch (Exception ex)
            {
                _logging.LogErrorForService("EventService", "Error generating new characters", ex);
                newCharLog.Add("[red]Error generating new characters.[/]");
            }

            return (newCharLog, newCharEvents);
        }

        public async Task<GameEvent> HandleSuccessionAsync(
            SaveData save,
            PersonData deadKing,
            string promptName = "sys_succession",
            CancellationToken ct = default)
        {
            _logging.LogForService("EventService", $"Handling succession for king {deadKing.Name} of country {deadKing.CountryId}");

            // 1) Prepare context (filtered)
            var contextData = new
            {
                game = save.Game,
                person = save.Person,
                country = save.Country.Values.Where(c => c.Id == deadKing.CountryId).ToList(),
                army = save.Army.Where(a => a.CountryId == deadKing.CountryId).ToList()
            };
            var contextJson = JsonConvert.SerializeObject(contextData);

            // 2) Load instruction
            var systemInstruction = LoadCachedPrompt(promptName);
            var prompt = systemInstruction + "\n\nData Context:\n" + contextJson;

            // 3) Call LLM
            try
            {
                var response = await _llmService.AskTextAsync(prompt, ct: ct);
                var content = response.Choices[0].Message.Content;

                // 4) Parse result
                // Format A: GameEvent JSON
                // Format B: { "person": [...], "event": {...} }  (or similar)
                var json = ExtractJsonObject(content);
                if (string.IsNullOrEmpty(json)) return null;

                // Try Format B first
                try
                {
                    var obj = JsonConvert.DeserializeObject<Dictionary<string, object>>(json);
                    if (obj != null && obj.ContainsKey("person") && obj.ContainsKey("event"))
                    {
                        return ProcessFormatB(save, json);
                    }
                }
                catch
                {
                    // ignore and fall back
                }

                // Try Format A (GameEvent)
                try
                {
                    return JsonConvert.DeserializeObject<GameEvent>(json);
                }
                catch
                {
                    return null;
                }
            }
            catch (Exception ex)
            {
                _logging.LogErrorForService("EventService", "Error handling succession", ex);
                return null;
            }
        }

        public async Task<List<GameEvent>> HandleDeathEventsAsync(
            SaveData save,
            List<PersonData> deadPersons,
            string promptName = "sys_funeral",
            CancellationToken ct = default)
        {
            var events = new List<GameEvent>();
            if (deadPersons == null || deadPersons.Count == 0) return events;

            var systemInstruction = LoadCachedPrompt(promptName);

            // Group dead persons by country (efficiency)
            var deadByCountry = deadPersons.GroupBy(p => p.CountryId);

            foreach (var group in deadByCountry)
            {
                ct.ThrowIfCancellationRequested();

                var countryId = group.Key;
                var deadInThisCountry = group.ToList();
                var deadIds = deadInThisCountry.Select(p => p.Id).ToHashSet();

                _logging.LogForService("EventService", $"Generating funeral events for {deadInThisCountry.Count} people in country {countryId}");

                // Context rules:
                // - include all person & army from that country
                // - include alive persons + those who died this turn (deadIds)
                var countryData = save.Country.Values.Where(c => c.Id == countryId).ToList();

                var personData = save.Person
                    .Where(p => p.CountryId == countryId && (p.IsAlive || deadIds.Contains(p.Id)))
                    .ToList();

                var armyData = save.Army.Where(a => a.CountryId == countryId).ToList();

                var contextData = new
                {
                    game = save.Game,
                    country = countryData,
                    person = personData,
                    army = armyData
                };

                var contextJson = JsonConvert.SerializeObject(contextData);

                // IMPORTANT: explicitly tell the LLM who died
                var deadNames = string.Join(", ", deadInThisCountry.Select(p => $"{p.Name} (ID: {p.Id})"));

                var prompt =
                    systemInstruction +
                    "\n\nData Context:\n" + contextJson +
                    "\n\nTask: Generate funeral events for the following recently deceased people: " + deadNames + ".";

                try
                {
                    var response = await _llmService.AskTextAsync(prompt, ct: ct);
                    var content = response.Choices[0].Message.Content;

                    var jsonBlocks = ExtractJsonBlocks(content);
                    foreach (var json in jsonBlocks)
                    {
                        try
                        {
                            var gameEvent = JsonConvert.DeserializeObject<GameEvent>(json);
                            if (gameEvent != null) events.Add(gameEvent);
                        }
                        catch (Exception ex)
                        {
                            _logging.LogErrorForService("EventService", "Failed to parse funeral event JSON", ex);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logging.LogErrorForService("EventService", $"Error generating funeral events for country {countryId}", ex);
                }
            }

            return events;
        }

        // -------------------------
        // Helpers
        // -------------------------

        private string ExtractJsonObject(string content)
        {
            var start = content.IndexOf('{');
            var end = content.LastIndexOf('}');
            if (start != -1 && end != -1 && end > start)
                return content.Substring(start, end - start + 1);
            return "";
        }

        private List<string> ExtractJsonBlocks(string content)
        {
            var blocks = new List<string>();
            var bracketDepth = 0;
            var startIndex = -1;

            for (int i = 0; i < content.Length; i++)
            {
                if (content[i] == '{')
                {
                    if (bracketDepth == 0) startIndex = i;
                    bracketDepth++;
                }
                else if (content[i] == '}')
                {
                    bracketDepth--;
                    if (bracketDepth == 0 && startIndex != -1)
                    {
                        blocks.Add(content.Substring(startIndex, i - startIndex + 1));
                        startIndex = -1;
                    }
                }
            }

            return blocks;
        }

        /// <summary>
        /// Succession Format B: { "person": [PersonData...], "event": GameEvent }
        /// Uses Newtonsoft to avoid System.Text.Json dependency.
        /// </summary>
        private GameEvent ProcessFormatB(SaveData save, string json)
        {
            try
            {
                var root = JsonConvert.DeserializeObject<SuccessionFormatB>(json);
                if (root == null) return null;

                if (root.Person != null && root.Person.Count > 0)
                {
                    var maxId = save.Person.Any() ? save.Person.Max(p => p.Id) : 0;
                    foreach (var newPerson in root.Person)
                    {
                        maxId++;
                        newPerson.Id = maxId;
                        save.Person.Add(newPerson);
                        _logging.LogForService("EventService", $"Succession created new person: {newPerson.Name}");
                    }
                }

                return root.Event;
            }
            catch (Exception ex)
            {
                _logging.LogErrorForService("EventService", "Failed to process succession Format B", ex);
                return null;
            }
        }

        private sealed class SuccessionFormatB
        {
            [JsonProperty("person")]
            public List<PersonData> Person { get; set; }

            [JsonProperty("event")]
            public GameEvent Event { get; set; }
        }

    }
}
