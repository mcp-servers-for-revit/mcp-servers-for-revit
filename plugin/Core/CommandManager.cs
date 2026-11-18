using Autodesk.Revit.UI;
using RevitMCPSDK.API.Interfaces;
using RevitMCPSDK.API.Utils;
using revit_mcp_plugin.Configuration;
using revit_mcp_plugin.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace revit_mcp_plugin.Core
{
    /// <summary>
    /// <para>Command Manager</para>
    /// </summary>
    public class CommandManager
    {
        private readonly ICommandRegistry _commandRegistry;
        private readonly ILogger _logger;
        private readonly ConfigurationManager _configManager;
        private readonly UIApplication _uiApplication;
        private readonly RevitVersionAdapter _versionAdapter;

        /// <summary>
        /// Manager in charge of loading and managing commands.
        /// </summary>
        /// <param name="commandRegistry"></param>
        /// <param name="logger"></param>
        /// <param name="configManager"></param>
        /// <param name="uiApplication"></param>
        public CommandManager(
            ICommandRegistry commandRegistry,
            ILogger logger,
            ConfigurationManager configManager,
            UIApplication uiApplication)
        {
            _commandRegistry = commandRegistry;
            _logger = logger;
            _configManager = configManager;
            _uiApplication = uiApplication;
            _versionAdapter = new RevitVersionAdapter(_uiApplication.Application);
        }

        /// <summary>
        /// <para>Load all commands specified in the configuration file.</para>
        /// </summary>
        public void LoadCommands()
        {
            _logger.Info("Start loading command.");
            string currentVersion = _versionAdapter.GetRevitVersion();
            _logger.Info("Current Revit version: {0}", currentVersion);

            // Load external commands from the configuration file.
            // Load external commands from the configuration file.
            foreach (var commandConfig in _configManager.Config.Commands)
            {
                try
                {
                    if (!commandConfig.Enabled)
                    {
                        _logger.Info("Skipping disabled command: {0}", commandConfig.CommandName);
                        continue;
                    }

                    // Check Revit version compatibility.
                    // Check Revit version compatibility.
                    if (commandConfig.SupportedRevitVersions != null &&
                        commandConfig.SupportedRevitVersions.Length > 0 &&
                        !_versionAdapter.IsVersionSupported(commandConfig.SupportedRevitVersions))
                    {
                        _logger.Warning("The command {0} is not supported by the current Revit version ({1}} and it has been skipped.",
                            commandConfig.CommandName, currentVersion);
                        continue;
                    }

                    // Replace version placeholder strings in paths.
                    // Replace version placeholder strings in paths.
                    commandConfig.AssemblyPath = commandConfig.AssemblyPath.Contains("{VERSION}")
                        ? commandConfig.AssemblyPath.Replace("{VERSION}", currentVersion)
                        : commandConfig.AssemblyPath;

                    // Load external command assembly.
                    // Load external command assembly.
                    LoadCommandFromAssembly(commandConfig);
                }
                catch (Exception ex)
                {
                    _logger.Error("Failed to load command {0}: {1}", commandConfig.CommandName, ex.Message);
                }
            }

            _logger.Info("Command loading complete.");
        }

        /// <summary>
        /// <para>Hot reload: re-registers commands from assemblies whose file
        /// name contains fileNameFilter (null = all assemblies). Command
        /// instances are recreated and ReplaceCommand in the registry
        /// overwrites the old entries by CommandName.</para>
        /// </summary>
        public int ReloadCommands(string fileNameFilter)
        {
            int reloaded = 0;
            string currentVersion = _versionAdapter.GetRevitVersion();
            foreach (var commandConfig in _configManager.Config.Commands)
            {
                try
                {
                    if (!commandConfig.Enabled) continue;

                    if (commandConfig.SupportedRevitVersions != null &&
                        commandConfig.SupportedRevitVersions.Length > 0 &&
                        !_versionAdapter.IsVersionSupported(commandConfig.SupportedRevitVersions))
                        continue;

                    string path = commandConfig.AssemblyPath.Contains("{VERSION}")
                        ? commandConfig.AssemblyPath.Replace("{VERSION}", currentVersion)
                        : commandConfig.AssemblyPath;
                    commandConfig.AssemblyPath = path; // in-place, matches LoadCommands behavior
                    if (!Path.IsPathRooted(path))
                    {
                        string baseDir = PathManager.GetCommandsDirectoryPath();
                        path = Path.Combine(baseDir, path);
                    }
                    string fileName = Path.GetFileName(path);
                    if (fileNameFilter != null &&
                        fileName.IndexOf(fileNameFilter, StringComparison.OrdinalIgnoreCase) < 0)
                        continue;
                    if (!File.Exists(path))
                        continue;
                    if (string.Equals(fileName, "RevitMCPCommandSet.dll", StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.Warning("RevitMCPCommandSet is LoadFrom-resident; restart Revit to reload it.");
                        continue;
                    }

                    CommandSetLoader.Invalidate(path);
                    LoadCommandFromAssembly(commandConfig);
                    reloaded++;
                }
                catch (Exception ex)
                {
                    _logger.Error("Hot reload of {0} failed: {1}",
                        commandConfig.CommandName, ex.Message);
                }
            }
            _logger.Info("Hot reload complete: {0} command configs reprocessed.", reloaded);
            return reloaded;
        }

        /// <summary>
        /// Loads specific commands in specific assemblies.
        /// </summary>
        /// <param name="config">Configuration class describing the command.</param>
        private void LoadCommandFromAssembly(CommandConfig config)
        {
            try
            {
                // Determine the assembly path.
                // Determine the assembly path.
                string assemblyPath = config.AssemblyPath;
                if (!Path.IsPathRooted(assemblyPath))
                {
                    // If it is not an absolute path, then it is relative to the Command's directory.
                    string baseDir = PathManager.GetCommandsDirectoryPath();
                    assemblyPath = Path.Combine(baseDir, assemblyPath);
                }

                if (!File.Exists(assemblyPath))
                {
                    _logger.Error("Command assembly does not exist: {0}", assemblyPath);
                    return;
                }

                bool matched = false;

                // The dynamic-code host (RevitMCPCommandSet) loads via LoadFrom so
                // its Roslyn chain resolves by sibling probing (byte loads lose the
                // dependency-location context and end up on Revit's own old
                // System.Collections.Immutable, which lacks ImmutableCollectionsMarshal).
                // All other command sets keep the byte-load live-reload capability and
                // their files never lock.
                Assembly assembly;
                if (string.Equals(Path.GetFileName(assemblyPath), "RevitMCPCommandSet.dll", StringComparison.OrdinalIgnoreCase))
                {
                    assembly = Assembly.LoadFrom(assemblyPath);
                }
                else
                {
                    assembly = CommandSetLoader.Resolve(assemblyPath, _logger);
                }

                // Track command names actually exposed by the DLL to surface name drift.
                // Track command names actually exposed by the DLL to surface name drift.
                var availableNames = new List<string>();

                // Find types that implement the IRevitCommand interface.
                // Find types that implement the IRevitCommand interface.
                foreach (Type type in assembly.GetTypes())
                {
                    if (typeof(RevitMCPSDK.API.Interfaces.IRevitCommand).IsAssignableFrom(type) &&
                        !type.IsInterface &&
                        !type.IsAbstract)
                    {
                        try
                        {
                            // Create a command instance.
                            // Create a command instance.
                            RevitMCPSDK.API.Interfaces.IRevitCommand command;

                            // Check whether the command implements the initializable interface.
                            // Check whether the command implements the initializable interface.
                            if (typeof(IRevitCommandInitializable).IsAssignableFrom(type))
                            {
                                // Create instance and initialize.
                                // Create instance and initialize.
                                command = (IRevitCommand)Activator.CreateInstance(type);
                                ((IRevitCommandInitializable)command).Initialize(_uiApplication);
                            }
                            else
                            {
                                // Try searching for constructors that accept UIApplication.
                                // Try searching for constructors that accept UIApplication.
                                var constructor = type.GetConstructor(new[] { typeof(UIApplication) });
                                if (constructor != null)
                                {
                                    command = (IRevitCommand)constructor.Invoke(new object[] { _uiApplication });
                                }
                                else
                                {
                                    // Use a parameterless constructor.
                                    // Use a parameterless constructor.
                                    command = (IRevitCommand)Activator.CreateInstance(type);
                                }
                            }

                            availableNames.Add(command.CommandName);

                            // Check whether the command name matches the configuration.
                            // Check whether the command name matches the configuration.
                            if (command.CommandName == config.CommandName)
                            {
                                _commandRegistry.RegisterCommand(command);
                                matched = true;
                                _logger.Info("Command instance registered [{0}]: {1}",
                                    command.CommandName, Path.GetFileName(assemblyPath));
                                break; // Exit the loop after finding a matching command.
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.Error("Failed to create command instance [{0}]: {1}", type.FullName, ex.Message);
                        }
                    }
                }

                // If no command name in the assembly matches the config, warn loudly.
                // If no command name in the assembly matches the config, warn loudly.
                if (!matched)
                {
                    string names = availableNames.Count > 0 ? string.Join(", ", availableNames) : "(none)";
                    _logger.Warning("Assembly {0} exposes no command named \"{1}\"; available names: {2}. " +
                        "Fix commandName in commandRegistry.json/command.json to match the IRevitCommand.CommandName.",
                        Path.GetFileName(assemblyPath), config.CommandName, names);
                }
            }
            catch (Exception ex)
            {
                _logger.Error("Failed to load command assembly: {0}", ex.Message);
            }
        }
    }
}
