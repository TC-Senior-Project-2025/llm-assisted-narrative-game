using System;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Game.Services
{
    public class LoggingService
    {
        private readonly string _logDirectory;
        private readonly string _logFile;
        private readonly string _serviceLogDirectory;
        private const int MaxServiceLogLines = 100;

        public LoggingService()
        {
            // Base directory (safe everywhere)
            _logDirectory = Path.Combine(Application.persistentDataPath, "Logs");

            if (!Directory.Exists(_logDirectory))
                Directory.CreateDirectory(_logDirectory);

            // Service-specific logs directory
            _serviceLogDirectory = Path.Combine(_logDirectory, "Services");
            if (!Directory.Exists(_serviceLogDirectory))
                Directory.CreateDirectory(_serviceLogDirectory);

            // Unix timestamp at app start
            long unixStart = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            // or milliseconds:
            // long unixStart = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            _logFile = Path.Combine(_logDirectory, $"log_{unixStart}.txt");

            Debug.Log($"Writing log to {_logFile}");
        }

        public void Log(string message)
        {
            WriteToFile("INFO", message);
        }

        public void LogWarning(string message)
        {
            WriteToFile("WARNING", message);
        }

        public void LogError(string message, Exception ex = null)
        {
            string logMessage = message;
            if (ex != null)
            {
                logMessage += $"\nException: {ex.Message}\nStack Trace: {ex.StackTrace}";
            }
            WriteToFile("ERROR", logMessage);
        }

        private void WriteToFile(string level, string message)
        {
            try
            {
                string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                string logEntry = $"[{timestamp}] [{level}] {message}{Environment.NewLine}";
                File.AppendAllText(_logFile, logEntry);
            }
            catch (Exception)
            {
                // Fail silently if we can't write to log to avoid crashing the app from the logger
            }
        }

        // --- Service-Specific Logging (with 100-line rotation) ---

        /// <summary>
        /// Logs an INFO message to a dedicated service-specific log file.
        /// The log file is automatically trimmed to keep only the latest 100 lines.
        /// </summary>
        public void LogForService(string serviceName, string message)
        {
            WriteToServiceFile(serviceName, "INFO", message);
        }

        /// <summary>
        /// Logs a WARNING message to a dedicated service-specific log file.
        /// The log file is automatically trimmed to keep only the latest 100 lines.
        /// </summary>
        public void LogWarningForService(string serviceName, string message)
        {
            WriteToServiceFile(serviceName, "WARNING", message);
        }

        /// <summary>
        /// Logs an ERROR message to a dedicated service-specific log file.
        /// The log file is automatically trimmed to keep only the latest 100 lines.
        /// </summary>
        public void LogErrorForService(string serviceName, string message, Exception ex = null)
        {
            string logMessage = message;
            if (ex != null)
            {
                logMessage += $" | Exception: {ex.Message} | Stack: {ex.StackTrace?.Replace(Environment.NewLine, " ")}";
            }
            WriteToServiceFile(serviceName, "ERROR", logMessage);
        }

        private void WriteToServiceFile(string serviceName, string level, string message)
        {
            try
            {
                string logFilePath = Path.Combine(_serviceLogDirectory, $"{serviceName}.log");
                string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                string logEntry = $"[{timestamp}] [{level}] {message}";

                // Read existing lines, append new entry, and trim to 100 lines
                var lines = File.Exists(logFilePath)
                    ? File.ReadAllLines(logFilePath).ToList()
                    : new System.Collections.Generic.List<string>();

                lines.Add(logEntry);

                // Keep only the latest MaxServiceLogLines lines
                if (lines.Count > MaxServiceLogLines)
                {
                    lines = lines.Skip(lines.Count - MaxServiceLogLines).ToList();
                }

                File.WriteAllLines(logFilePath, lines);
            }
            catch (Exception)
            {
                // Fail silently if we can't write to log to avoid crashing the app from the logger
            }
        }

        /// <summary>
        /// Gets the path to a service-specific log file.
        /// </summary>
        public string GetServiceLogPath(string serviceName)
        {
            return Path.Combine(_serviceLogDirectory, $"{serviceName}.log");
        }
    }
}
