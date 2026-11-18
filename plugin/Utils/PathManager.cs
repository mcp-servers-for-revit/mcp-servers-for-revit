using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using revit_mcp_plugin.Configuration;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace revit_mcp_plugin.Utils
{
    public static class PathManager
    {
        /// <summary>
        /// Gets the root application data directory
        /// </summary>
        public static string GetAppDataDirectoryPath()
        {
            string applicationPath = System.Reflection.Assembly.GetExecutingAssembly().Location;
            string applicationDirectory = Path.GetDirectoryName(applicationPath);

            return applicationDirectory;
        }
        /// <summary>
        /// Gets the path to the Commands directory
        /// </summary>
        public static string GetCommandsDirectoryPath()
        {
            string appDataDirectory = GetAppDataDirectoryPath();
            string commandsDirectory = Path.Combine(appDataDirectory, "Commands");

            EnsureDirectoryExists(commandsDirectory);

            return commandsDirectory;
        }
        /// <summary>
        /// Gets the path to the Logs directory
        /// </summary>
        public static string GetLogsDirectoryPath()
        {
            string appDataDirectory = GetAppDataDirectoryPath();
            string logsDirectory = Path.Combine(appDataDirectory, "Logs");

            EnsureDirectoryExists(logsDirectory);

            return logsDirectory;
        }
        /// <summary>
        /// Gets the path to the command registry file.
        /// If the file doesn't exist, creates it with default content.
        /// </summary>
        /// <param name="createIfNotExists">Whether to create a default file if it doesn't exist (default: true)</param>
        /// <returns>Path to the command registry file</returns>
        public static string GetCommandRegistryFilePath(bool createIfNotExists = true)
        {
            string commandsDirectory = GetCommandsDirectoryPath();
            string registryFilePath = Path.Combine(commandsDirectory, "commandRegistry.json");

            if (createIfNotExists && !File.Exists(registryFilePath))
            {
                CreateDefaultCommandRegistryFile(registryFilePath);
            }

            return registryFilePath;
        }
        /// <summary>
        /// Creates a default command registry file by scanning command.json files
        /// in Commands subdirectories. All discovered commands are disabled by
        /// default so commands are always an explicit user opt-in.
        /// </summary>
        /// <param name="filePath">Path where to create the file</param>
        private static void CreateDefaultCommandRegistryFile(string filePath)
        {
            try
            {
                string commandsDirectory = GetCommandsDirectoryPath();
                var commands = new List<CommandConfig>();

                if (Directory.Exists(commandsDirectory))
                {
                    foreach (var setDir in Directory.GetDirectories(commandsDirectory))
                    {
                        if (Path.GetFileName(setDir).StartsWith("."))
                            continue;

                        string commandJsonPath = Path.Combine(setDir, "command.json");
                        if (!File.Exists(commandJsonPath))
                            continue;

                        string setName = Path.GetFileName(setDir);
                        string commandJson = File.ReadAllText(commandJsonPath);
                        var commandSetData = JObject.Parse(commandJson);

                        var devToken = commandSetData["developer"];
                        var developer = devToken != null
                            ? devToken.ToObject<DeveloperInfo>()
                            : new DeveloperInfo();

                        var versionDirs = Directory.GetDirectories(setDir)
                            .Select(Path.GetFileName)
                            .Where(name => int.TryParse(name, out _))
                            .ToList();

                        var commandsArray = commandSetData["commands"] as JArray;
                        if (commandsArray == null) continue;

                        foreach (var cmdToken in commandsArray)
                        {
                            string commandName = cmdToken["commandName"]?.ToString();
                            string description = cmdToken["description"]?.ToString() ?? "";
                            string assemblyPath = cmdToken["assemblyPath"]?.ToString();

                            if (string.IsNullOrEmpty(commandName)) continue;

                            var supportedVersions = new List<string>();
                            string dllBasePath = null;

                            foreach (var version in versionDirs)
                            {
                                string versionDir = Path.Combine(setDir, version);
                                string dllPath = !string.IsNullOrEmpty(assemblyPath)
                                    ? Path.Combine(versionDir, assemblyPath)
                                    : Directory.GetFiles(versionDir, "*.dll").FirstOrDefault();

                                if (dllPath != null && File.Exists(dllPath))
                                {
                                    if (dllBasePath == null)
                                    {
                                        string dllName = !string.IsNullOrEmpty(assemblyPath)
                                            ? assemblyPath
                                            : Path.GetFileName(dllPath);
                                        dllBasePath = Path.Combine(setName, "{VERSION}", dllName);
                                    }
                                    supportedVersions.Add(version);
                                }
                            }

                            if (supportedVersions.Count > 0 && dllBasePath != null)
                            {
                                commands.Add(new CommandConfig
                                {
                                    CommandName = commandName,
                                    Description = description,
                                    AssemblyPath = dllBasePath,
                                    Enabled = false,
                                    SupportedRevitVersions = supportedVersions.ToArray(),
                                    Developer = developer
                                });
                            }
                        }
                    }
                }

                var registry = new { commands };
                string jsonContent = JsonConvert.SerializeObject(registry, Formatting.Indented);
                File.WriteAllText(filePath, jsonContent);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error creating default command registry file: {ex.Message}");
                var fallback = new { commands = new List<CommandConfig>() };
                File.WriteAllText(filePath, JsonConvert.SerializeObject(fallback, Formatting.Indented));
            }
        }
        /// <summary>
        /// Ensures that the specified directory exists
        /// </summary>
        /// <param name="directoryPath">The path to check and create if needed</param>
        private static void EnsureDirectoryExists(string directoryPath)
        {
            if (!Directory.Exists(directoryPath))
            {
                Directory.CreateDirectory(directoryPath);
            }
        }
    }
}
