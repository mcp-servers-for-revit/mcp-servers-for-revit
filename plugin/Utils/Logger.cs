using RevitMCPSDK.API.Interfaces;
using System;
using System.IO;

namespace revit_mcp_plugin.Utils
{
    public class Logger : ILogger
    {
        private readonly string _logFilePath;
        private readonly object _fileLock = new object();
        private LogLevel _currentLogLevel = LogLevel.Info;

        public Logger()
        {
            _logFilePath = Path.Combine(PathManager.GetLogsDirectoryPath(), $"mcp_{DateTime.Now:yyyyMMdd}.log");

        }

        public void Log(LogLevel level, string message, params object[] args)
        {
            if (level < _currentLogLevel)
                return;

            string formattedMessage = args.Length > 0 ? string.Format(message, args) : message;
            string logEntry = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level}] {formattedMessage}";

            // Output to debug window.
            System.Diagnostics.Debug.WriteLine(logEntry);

            // Write to the logfile (thread-safe).
            try
            {
                lock (_fileLock)
                {
                    File.AppendAllText(_logFilePath, logEntry + Environment.NewLine);
                }
            }
            catch
            {
                // If writing to the logfile fails, do not throw an exception.
            }
        }

        public void Debug(string message, params object[] args)
        {
            Log(LogLevel.Debug, message, args);
        }

        public void Info(string message, params object[] args)
        {
            Log(LogLevel.Info, message, args);
        }

        public void Warning(string message, params object[] args)
        {
            Log(LogLevel.Warning, message, args);
        }

        public void Error(string message, params object[] args)
        {
            Log(LogLevel.Error, message, args);
        }
    }
}
