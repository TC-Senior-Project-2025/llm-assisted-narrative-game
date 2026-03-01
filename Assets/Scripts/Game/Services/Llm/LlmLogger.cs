using System;
using System.IO;
using UnityEngine;

namespace Game.Services.Llm
{
    public class LlmLogger
    {
        private readonly string _logDirectory;

        public enum LogType
        {
            Single,
            Stream
        }

        public LlmLogger()
        {
            // Base directory (safe everywhere)
            _logDirectory = Path.Combine(Application.persistentDataPath, "Logs", "LLM");

            if (!Directory.Exists(_logDirectory))
                Directory.CreateDirectory(_logDirectory);

            Debug.Log($"Writing LLM logs to {_logDirectory}");
        }

        public void Log(LogType type, string prompt, string response)
        {
            long unixStart = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var folder = Path.Combine(_logDirectory, $"{unixStart}_{type}");
            Directory.CreateDirectory(folder);

            var promptPath = Path.Combine(folder, "prompt.txt");
            var resPath = Path.Combine(folder, "response.txt");

            File.WriteAllText(promptPath, prompt);
            File.WriteAllText(resPath, response);
        }
    }
}