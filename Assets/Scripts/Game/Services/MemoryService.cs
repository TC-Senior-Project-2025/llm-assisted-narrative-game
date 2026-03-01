using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Game.Services.Events;
using Game.Services.Llm;
using Game.Services.Saves;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq; // Used for JArray/JObject if needed, though we use dynamic or specific types
using UnityEngine;

namespace Game.Services
{
    public class MemoryService
    {
        private readonly LlmService _llmService;

        private string _systemInstruction;

        public MemoryService(LlmService llmService)
        {
            _llmService = llmService;
            _systemInstruction = ResourcesService.LoadPrompt("sys_history");
        }

        public async Task<Dictionary<string, List<Dictionary<string, object>>>> GenerateHistoryUpdates(SaveData save, List<GameEvent> events, List<EventChoice> choices)
        {
            // --- 1. Identify Relevant IDs ---
            var relevantCountryIds = new HashSet<int>();
            var relevantPersonIds = new HashSet<int>();
            var relevantCommanderyIds = new HashSet<int>();
            var relevantArmyIds = new HashSet<int>();

            // Always include Player Country
            if (save.Country.TryGetValue(save.Game.PlayerCountryId, out var playerCountry))
            {
                relevantCountryIds.Add(playerCountry.Id);
            }

            // Collect IDs from Events (EventCountry, RelatedCountryIds, and Outcomes)
            foreach (var evt in events)
            {
                if (evt.EventCountry.HasValue && evt.EventCountry.Value != 0)
                    relevantCountryIds.Add(evt.EventCountry.Value);

                if (evt.RelatedCountryIds != null)
                    foreach (var cid in evt.RelatedCountryIds)
                        relevantCountryIds.Add(cid);

                // Extract IDs from Outcomes
                if (evt.Outcomes != null)
                {
                    foreach (var outcome in evt.Outcomes)
                    {
                        if (outcome.Effects != null)
                            ExtractIdsFromEffects(outcome.Effects, relevantCountryIds, relevantPersonIds, relevantCommanderyIds, relevantArmyIds);
                    }
                }
            }

            // Collect IDs from Choice Effects
            foreach (var choice in choices)
            {
                if (choice.Effects != null)
                    ExtractIdsFromEffects(choice.Effects, relevantCountryIds, relevantPersonIds, relevantCommanderyIds, relevantArmyIds);
            }

            // --- 2. Filter Save Data ---
            var filteredSave = new
            {
                game = save.Game,
                country = save.Country.Values.Where(c => relevantCountryIds.Contains(c.Id)).ToList(),
                relation = save.Relation.Where(r => relevantCountryIds.Contains(r.SrcCountryId) || relevantCountryIds.Contains(r.DstCountryId)).ToList(),
                person = save.Person.Where(p => relevantPersonIds.Contains(p.Id)).ToList(),
                commandery = save.Commandery.Values.Where(c => relevantCommanderyIds.Contains(c.Id)).ToList(),
                army = save.Army.Where(a => relevantArmyIds.Contains(a.Id) || relevantCountryIds.Contains(a.CountryId)).ToList()
            };

            // --- 3. Construct Event Context ---
            var eventList = new List<object>();
            int count = Math.Min(events.Count, choices.Count);
            for (int i = 0; i < count; i++)
            {
                eventList.Add(new
                {
                    event_name = events[i].EventName,
                    event_desc = events[i].EventDesc,
                    choice_chosen = new[]
                    {
                        new
                        {
                            choice_name = choices[i].ChoiceName,
                            choice_desc = choices[i].ChoiceDesc,
                            effects = choices[i].Effects
                        }
                    }
                });
            }

            // Prepare context
            var context = new
            {
                game_stats = new[] { filteredSave },
                events = eventList
            };

            var contextJson = JsonConvert.SerializeObject(context);

            var response = await _llmService.AskTextAsync(_systemInstruction + "\n\nUser Input:\n" + contextJson);

            if (response == null || response.Choices == null || response.Choices.Count == 0)
            {
                throw new Exception("Null or empty response from LLM");
            }

            var content = response.Choices[0]?.Message?.Content; // LlmService returns LlmResponse with 'response' field

            // Clean JSON
            content = CleanAndExtractJson(content);

            try
            {
                return JsonConvert.DeserializeObject<Dictionary<string, List<Dictionary<string, object>>>>(content) ?? new Dictionary<string, List<Dictionary<string, object>>>();
            }
            catch
            {
                try
                {
                    // Fallback: Check if it's wrapped in an array [ {...} ]
                    var list = JsonConvert.DeserializeObject<List<Dictionary<string, List<Dictionary<string, object>>>>>(content);
                    if (list != null && list.Count > 0)
                    {
                        return list[0];
                    }
                    return new Dictionary<string, List<Dictionary<string, object>>>();
                }
                catch
                {
                    throw new Exception($"Cannot serialize history updates: {content}");
                }
            }
        }

        private string CleanAndExtractJson(string content)
        {
            var start = content.IndexOf('{');
            var end = content.LastIndexOf('}');
            if (start != -1 && end != -1 && end > start)
            {
                return content.Substring(start, end - start + 1);
            }
            return content; // Return original if no brackets found (might be valid json if just a number or string, but unlikely here)
        }

        private void ExtractIdsFromEffects(
            Dictionary<string, List<Dictionary<string, object>>> effects,
            HashSet<int> countryIds,
            HashSet<int> personIds,
            HashSet<int> commanderyIds,
            HashSet<int> armyIds)
        {
            foreach (var table in effects)
            {
                foreach (var effectItem in table.Value)
                {
                    if (effectItem.TryGetValue("id", out var idObj))
                    {
                        int id = 0;
                        // Newtonsoft handling
                        if (idObj is long l) id = (int)l;
                        else if (idObj is int i) id = i;
                        else if (idObj is string s && int.TryParse(s, out var parsedId)) id = parsedId;
                        else
                        {
                            try { id = Convert.ToInt32(idObj); } catch { }
                        }

                        if (id != 0)
                        {
                            switch (table.Key.ToLower())
                            {
                                case "game": break; // Game is a singleton, always included
                                case "country": countryIds.Add(id); break;
                                case "person": personIds.Add(id); break;
                                case "commandery": commanderyIds.Add(id); break;
                                case "army": armyIds.Add(id); break;
                            }
                        }
                    }

                    // Handle Relation IDs (src_country, dst_country)
                    if (table.Key.ToLower() == "relation")
                    {
                        if (effectItem.TryGetValue("src_country", out var srcObj))
                        {
                            try { countryIds.Add(Convert.ToInt32(srcObj)); } catch { }
                        }
                        if (effectItem.TryGetValue("dst_country", out var dstObj))
                        {
                            try { countryIds.Add(Convert.ToInt32(dstObj)); } catch { }
                        }
                    }
                }
            }
        }
    }
}
