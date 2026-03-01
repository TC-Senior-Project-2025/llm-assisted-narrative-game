using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Game.Services.Saves
{
    public class SaveData
    {
        [JsonProperty("game")] public GameData Game { get; set; }
        [JsonProperty("country")] public Dictionary<int, CountryData> Country { get; set; } = new Dictionary<int, CountryData>();
        [JsonProperty("relation")] public List<RelationData> Relation { get; set; } = new List<RelationData>();
        [JsonProperty("person")] public List<PersonData> Person { get; set; } = new List<PersonData>();
        [JsonProperty("commandery")] public Dictionary<int, CommanderyData> Commandery { get; set; } = new Dictionary<int, CommanderyData>();
        [JsonProperty("army")] public List<ArmyData> Army { get; set; } = new List<ArmyData>();
        [JsonProperty("battle")] public List<BattleData> Battle { get; set; } = new List<BattleData>();

        [JsonExtensionData]
        public IDictionary<string, JToken> ExtensionData { get; set; }
    }

    public class GameData
    {
        [JsonProperty("id")] public int Id { get; set; }
        [JsonProperty("save_name")] public string SaveName { get; set; }
        [JsonProperty("turn")] public int Turn { get; set; }
        [JsonProperty("current_year")] public int CurrentYear { get; set; }
        [JsonProperty("current_month")] public int CurrentMonth { get; set; }
        [JsonProperty("months_per_turn")] public int MonthsPerTurn { get; set; }
        [JsonProperty("player_country_id")] public int PlayerCountryId { get; set; }
        [JsonProperty("history")] public string History { get; set; } = "";

        [JsonExtensionData]
        public IDictionary<string, JToken> ExtensionData { get; set; }
    }

    public class CountryData
    {
        [JsonProperty("id")] public int Id { get; set; }
        [JsonProperty("game_id")] public int GameId { get; set; }
        [JsonProperty("code")] public string Code { get; set; }
        [JsonProperty("name")] public string Name { get; set; }
        [JsonProperty("efficiency")] public int Efficiency { get; set; }
        [JsonProperty("treasury")] public int Treasury { get; set; }
        [JsonProperty("stability")] public int Stability { get; set; }
        [JsonProperty("manpower")] public int Manpower { get; set; }
        [JsonProperty("prestige")] public int Prestige { get; set; }
        [JsonProperty("mission_point")] public int MissionPoint { get; set; }
        [JsonProperty("last_turn_gold_income")] public int LastTurnGoldIncome { get; set; }
        [JsonProperty("last_turn_manpower_income")] public int LastTurnManpowerIncome { get; set; }
        [JsonProperty("last_turn_garrison_upkeep")] public int LastTurnGarrisonUpkeep { get; set; }
        [JsonProperty("last_turn_army_upkeep")] public int LastTurnArmyUpkeep { get; set; }
        [JsonProperty("history")] public string History { get; set; } = "";

        [JsonIgnore] public HashSet<int> BorderingCountryIds { get; set; } = new HashSet<int>();

        [JsonExtensionData]
        public IDictionary<string, JToken> ExtensionData { get; set; }
    }

    public class RelationData
    {
        [JsonProperty("src_country")] public int SrcCountryId { get; set; }
        [JsonProperty("dst_country")] public int DstCountryId { get; set; }
        [JsonProperty("value")] public int Value { get; set; }
        [JsonProperty("is_allied")] public bool IsAllied { get; set; }
        [JsonProperty("is_at_war")] public bool IsAtWar { get; set; }
        [JsonProperty("history")] public string History { get; set; } = "";

        [JsonExtensionData]
        public IDictionary<string, JToken> ExtensionData { get; set; }
    }

    public class PersonData
    {
        [JsonProperty("id")] public int Id { get; set; }
        [JsonProperty("country_id")] public int CountryId { get; set; }
        [JsonProperty("name")] public string Name { get; set; }
        [JsonProperty("age")] public int Age { get; set; }
        [JsonProperty("is_alive")] public bool IsAlive { get; set; }
        [JsonProperty("loyalty")] public int Loyalty { get; set; }
        [JsonProperty("role")] public string Role { get; set; }
        [JsonProperty("stats")] public PersonStats Stats { get; set; }
        [JsonProperty("history")] public string History { get; set; } = "";

        [JsonExtensionData]
        public IDictionary<string, JToken> ExtensionData { get; set; }
    }

    public class PersonStats
    {
        [JsonProperty("morale")] public int? Morale { get; set; }
        [JsonProperty("field_offense")] public int? FieldOffense { get; set; }
        [JsonProperty("field_defense")] public int? FieldDefense { get; set; }
        [JsonProperty("siege_offense")] public int? SiegeOffense { get; set; }
        [JsonProperty("siege_defense")] public int? SiegeDefense { get; set; }

        [JsonExtensionData]
        public IDictionary<string, JToken> ExtensionData { get; set; }
    }

    public class CommanderyData
    {
        [JsonProperty("id")] public int Id { get; set; }
        [JsonProperty("game_id")] public int GameId { get; set; }
        [JsonProperty("country_id")] public int CountryId { get; set; }
        [JsonProperty("commander_id")] public int? CommanderId { get; set; }
        [JsonProperty("code")] public string Code { get; set; }
        [JsonProperty("name")] public string Name { get; set; }
        [JsonProperty("population")] public int Population { get; set; }
        [JsonProperty("wealth")] public int Wealth { get; set; }
        [JsonProperty("unrest")] public int Unrest { get; set; }
        [JsonProperty("defensiveness")] public int Defensiveness { get; set; }
        [JsonProperty("garrisons")] public int Garrisons { get; set; }
        [JsonProperty("neighbors")] public List<int> Neighbors { get; set; } = new List<int>();
        [JsonProperty("history")] public string History { get; set; } = "";

        [JsonIgnore] public List<int> BorderCountryIds { get; set; } = new List<int>();
        [JsonIgnore] public bool IsBorder => BorderCountryIds.Count > 0;

        [JsonExtensionData]
        public IDictionary<string, JToken> ExtensionData { get; set; }
    }

    public class ArmyData
    {
        [JsonProperty("id")] public int Id { get; set; }
        [JsonProperty("game_id")] public int GameId { get; set; }
        [JsonProperty("country_id")] public int CountryId { get; set; }
        [JsonProperty("commander_id")] public int? CommanderId { get; set; }
        [JsonProperty("location_id")] public int LocationId { get; set; }
        [JsonProperty("name")] public string Name { get; set; }
        [JsonProperty("size")] public int Size { get; set; }
        [JsonProperty("morale")] public int Morale { get; set; }

        [JsonProperty("supply")] public int Supply { get; set; }
        [JsonProperty("action_left")] public int ActionLeft { get; set; }
        [JsonProperty("history")] public string History { get; set; } = "";

        [JsonExtensionData]
        public IDictionary<string, JToken> ExtensionData { get; set; }
    }

    public class BattleData
    {
        [JsonProperty("id")] public int Id { get; set; }
        [JsonProperty("phase")] public string Phase { get; set; }
        [JsonProperty("attacker_army")] public List<int> AttackerArmyIds { get; set; } = new List<int>();
        [JsonProperty("defender_army")] public List<int> DefenderArmyIds { get; set; } = new List<int>();
        [JsonProperty("location_id")] public int LocationId { get; set; }
        [JsonProperty("starting_month")] public int StartingMonth { get; set; }
        [JsonProperty("starting_year")] public int StartingYear { get; set; }
        [JsonProperty("attacker_losses")] public int AttackerLosses { get; set; }
        [JsonProperty("defender_losses")] public int DefenderLosses { get; set; }

        [JsonExtensionData]
        public IDictionary<string, JToken> ExtensionData { get; set; }
    }
}
