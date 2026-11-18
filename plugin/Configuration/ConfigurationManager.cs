using Newtonsoft.Json;
using RevitMCPSDK.API.Interfaces;
using revit_mcp_plugin.UI;
using revit_mcp_plugin.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace revit_mcp_plugin.Configuration
{
    public class ConfigurationManager
    {
        private readonly ILogger _logger;
        private readonly string _configPath;

        public FrameworkConfig Config { get; private set; }

        public ConfigurationManager(ILogger logger)
        {
            _logger = logger;

            // Configuration file path.
            _configPath = PathManager.GetCommandRegistryFilePath();
        }

        /// <summary>
        /// <para>Load configuration from a JSON file.</para>
        /// </summary>
        public void LoadConfiguration()
        {
            try
            {
                if (File.Exists(_configPath))
                {
                    string json = File.ReadAllText(_configPath);
                    Config = JsonConvert.DeserializeObject<FrameworkConfig>(json);
                    _logger.Info("Configuration file loaded: {0}", _configPath);
                }
                else
                {
                    _logger.Error("No configuration file found.");
                }
            }
            catch (Exception ex)
            {
                _logger.Error("Failed to load configuration file: {0}", ex.Message);
            }

            if (Config == null)
            {
                Config = new FrameworkConfig();
            }
            if (Config.Commands == null)
            {
                Config.Commands = new List<CommandConfig>();
            }

            // Self-heal: align the registry with the command manifests on disk.
            ReconcileWithManifests();

            // Record the configuration load time.
            _lastConfigLoadTime = DateTime.Now;
        }

        /// <summary>
        /// Writes the configuration back to commandRegistry.json.
        /// </summary>
        public void SaveConfiguration()
        {
            try
            {
                string json = JsonConvert.SerializeObject(Config, Formatting.Indented);
                File.WriteAllText(_configPath, json);
                _logger.Info("Configuration file saved: {0}", _configPath);
            }
            catch (Exception ex)
            {
                _logger.Error("Failed to save configuration file: {0}", ex.Message);
            }
        }

        /// <summary>
        /// <para>Align the in-memory command registry with the per-set command.json
        /// manifests: commands present in a manifest but missing from the registry are
        /// added as enabled:false (existing enabled state untouched), and registry
        /// entries with no corresponding manifest are removed. Writes back only when
        /// something changed.</para>
        /// </summary>
        private void ReconcileWithManifests()
        {
            try
            {
                string commandsDir = PathManager.GetCommandsDirectoryPath();
                if (!Directory.Exists(commandsDir))
                {
                    _logger.Warning("Commands directory missing; registry reconcile skipped: {0}", commandsDir);
                    return;
                }

                // 1. Collect manifest commands: commandName -> materialized registry entry (including the version list probed from DLL directories).
                var manifestCommands = new Dictionary<string, CommandConfig>(StringComparer.OrdinalIgnoreCase);
                foreach (var setDir in Directory.GetDirectories(commandsDir))
                {
                    if (Path.GetFileName(setDir).StartsWith(".")) continue;
                    string commandJsonPath = Path.Combine(setDir, "command.json");
                    if (!File.Exists(commandJsonPath)) continue;

                    CommandJson manifest;
                    try
                    {
                        manifest = JsonConvert.DeserializeObject<CommandJson>(File.ReadAllText(commandJsonPath));
                    }
                    catch (Exception ex)
                    {
                        _logger.Warning("Failed to parse command.json ({0}): {1}", commandJsonPath, ex.Message);
                        continue;
                    }
                    if (manifest?.Commands == null) continue;

                    // Probe year subdirectories to check whether each command's DLL actually exists ({VERSION} needs a concrete year).
                    var yearDirs = Directory.GetDirectories(setDir)
                        .Select(Path.GetFileName)
                        .Where(name => int.TryParse(name, out _))
                        .ToList();

                    foreach (var item in manifest.Commands)
                    {
                        if (string.IsNullOrWhiteSpace(item?.CommandName)) continue;

                        var supported = new List<string>();
                        string dllTemplate = null;
                        foreach (var year in yearDirs)
                        {
                            string dllPath = string.IsNullOrEmpty(item.AssemblyPath)
                                ? null
                                : Path.Combine(setDir, year, item.AssemblyPath);
                            if (dllPath != null && File.Exists(dllPath))
                            {
                                supported.Add(year);
                                if (dllTemplate == null)
                                    dllTemplate = Path.Combine(manifest.Name, "{VERSION}", item.AssemblyPath);
                            }
                        }

                        if (supported.Count == 0)
                        {
                            // The DLL is not in any year directory - cannot build a resolvable entry; skip (do not write to the registry).
                            _logger.Warning("Manifest command has no DLL found; self-heal skipped for {0} ({1})",
                                item.CommandName, manifest.Name);
                            continue;
                        }

                        manifestCommands[item.CommandName] = new CommandConfig
                        {
                            CommandName = item.CommandName,
                            Description = item.Description ?? "",
                            AssemblyPath = dllTemplate,
                            SupportedRevitVersions = supported.ToArray(),
                            Developer = manifest.Developer ?? new DeveloperInfo { Name = "Unspecified" },
                            // Disabled by default - the enabled state is always governed by user settings.
                            Enabled = false
                        };
                    }
                }

                // 2. Keep existing entries still backed by a manifest (preserving enabled state and user data), remove the rest.
                var beforeCount = Config.Commands.Count;
                var byNameMap = new Dictionary<string, CommandConfig>(StringComparer.OrdinalIgnoreCase);
                foreach (var cmd in Config.Commands)
                {
                    if (!string.IsNullOrWhiteSpace(cmd?.CommandName) && manifestCommands.ContainsKey(cmd.CommandName))
                        byNameMap[cmd.CommandName] = cmd;
                }
                int removedCount = beforeCount - byNameMap.Count;
                if (removedCount > 0)
                {
                    _logger.Info("Self-heal: removed {0} registry entries with no corresponding manifest", removedCount);
                    Config.Commands = byNameMap.Values.ToList();
                }

                // 3. Add manifest commands missing from the registry (enabled=false).
                var existingNames = new HashSet<string>(byNameMap.Keys, StringComparer.OrdinalIgnoreCase);
                int addedCount = 0;
                foreach (var mc in manifestCommands.Values)
                {
                    if (!existingNames.Contains(mc.CommandName))
                    {
                        Config.Commands.Add(mc);
                        _logger.Info("Self-heal: manifest command {0} missing from registry; added (disabled by default)", mc.CommandName);
                        addedCount++;
                    }
                }

                // 4. Write back only when something changed.
                if (removedCount > 0 || addedCount > 0)
                {
                    SaveConfiguration();
                }
            }
            catch (Exception ex)
            {
                _logger.Error("Registry self-heal failed: {0}", ex.Message);
            }
        }

        private DateTime _lastConfigLoadTime;
    }
}
