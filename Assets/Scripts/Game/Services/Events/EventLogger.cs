using System;
using System.IO;
using Game.Services.Saves;
using Newtonsoft.Json;
using UnityEngine;

namespace Game.Services.Events
{
    public class EventLogger
    {
        private readonly string _logDirectory;

        public EventLogger()
        {
            // Base directory (safe everywhere)
            _logDirectory = Path.Combine(Application.persistentDataPath, "Logs", "Events");

            if (!Directory.Exists(_logDirectory))
                Directory.CreateDirectory(_logDirectory);

            Debug.Log($"Writing Event logs to {_logDirectory}");
        }

        public void LogGameEventsAndChoices(SaveData state, GameEventAndChoice[] gameEventAndChoices)
        {
            long unixStart = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var folder = Path.Combine(_logDirectory, $"{unixStart}_{state.Game.SaveName}_turn{state.Game.Turn}");
            Directory.CreateDirectory(folder);

            foreach (var gac in gameEventAndChoices)
            {
                var isPlayerRelatedEvent =
                    gac.GameEvent.EventCountry == state.Game.PlayerCountryId
                    || gac.GameEvent.RelatedCountryIds.Contains(state.Game.PlayerCountryId);

                var eventPath = Path.Combine(folder, $"{SanitizeFileName(gac.GameEvent.EventName)}.txt");
                var eventJson = JsonConvert.SerializeObject(gac.GameEvent, Formatting.Indented);
                var choiceJson = JsonConvert.SerializeObject(gac.ChoiceMade, Formatting.Indented);

                var output = $"Is player-related event?: {isPlayerRelatedEvent}\n\nEvent:\n{eventJson}\n\nChoice made:\n{choiceJson}";

                File.WriteAllText(eventPath, output);
            }
        }

        private string SanitizeFileName(string name, string replacement = "_")
        {
            var invalidChars = Path.GetInvalidFileNameChars();
            var newName = string.Join(replacement, name.Split(invalidChars, StringSplitOptions.RemoveEmptyEntries));
            // Windows does not allow trailing periods in file names.
            return newName.TrimEnd('.');
        }
    }
}