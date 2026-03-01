using System;
using System.Collections;
using System.Collections.Generic;
using Extensions;
using Game.Services.Saves;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Game.Services
{
    public class EffectService
    {
        public static EffectService Main { get; private set; }

        private readonly LoggingService _logging = new();

        public EffectService()
        {
            Main = this;
        }

        public void ApplyEffects(
            SaveData save,
            Dictionary<string, List<Dictionary<string, object>>> effects)
        {
            foreach (var (tableName, modifications) in effects)
            {
                object tableObject = tableName switch
                {
                    "country" => save.Country,
                    "relation" => save.Relation,
                    "person" => save.Person,
                    "commandery" => save.Commandery,
                    "game" => save.Game,
                    "army" => save.Army,
                    _ => null
                };

                if (tableObject == null) continue;

                foreach (var mod in modifications)
                {
                    if (!mod.TryGetValue("id", out var idObj))
                        continue;

                    int id;
                    // ID parsing logic...
                    if (idObj is JToken jt && jt.Type == JTokenType.Integer) id = jt.Value<int>();
                    else if (idObj is int i) id = i;
                    else if (idObj is long l) id = checked((int)l);
                    else if (idObj is string s && int.TryParse(s, out var parsed)) id = parsed;
                    else if (idObj is IConvertible conv) { try { id = conv.ToInt32(System.Globalization.CultureInfo.InvariantCulture); } catch { continue; } }
                    else continue;

                    // 0️⃣ Single Object path (GameData)
                    if (tableObject is GameData gameData)
                    {
                        if (gameData.Id == id)
                        {
                            ApplyModifications(gameData, mod);
                        }
                        continue;
                    }

                    // 1️⃣ Dictionary<int, T> fast path
                    if (tableObject is IDictionary dict)
                    {
                        if (dict.Contains(id))
                        {
                            var targetItem = dict[id];
                            if (targetItem != null)
                                ApplyModifications(targetItem, mod);
                        }
                        continue;
                    }

                    // 2️⃣ List path
                    if (tableObject is IEnumerable list)
                    {
                        object targetItem = null;
                        foreach (var item in list)
                        {
                            var idProp = item.GetType().GetProperty("Id");
                            if (idProp != null && (int)(idProp.GetValue(item) ?? 0) == id)
                            {
                                targetItem = item;
                                break;
                            }
                        }

                        if (targetItem != null)
                            ApplyModifications(targetItem, mod);
                    }
                }
            }

            if (effects.Count > 0)
            {
                var tables = string.Join(", ", effects.Keys);
                _logging.LogForService("EffectService", $"Applied effects to tables: {tables}");
            }
        }

        private void ApplyModifications(object target, Dictionary<string, object> mods)
        {
            var type = target.GetType();

            foreach (var (key, value) in mods)
            {
                if (key == "id") continue;

                string stringVal = value switch
                {
                    JToken jt => jt.Type == JTokenType.String
                        ? jt.Value<string>()
                        : jt.ToString(),
                    string s => s,
                    _ => null
                };

                if (stringVal == null) continue;

                var propName = ToPascalCase(key);
                var prop = type.GetProperty(propName);
                if (prop == null) continue;

                if (prop.PropertyType == typeof(int) || prop.PropertyType == typeof(int?))
                {
                    int current = (int)(prop.GetValue(target) ?? 0);

                    if (stringVal.StartsWith("+") &&
                        int.TryParse(stringVal[1..], out var add))
                        current += add;
                    else if (stringVal.StartsWith("-") &&
                        int.TryParse(stringVal[1..], out var sub))
                        current -= sub;
                    else if (int.TryParse(stringVal, out var set))
                        current = set;

                    prop.SetValue(target, current);
                }
                else if (prop.PropertyType == typeof(string))
                {
                    prop.SetValue(target, stringVal);
                }
            }
        }

        private string ToPascalCase(string snake)
        {
            return System.Globalization.CultureInfo
                .CurrentCulture
                .TextInfo
                .ToTitleCase(snake.Replace("_", " "))
                .Replace(" ", "");
        }
    }
}
