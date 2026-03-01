using System.Collections.Generic;
using Newtonsoft.Json;

namespace Game.Services.Events
{
    public class GameEvent
    {
        [JsonProperty("event_country")] public int? EventCountry { get; set; }
        [JsonProperty("event_name")] public string EventName { get; set; } = "";
        [JsonProperty("event_desc")] public string EventDesc { get; set; } = "";
        [JsonProperty("choices")] public List<EventChoice> Choices { get; set; } = new();
        [JsonProperty("outcome")] public List<EventOutcome> Outcomes { get; set; } = new();
        [JsonProperty("related_countries")] public List<int> RelatedCountryIds { get; set; } = new();
    }

    public class EventChoice
    {
        [JsonProperty("choice_name")] public string ChoiceName { get; set; } = "";
        [JsonProperty("choice_desc")] public string ChoiceDesc { get; set; } = "";
        [JsonProperty("effects")] public Dictionary<string, List<Dictionary<string, object>>> Effects { get; set; } = new();
    }

    public class EventOutcome
    {
        [JsonProperty("outcome_name")] public string OutcomeName { get; set; } = "";
        [JsonProperty("outcome_desc")] public string OutcomeDesc { get; set; } = "";
        [JsonProperty("effects")] public Dictionary<string, List<Dictionary<string, object>>> Effects { get; set; } = new();
    }

    public class GameEventAndChoice
    {
        public GameEvent GameEvent;
        public EventChoice ChoiceMade;
    }
}