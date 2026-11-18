using System;
using System.Globalization;
using System.IO;

namespace revit_mcp_plugin.Helpers
{
    /// <summary>
    /// Thread-safe structured file logger with automatic 7-day log rotation.
    /// All writes are wrapped in try/catch — this logger will never crash the host.
    /// </summary>
    public static class McpLogger
    {
        private static readonly object _lock = new object();
        private static string _logDirectory;
        private static string _logFile;

        /// <summary>
        /// Returns the current log directory path, or null if not yet initialized.
        /// </summary>
        public static string LogDirectory => _logDirectory;

        /// <summary>
        /// Initializes the logger: sets up the log directory, creates the daily log file path,
        /// and cleans up log files older than 7 days.
        /// </summary>
        /// <param name="pluginDir">The plugin's installation directory.</param>
        public static void Initialize(string pluginDir)
        {
            try
            {
                _logDirectory = Path.Combine(pluginDir, "logs");
                if (!Directory.Exists(_logDirectory))
                {
                    Directory.CreateDirectory(_logDirectory);
                }

                var today = DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                _logFile = Path.Combine(_logDirectory, $"mcp-{today}.log");

                CleanOldLogs(7);
            }
            catch
            {
                // Never crash — if initialization fails, logging will silently no-op.
            }
        }

        /// <summary>
        /// Logs an informational message.
        /// </summary>
        public static void Info(string component, string message)
        {
            Write("INFO", component, message);
        }

        /// <summary>
        /// Logs a warning message.
        /// </summary>
        public static void Warn(string component, string message)
        {
            Write("WARN", component, message);
        }

        /// <summary>
        /// Logs an error message.
        /// </summary>
        public static void Error(string component, string message)
        {
            Write("ERROR", component, message);
        }

        /// <summary>
        /// Logs an error message along with exception details.
        /// </summary>
        public static void Error(string component, string message, Exception ex)
        {
            if (ex != null)
            {
                Write("ERROR", component, $"{message} | {ex.GetType().Name}: {ex.Message}");
            }
            else
            {
                Write("ERROR", component, message);
            }
        }

        /// <summary>
        /// Thread-safe log writer. Appends a single formatted line to the current log file.
        /// Never throws — all exceptions are silently swallowed.
        /// </summary>
        private static void Write(string level, string component, string message)
        {
            try
            {
                if (string.IsNullOrEmpty(_logFile))
                    return;

                var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
                var line = $"[{timestamp}] [{level}] [{component}] {message}{Environment.NewLine}";

                lock (_lock)
                {
                    File.AppendAllText(_logFile, line);
                }
            }
            catch
            {
                // Never crash — if a write fails, we silently skip it.
            }
        }

        /// <summary>
        /// Deletes log files older than the specified number of days. Best-effort only.
        /// </summary>
        private static void CleanOldLogs(int keepDays)
        {
            try
            {
                if (string.IsNullOrEmpty(_logDirectory) || !Directory.Exists(_logDirectory))
                    return;

                var cutoff = DateTime.Now.AddDays(-keepDays);
                var files = Directory.GetFiles(_logDirectory, "mcp-*.log");

                foreach (var file in files)
                {
                    try
                    {
                        var lastWrite = File.GetLastWriteTime(file);
                        if (lastWrite < cutoff)
                        {
                            File.Delete(file);
                        }
                    }
                    catch
                    {
                        // Best-effort: skip files that cannot be deleted.
                    }
                }
            }
            catch
            {
                // Best-effort: if enumeration fails, do nothing.
            }
        }
    }
}
