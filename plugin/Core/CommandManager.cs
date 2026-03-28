using Autodesk.Revit.UI;
using RevitMCPSDK.API.Interfaces;
using RevitMCPSDK.API.Utils;
using revit_mcp_plugin.Configuration;
using revit_mcp_plugin.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace revit_mcp_plugin.Core
{
    /// <summary>
    /// Command Manager — loads and manages commands with assembly caching.
    /// </summary>
    public class CommandManager
    {
        private readonly ICommandRegistry _commandRegistry;
        private readonly ILogger _logger;
        private readonly ConfigurationManager _configManager;
        private readonly UIApplication _uiApplication;
        private readonly RevitVersionAdapter _versionAdapter;

        // Cache: assembly path -> (Assembly, IRevitCommand types)
        private readonly Dictionary<string, (Assembly Assembly, Type[] CommandTypes)> _assemblyCache
            = new Dictionary<string, (Assembly, Type[])>(StringComparer.OrdinalIgnoreCase);

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
        /// Load all commands specified in the configuration file.
        /// Uses assembly caching to avoid redundant loading and type scanning.
        /// </summary>
        public void LoadCommands()
        {
            _logger.Info("Start loading commands.");
            string currentVersion = _versionAdapter.GetRevitVersion();
            _logger.Info("Current Revit version: {0}", currentVersion);

            foreach (var commandConfig in _configManager.Config.Commands)
            {
                try
                {
                    if (!commandConfig.Enabled)
                        continue;

                    // Check Revit version compatibility
                    if (commandConfig.SupportedRevitVersions != null &&
                        commandConfig.SupportedRevitVersions.Length > 0 &&
                        !_versionAdapter.IsVersionSupported(commandConfig.SupportedRevitVersions))
                        continue;

                    // Replace version placeholder
                    commandConfig.AssemblyPath = commandConfig.AssemblyPath.Contains("{VERSION}")
                        ? commandConfig.AssemblyPath.Replace("{VERSION}", currentVersion)
                        : commandConfig.AssemblyPath;

                    LoadCommandFromAssembly(commandConfig);
                }
                catch (Exception ex)
                {
                    _logger.Error("Failed to load command {0}: {1}", commandConfig.CommandName, ex.Message);
                }
            }

            _logger.Info("Command loading complete.");
        }

        private void LoadCommandFromAssembly(CommandConfig config)
        {
            try
            {
                string assemblyPath = config.AssemblyPath;
                if (!Path.IsPathRooted(assemblyPath))
                {
                    string baseDir = PathManager.GetCommandsDirectoryPath();
                    assemblyPath = Path.Combine(baseDir, assemblyPath);
                }

                if (!File.Exists(assemblyPath))
                {
                    _logger.Error("Command assembly does not exist: {0}", assemblyPath);
                    return;
                }

                // Use cached assembly and types
                if (!_assemblyCache.TryGetValue(assemblyPath, out var cached))
                {
                    Assembly assembly = Assembly.LoadFrom(assemblyPath);

                    // GetTypes() only once per assembly
                    Type[] commandTypes = assembly.GetTypes()
                        .Where(t => typeof(IRevitCommand).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract)
                        .ToArray();

                    cached = (assembly, commandTypes);
                    _assemblyCache[assemblyPath] = cached;

                    _logger.Info("Loaded assembly: {0} ({1} command types found)",
                        Path.GetFileName(assemblyPath), commandTypes.Length);
                }

                // Find the matching command type
                foreach (Type type in cached.CommandTypes)
                {
                    try
                    {
                        IRevitCommand command;

                        if (typeof(IRevitCommandInitializable).IsAssignableFrom(type))
                        {
                            command = (IRevitCommand)Activator.CreateInstance(type);
                            ((IRevitCommandInitializable)command).Initialize(_uiApplication);
                        }
                        else
                        {
                            var constructor = type.GetConstructor(new[] { typeof(UIApplication) });
                            command = constructor != null
                                ? (IRevitCommand)constructor.Invoke(new object[] { _uiApplication })
                                : (IRevitCommand)Activator.CreateInstance(type);
                        }

                        if (command.CommandName == config.CommandName)
                        {
                            _commandRegistry.RegisterCommand(command);
                            _logger.Info("Registered command: {0}", command.CommandName);
                            break;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Error("Failed to create command instance [{0}]: {1}", type.FullName, ex.Message);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error("Failed to load command assembly: {0}", ex.Message);
            }
        }
    }
}
