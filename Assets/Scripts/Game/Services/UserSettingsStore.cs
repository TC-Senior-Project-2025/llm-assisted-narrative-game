using System;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

namespace Game.Services
{
    [Serializable]
    public class UserSettings
    {
        [JsonProperty("apiKey")] public string ApiKey { get; set; } = "";
    }

    public static class UserSettingsStore
    {
        private static readonly string Dir = Path.Combine(Application.persistentDataPath, "settings");
        private static readonly string FilePath = Path.Combine(Dir, "user_settings.json");

        private static UserSettings _cache;

        public static UserSettings Load()
        {
            if (_cache != null) return _cache;

            try
            {
                if (!File.Exists(FilePath))
                    return _cache = new UserSettings();

                var json = File.ReadAllText(FilePath);
                return _cache = JsonConvert.DeserializeObject<UserSettings>(json) ?? new UserSettings();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Failed to load settings: {e.Message}");
                return _cache = new UserSettings();
            }
        }

        public static void Save(UserSettings settings)
        {
            try
            {
                Directory.CreateDirectory(Dir);
                var json = JsonConvert.SerializeObject(settings, Formatting.Indented);
                File.WriteAllText(FilePath, json);
                _cache = settings;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Failed to save settings: {e.Message}");
            }
        }

        public static string GetApiKey() => Load().ApiKey ?? "";

        public static void SetApiKey(string apiKey)
        {
            var s = Load();
            s.ApiKey = apiKey?.Trim() ?? "";
            Save(s);
        }

        public static void ClearApiKey()
        {
            var s = Load();
            s.ApiKey = "";
            Save(s);
        }
    }
}
