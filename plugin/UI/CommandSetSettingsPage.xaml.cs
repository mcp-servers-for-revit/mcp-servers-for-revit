using Newtonsoft.Json;
using revit_mcp_plugin.Configuration;
using revit_mcp_plugin.Utils;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
namespace revit_mcp_plugin.UI
{
    /// <summary>
    /// Interaction logic for CommandSetSettingsPage.xaml
    /// </summary>
    public partial class CommandSetSettingsPage : Page
    {
        private ObservableCollection<CommandSet> commandSets;
        private ObservableCollection<CommandConfig> currentCommands;

        public CommandSetSettingsPage()
        {
            InitializeComponent();
            // Initialize data collections
            commandSets = new ObservableCollection<CommandSet>();
            currentCommands = new ObservableCollection<CommandConfig>();
            // Set data bindings
            CommandSetListBox.ItemsSource = commandSets;
            FeaturesListView.ItemsSource = currentCommands;
            // Load command sets
            LoadCommandSets();
            // Initial state
            NoSelectionTextBlock.Visibility = Visibility.Visible;
        }

        private void LoadCommandSets()
        {
            try
            {
                commandSets.Clear();
                string commandsDirectory = PathManager.GetCommandsDirectoryPath();
                string registryFilePath = PathManager.GetCommandRegistryFilePath();
                // 1. First load all command set folders, establish available command collections
                Dictionary<string, CommandSet> availableCommandSets = new Dictionary<string, CommandSet>();
                HashSet<string> availableCommandNames = new HashSet<string>();
                // Get all command set directories
                string[] commandSetDirectories = Directory.GetDirectories(commandsDirectory);
                foreach (var directory in commandSetDirectories)
                {
                    // Skip special folders or hidden folders
                    if (Path.GetFileName(directory).StartsWith("."))
                        continue;
                    string commandJsonPath = Path.Combine(directory, "command.json");
                    // If there's a command.json, load it
                    if (File.Exists(commandJsonPath))
                    {
                        string commandJson = File.ReadAllText(commandJsonPath);
                        var commandSetData = JsonConvert.DeserializeObject<CommandJson>(commandJson);
                        if (commandSetData != null)
                        {
                            var newCommandSet = new CommandSet
                            {
                                Name = commandSetData.Name,
                                Description = commandSetData.Description,
                                Commands = new List<CommandConfig>()
                            };

                            // Detect supported Revit versions - determined from the year subfolders
                            List<string> supportedVersions = new List<string>();
                            var versionDirectories = Directory.GetDirectories(directory)
                                .Select(Path.GetFileName)
                                .Where(name => int.TryParse(name, out _))
                                .ToList();

                            // Loop through each command
                            foreach (var command in commandSetData.Commands)
                            {
                                // Create a command configuration, but determine supported versions by checking the folders
                                List<string> supportedCommandVersions = new List<string>();
                                string dllBasePath = null;

                                foreach (var version in versionDirectories)
                                {
                                    string versionDirectory = Path.Combine(directory, version);
                                    string versionDllPath = null;

                                    if (!string.IsNullOrEmpty(command.AssemblyPath))
                                    {
                                        // If a relative path is specified, look within the version subfolder
                                        versionDllPath = Path.Combine(versionDirectory, command.AssemblyPath);
                                        if (File.Exists(versionDllPath))
                                        {
                                            // Record the base path template
                                            if (dllBasePath == null)
                                            {
                                                // Extract the relative path for use in the template
                                                dllBasePath = Path.Combine(commandSetData.Name, "{VERSION}", command.AssemblyPath);
                                            }
                                            supportedCommandVersions.Add(version);
                                        }
                                    }
                                    else
                                    {
                                        // If no path is specified, look for any DLL in the version subfolder
                                        var dllFiles = Directory.GetFiles(versionDirectory, "*.dll");
                                        if (dllFiles.Length > 0)
                                        {
                                            versionDllPath = dllFiles[0]; // Use the first DLL found
                                            if (dllBasePath == null)
                                            {
                                                // Extract the DLL file name
                                                string dllFileName = Path.GetFileName(versionDllPath);
                                                dllBasePath = Path.Combine(commandSetData.Name, "{VERSION}", dllFileName);
                                            }
                                            supportedCommandVersions.Add(version);
                                        }
                                    }
                                }

                                // If at least one version supports this command
                                if (supportedCommandVersions.Count > 0 && dllBasePath != null)
                                {
                                    // Create a command configuration
                                    var commandConfig = new CommandConfig
                                    {
                                        CommandName = command.CommandName,
                                        Description = command.Description,
                                        // Use a path with a version placeholder
                                        AssemblyPath = dllBasePath,
                                        Enabled = false,
                                        // Record all supported versions
                                        SupportedRevitVersions = supportedCommandVersions.ToArray()
                                    };

                                    // Add to the command list
                                    newCommandSet.Commands.Add(commandConfig);
                                    availableCommandNames.Add(command.CommandName);
                                }
                            }

                            // If there are available commands, add the command set to the list
                            if (newCommandSet.Commands.Any())
                            {
                                availableCommandSets[commandSetData.Name] = newCommandSet;
                            }
                        }
                    }
                }
                // 2. Load registry, update command enabled status, and clean up non-existent commands
                if (File.Exists(registryFilePath))
                {
                    string registryJson = File.ReadAllText(registryFilePath);
                    var registry = JsonConvert.DeserializeObject<CommandRegistryJson>(registryJson);
                    if (registry?.Commands != null)
                    {
                        // Keep only valid commands
                        List<CommandConfig> validCommands = new List<CommandConfig>();
                        foreach (var registryItem in registry.Commands)
                        {
                            if (availableCommandNames.Contains(registryItem.CommandName))
                            {
                                validCommands.Add(registryItem);
                                // Update the enabled status of this command in all command sets
                                foreach (var commandSet in availableCommandSets.Values)
                                {
                                    var command = commandSet.Commands.FirstOrDefault(c => c.CommandName == registryItem.CommandName);
                                    if (command != null)
                                    {
                                        command.Enabled = registryItem.Enabled;
                                    }
                                }
                            }
                        }
                        // If there are invalid commands, update the registry file
                        if (validCommands.Count != registry.Commands.Count)
                        {
                            registry.Commands = validCommands;
                            string updatedJson = JsonConvert.SerializeObject(registry, Formatting.Indented);
                            File.WriteAllText(registryFilePath, updatedJson);
                        }
                    }
                }
                // 3. Add command sets to the UI collection
                foreach (var commandSet in availableCommandSets.Values)
                {
                    commandSets.Add(commandSet);
                }
                // If no command sets found, display a message
                if (commandSets.Count == 0)
                {
                    MessageBox.Show("No command sets found. Please check if the Commands folder exists and contains valid command sets.",
                                  "No Command Sets", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading command sets: {ex.Message}", "Error",
                                MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CommandSetListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            currentCommands.Clear();
            var selectedCommandSet = CommandSetListBox.SelectedItem as CommandSet;
            if (selectedCommandSet != null)
            {
                NoSelectionTextBlock.Visibility = Visibility.Collapsed;
                FeaturesHeaderTextBlock.Text = $"{selectedCommandSet.Name} - Command List";
                // Load commands from selected command set
                foreach (var command in selectedCommandSet.Commands)
                {
                    currentCommands.Add(command);
                }
            }
            else
            {
                NoSelectionTextBlock.Visibility = Visibility.Visible;
                FeaturesHeaderTextBlock.Text = "Command List";
            }
        }

        private void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            // Save current selection state
            var selectedIndex = CommandSetListBox.SelectedIndex;
            // Reload command sets
            LoadCommandSets();
            // Restore selection
            if (selectedIndex >= 0 && selectedIndex < commandSets.Count)
            {
                CommandSetListBox.SelectedIndex = selectedIndex;
            }
        }

        private void SelectAllButton_Click(object sender, RoutedEventArgs e)
        {
            // Only operate on the currently displayed commands
            if (currentCommands.Count > 0)
            {
                foreach (var command in currentCommands)
                {
                    command.Enabled = true;
                }

                // Refresh the UI
                FeaturesListView.Items.Refresh();
            }
        }

        private void UnselectAllButton_Click(object sender, RoutedEventArgs e)
        {
            // Only operate on the currently displayed commands
            if (currentCommands.Count > 0)
            {
                foreach (var command in currentCommands)
                {
                    command.Enabled = false;
                }

                // Refresh the UI
                FeaturesListView.Items.Refresh();
            }
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string registryFilePath = PathManager.GetCommandRegistryFilePath();
                // Read the existing registry to preserve complete command information
                Dictionary<string, CommandConfig> existingCommandsDict = new Dictionary<string, CommandConfig>();
                if (File.Exists(registryFilePath))
                {
                    string registryJson = File.ReadAllText(registryFilePath);
                    var existingRegistry = JsonConvert.DeserializeObject<CommandRegistryJson>(registryJson);
                    if (existingRegistry?.Commands != null)
                    {
                        foreach (var cmd in existingRegistry.Commands)
                        {
                            existingCommandsDict[cmd.CommandName] = cmd;
                        }
                    }
                }
                // Create a new registry object
                CommandRegistryJson registry = new CommandRegistryJson();
                registry.Commands = new List<CommandConfig>();
                // Collect all "enabled" commands
                foreach (var commandSet in commandSets)
                {
                    // Try to obtain developer info from command.json
                    var commandSetDeveloper = new DeveloperInfo { Name = "Unspecified", Email = "Unspecified" };
                    string commandJsonPath = Path.Combine(PathManager.GetCommandsDirectoryPath(),
                        commandSet.Name, "command.json");
                    if (File.Exists(commandJsonPath))
                    {
                        try
                        {
                            string commandJson = File.ReadAllText(commandJsonPath);
                            var commandSetData = JsonConvert.DeserializeObject<CommandJson>(commandJson);
                            if (commandSetData != null)
                            {
                                commandSetDeveloper = commandSetData.Developer ?? commandSetDeveloper;
                            }
                        }
                        catch { /* Use defaults when parsing fails */ }
                    }

                    foreach (var command in commandSet.Commands)
                    {
                        // Keep disabled commands in the registry (enabled:false) so
                        // opt-in decisions survive and Save never destroys them.
                        CommandConfig newConfig;
                        // Check whether the command already exists in the previous registry
                        if (existingCommandsDict.ContainsKey(command.CommandName))
                        {
                            // If it exists, keep the original info and only update the enabled state and path template
                            newConfig = existingCommandsDict[command.CommandName];
                            newConfig.Enabled = command.Enabled;
                            newConfig.AssemblyPath = command.AssemblyPath;
                            newConfig.SupportedRevitVersions = command.SupportedRevitVersions;
                        }
                        else
                        {
                            // If it is a new command, create a new configuration (disabled commands are also registered)
                            newConfig = new CommandConfig
                            {
                                CommandName = command.CommandName,
                                AssemblyPath = command.AssemblyPath ?? "",
                                Enabled = command.Enabled,
                                Description = command.Description,
                                SupportedRevitVersions = command.SupportedRevitVersions,
                                Developer = commandSetDeveloper
                            };
                        }
                        registry.Commands.Add(newConfig);
                    }
                }
                // Build a summary for display (count enabled entries; disabled entries are only registered, not listed)
                int enabledCount = registry.Commands.Count(c => c.Enabled);
                string enabledFeaturesText = $"Enabled {enabledCount} of {registry.Commands.Count} (disabled entries are kept in the registry):\n";
                foreach (var command in registry.Commands.Where(c => c.Enabled))
                {
                    string commandSetName = commandSets
                        .FirstOrDefault(cs => cs.Commands.Any(c => c.CommandName == command.CommandName))?.Name ?? "Unknown";
                    string versions = command.SupportedRevitVersions != null && command.SupportedRevitVersions.Any()
                        ? $" (Revit {string.Join(", ", command.SupportedRevitVersions)})"
                        : "";
                    enabledFeaturesText += $"• {commandSetName}: {command.CommandName}\n";
                }
                // Serialize and save to the file
                string json = JsonConvert.SerializeObject(registry, Formatting.Indented);
                File.WriteAllText(registryFilePath, json);
                MessageBox.Show($"Command set settings successfully saved!\n\n{enabledFeaturesText}",
                              "Settings Saved", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving settings: {ex.Message}", "Error",
                              MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OpenFolderButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start("explorer.exe", PathManager.GetCommandsDirectoryPath());
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error opening Commands folder: {ex.Message}", "Error",
                              MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    // Data models
    public class CommandSet
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public List<CommandConfig> Commands { get; set; } = new List<CommandConfig>();
    }
    // Configuration files
    public class CommandRegistryJson
    {
        public List<CommandConfig> Commands { get; set; } = new List<CommandConfig>();
    }

    public class CommandJson
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public List<CommandItemJson> Commands { get; set; } = new List<CommandItemJson>();
        public DeveloperInfo Developer { get; set; }
        public List<string> SupportedRevitVersions { get; set; } = new List<string>();
    }

    public class CommandItemJson
    {
        public string CommandName { get; set; }
        public string Description { get; set; }
        public string AssemblyPath { get; set; }
    }
}