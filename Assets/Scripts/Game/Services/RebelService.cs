using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Game.Services.Events;
using Game.Services.Llm;
using Game.Services.Saves;
using Newtonsoft.Json;

namespace Game.Services
{
    public class RebelService
    {
        private readonly LlmService _llmService;
        private readonly LoggingService _logging;

        private readonly string _prompt;

        public RebelService(LlmService llmService, LoggingService logging)
        {
            _llmService = llmService;
            _logging = logging;
            _prompt = ResourcesService.LoadPrompt("sys_rebel");
        }

        public async Task<GameEvent> HandleRebellion(SaveData save, int countryId, int commanderyId, string rebellionType, int? personId = null, int? armyId = null, CancellationToken ct = default)
        {
            // 1. Prepare Context
            var country = save.Country.GetValueOrDefault(countryId);
            var commandery = save.Commandery.GetValueOrDefault(commanderyId);
            PersonData person = null;
            if (personId.HasValue)
            {
                person = save.Person.Find(p => p.Id == personId.Value);
            }

            ArmyData defectingArmy = null;
            if (rebellionType == "General" && armyId.HasValue)
            {
                defectingArmy = save.Army.Find(a => a.Id == armyId.Value);
            }

            if (country == null || commandery == null)
            {
                _logging.LogErrorForService("RebelService", $"Invalid country {countryId} or commandery {commanderyId} for rebellion.");
                return null;
            }

            var contextData = new
            {
                rebellion_type = rebellionType,
                target_country = country,
                target_commandery = commandery,
                leader = person,
                defecting_army = defectingArmy
            };

            var contextJson = JsonConvert.SerializeObject(contextData);
            var prompt = _prompt + "\n\nData Context:\n" + contextJson;

            // 2. Call LLM
            try
            {
                var response = await _llmService.AskTextAsync(prompt, ct: ct);
                var content = response.Choices[0].Message.Content;

                // 3. Parse Result
                var json = ExtractJsonObject(content);
                if (string.IsNullOrEmpty(json))
                {
                    json = ExtractJsonBlock(content);
                }

                if (string.IsNullOrEmpty(json)) return null;

                var result = JsonConvert.DeserializeObject<RebelGenerationResult>(json);

                if (result == null) return null;

                // 4. ID Generation & Integration
                int newCountryId = -1;

                // Add Country
                if (result.Country != null && result.Country.Count > 0)
                {
                    foreach (var c in result.Country)
                    {
                        var maxCid = save.Country.Count > 0 ? save.Country.Keys.Max() : 0;
                        newCountryId = maxCid + 1;
                        c.Id = newCountryId;
                        c.GameId = save.Game.Id;
                        save.Country[newCountryId] = c;
                    }
                }

                if (newCountryId != -1)
                {
                    // Logic: Noble Rebellion -> Commandery Defects
                    if (rebellionType == "Noble")
                    {
                        commandery.CountryId = newCountryId;
                    }

                    // Create hostile relation with origin country (-100/-100)
                    save.Relation.Add(new RelationData
                    {
                        SrcCountryId = newCountryId,
                        DstCountryId = countryId,
                        Value = -100,
                        IsAtWar = true,
                        IsAllied = false
                    });
                    save.Relation.Add(new RelationData
                    {
                        SrcCountryId = countryId,
                        DstCountryId = newCountryId,
                        Value = -100,
                        IsAtWar = true,
                        IsAllied = false
                    });

                    // Create relations with other nations (0/-50)
                    foreach (var otherCountryId in save.Country.Keys)
                    {
                        if (otherCountryId == newCountryId || otherCountryId == countryId) continue;

                        save.Relation.Add(new RelationData
                        {
                            SrcCountryId = newCountryId,
                            DstCountryId = otherCountryId,
                            Value = 0,
                            IsAtWar = true,
                            IsAllied = false
                        });

                        save.Relation.Add(new RelationData
                        {
                            SrcCountryId = otherCountryId,
                            DstCountryId = newCountryId,
                            Value = -50,
                            IsAtWar = true,
                            IsAllied = false
                        });
                    }

                    // Logic: General Rebellion -> Army Defects
                    if (rebellionType == "General" && defectingArmy != null)
                    {
                        defectingArmy.CountryId = newCountryId;
                    }

                    // Manpower / Population Deduction Logic
                    var newCountryData = save.Country[newCountryId];
                    int newTotalManpower = newCountryData.Manpower;

                    // Also account for new armies created (if they consume manpower?)
                    // Assuming the 'newCountryData.Manpower' is the *initial* manpower pool.
                    // The user prompt implies: "manpower -= newcountry manpower"
                    // It doesn't explicitly mention the new armies' size, but often armies drain manpower on creation.
                    // Let's stick to the user request strictly: "origin's manpower -= newcountry manpower".

                    if (rebellionType == "Noble" || rebellionType == "General")
                    {
                        country.Manpower = Math.Max(0, country.Manpower - newTotalManpower);
                        person.CountryId = newCountryId;
                    }
                    else
                    {
                        // Commoner / Peasant: Deduct from commandery population
                        commandery.Population = Math.Max(0, commandery.Population - newTotalManpower);
                        commandery.Unrest -= 3;
                    }
                }

                // Add Army
                if (result.Army != null && result.Army.Count > 0 && newCountryId != -1 && rebellionType != "General")
                {
                    var maxAid = save.Army.Count > 0 ? save.Army.Max(a => a.Id) : 0;
                    foreach (var a in result.Army)
                    {
                        maxAid++;
                        a.Id = maxAid;
                        a.CountryId = newCountryId;
                        a.GameId = save.Game.Id;

                        if (a.LocationId == 0) a.LocationId = commanderyId;

                        save.Army.Add(a);
                    }
                }

                // Process Event
                if (result.Event != null && result.Event.Count > 0)
                {
                    var evt = result.Event[0];
                    if (evt.EventCountry == null || evt.EventCountry == 0)
                    {
                        evt.EventCountry = countryId;
                    }

                    if (evt.RelatedCountryIds == null) evt.RelatedCountryIds = new List<int>();
                    if (newCountryId != -1) evt.RelatedCountryIds.Add(newCountryId);

                    return evt;
                }

                return null;
            }
            catch (Exception ex)
            {
                _logging.LogErrorForService("RebelService", "Failed to generate rebellion", ex);
                return null;
            }
        }

        private string ExtractJsonObject(string content)
        {
            int startObj = content.IndexOf('{');
            int startArr = content.IndexOf('[');

            if (startObj == -1 && startArr == -1) return null;

            int start = (startObj != -1 && (startArr == -1 || startObj < startArr)) ? startObj : startArr;
            char endChar = content[start] == '{' ? '}' : ']';
            int end = content.LastIndexOf(endChar);

            if (end > start)
            {
                return content.Substring(start, end - start + 1);
            }
            return null;
        }

        private string ExtractJsonBlock(string content)
        {
            if (content.Contains("```json"))
            {
                var start = content.IndexOf("```json") + 7;
                var end = content.IndexOf("```", start);
                if (end != -1) return content.Substring(start, end - start).Trim();
            }
            return ExtractJsonObject(content);
        }

        private class RebelGenerationResult
        {
            [JsonProperty("country")] public List<CountryData> Country { get; set; }
            [JsonProperty("army")] public List<ArmyData> Army { get; set; }
            [JsonProperty("event")] public List<GameEvent> Event { get; set; }
        }
    }
}
