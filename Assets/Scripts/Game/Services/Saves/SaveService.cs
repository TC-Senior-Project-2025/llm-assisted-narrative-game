using System.Collections;
using System.Linq;
using UnityEngine;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace Game.Services.Saves
{
    public static class SaveService
    {
        public static SaveData CurrentSave { get; private set; }

        private static readonly JsonSerializerSettings jsonSettings = new()
        {
            ContractResolver = new DefaultContractResolver
            {
                NamingStrategy = new SnakeCaseNamingStrategy()
            }
        };

        public static List<(string, SaveData)> ListSaves()
        {
            var saves = new List<(string, SaveData)>();

            foreach (string path in Directory.GetFiles(Application.streamingAssetsPath, "Data/Saves/*.json"))
            {
                var saveName = Path.GetFileNameWithoutExtension(path);
                var save = Load(saveName);
                saves.Add((saveName, save));
            }

            return saves;
        }

        public static void SetCurrentSave(SaveData save)
        {
            CurrentSave = save;
        }

        public static SaveData Load(string saveName)
        {
            var saveRoot = JsonLoader.Load<SaveRootDto>($"Data/Saves/{saveName}.json");
            var saveData = new SaveData
            {
                Game = saveRoot.Game,
                Country = saveRoot.Country.ToDictionary(c => c.Id, c => c),
                Relation = saveRoot.Relation,
                Person = saveRoot.Person,
                Commandery = saveRoot.Commandery.ToDictionary(c => c.Id, c => c),
                Army = saveRoot.Army,
                Battle = saveRoot.Battle,
                ExtensionData = saveRoot.ExtensionData
            };

            return saveData;
        }

        public static void Save(string saveName, SaveData save)
        {
            var saveRoot = new SaveRootDto
            {
                Game = save.Game,
                Country = save.Country.Values.ToList(),
                Relation = save.Relation,
                Person = save.Person,
                Commandery = save.Commandery.Values.ToList(),
                Army = save.Army,
                Battle = save.Battle,
                ExtensionData = save.ExtensionData
            };

            saveRoot.Game.SaveName = saveName;

            var json = JsonConvert.SerializeObject(saveRoot, Formatting.Indented, jsonSettings);
            string path = Path.Combine(Application.streamingAssetsPath, $"Data/Saves/{saveName}.json");
            File.WriteAllText(path, json);
        }

        private class SaveRootDto
        {
            public GameData Game { get; set; }
            public List<CountryData> Country { get; set; }
            public List<RelationData> Relation { get; set; }
            public List<PersonData> Person { get; set; }
            public List<CommanderyData> Commandery { get; set; }
            public List<ArmyData> Army { get; set; }
            public List<BattleData> Battle { get; set; }

            [Newtonsoft.Json.JsonExtensionData]
            public IDictionary<string, Newtonsoft.Json.Linq.JToken> ExtensionData { get; set; }
        }
    }
}