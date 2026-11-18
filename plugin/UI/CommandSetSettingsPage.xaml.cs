using Newtonsoft.Json;
using revit_mcp_plugin.Configuration;
using revit_mcp_plugin.Utils;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
namespace revit_mcp_plugin.UI
{
    /// <summary>
    /// Interaction logic for CommandSetSettingsPage.xaml
    /// </summary>
    public partial class CommandSetSettingsPage : Page
    {
        private ObservableCollection<CommandSet> commandSets;
        private ObservableCollection<CommandConfig> currentCommands;
        private ICollectionView collectionView;

        /// <summary>
        /// Maps command name prefixes to user-friendly category labels.
        /// </summary>
        private static readonly (string[] Prefixes, string Category)[] CategoryRules = new[]
        {
            (new[] { "get_" },                                              "Get / Query"),
            (new[] { "create_" },                                           "Create"),
            (new[] { "export_" },                                           "Export"),
            (new[] { "import_" },                                           "Import"),
            (new[] { "manage_" },                                           "Manage"),
            (new[] { "set_", "modify_", "change_", "operate_" },            "Set / Modify"),
            (new[] { "batch_", "bulk_", "renumber_", "rename_" },           "Batch / Bulk"),
            (new[] { "check_", "audit_", "clash_", "measure_" },            "Audit / Check"),
        };

        public CommandSetSettingsPage()
        {
            InitializeComponent();
            // Initialize data collections
            commandSets = new ObservableCollection<CommandSet>();
            currentCommands = new ObservableCollection<CommandConfig>();
            // Set data bindings
            CommandSetListBox.ItemsSource = commandSets;
            FeaturesListView.ItemsSource = currentCommands;

            // Set up CollectionView for filtering and grouping
            collectionView = CollectionViewSource.GetDefaultView(currentCommands);
            collectionView.Filter = FilterCommand;
            collectionView.GroupDescriptions.Add(new PropertyGroupDescription("GroupCategory"));

            // Load command sets
            LoadCommandSets();
            // Initial state
            NoSelectionTextBlock.Visibility = Visibility.Visible;
        }

        /// <summary>
        /// Determines the display category for a command based on its name prefix.
        /// </summary>
        private static string GetCommandCategory(string commandName)
        {
            if (string.IsNullOrEmpty(commandName))
                return "Other";

            string lower = commandName.ToLowerInvariant();
            foreach (var rule in CategoryRules)
            {
                foreach (var prefix in rule.Prefixes)
                {
                    if (lower.StartsWith(prefix))
                        return rule.Category;
                }
            }
            return "Other";
        }

        /// <summary>
        /// Filter predicate used by CollectionView — matches search text against
        /// CommandName and Description (case-insensitive).
        /// </summary>
        private bool FilterCommand(object item)
        {
            if (item is CommandConfig cmd)
            {
                string searchText = SearchTextBox?.Text;
                if (string.IsNullOrWhiteSpace(searchText))
                    return true;

                string lower = searchText.ToLowerInvariant();
                bool matchName = cmd.CommandName != null &&
                                 cmd.CommandName.ToLowerInvariant().Contains(lower);
                bool matchDesc = cmd.Description != null &&
                                 cmd.Description.ToLowerInvariant().Contains(lower);
                return matchName || matchDesc;
            }
            return true;
        }

        /// <summary>
        /// Updates the "XX / YY commands enabled" counter text.
        /// </summary>
        private void UpdateCommandCounter()
        {
            if (currentCommands == null || CommandCounterText == null)
                return;

            int enabled = currentCommands.Count(c => c.Enabled);
            int total = currentCommands.Count;
            CommandCounterText.Text = $"{enabled} / {total} commands enabled";
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

                            // Detect supported Revit versions - determined from year subfolders
                            List<string> supportedVersions = new List<string>();
                            var versionDirectories = Directory.GetDirectories(directory)
                                .Select(Path.GetFileName)
                                .Where(name => int.TryParse(name, out _))
                                .ToList();

                            // Loop through each command
                            foreach (var command in commandSetData.Commands)
                            {
                                // Create a command configuration, determining supported versions by checking folders
                                List<string> supportedCommandVersions = new List<string>();
                                string dllBasePath = null;

                                foreach (var version in versionDirectories)
                                {
                                    string versionDirectory = Path.Combine(directory, version);
                                    string versionDllPath = null;

                                    if (!string.IsNullOrEmpty(command.AssemblyPath))
                                    {
                                        // AssemblyPath is relative to Commands/ dir with {VERSION} placeholder
                                        // e.g. "RevitMCPCommandSet/{VERSION}/RevitMCPCommandSet.dll"
                                        // Resolve by replacing {VERSION} and combining with Commands dir
                                        string template = command.AssemblyPath.Contains("{VERSION}")
                                            ? command.AssemblyPath
                                            : Path.Combine(commandSetData.Name, "{VERSION}", command.AssemblyPath);
                                        string resolvedPath = template.Replace("{VERSION}", version);
                                        versionDllPath = Path.Combine(commandsDirectory, resolvedPath);
                                        if (File.Exists(versionDllPath))
                                        {
                                            if (dllBasePath == null)
                                            {
                                                dllBasePath = template;
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
                                            versionDllPath = dllFiles[0];
                                            if (dllBasePath == null)
                                            {
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
                                    // Create command configuration
                                    var commandConfig = new CommandConfig
                                    {
                                        CommandName = command.CommandName,
                                        Description = command.Description,
                                        // Use path with version placeholder
                                        AssemblyPath = dllBasePath,
                                        Enabled = false,
                                        // Record all supported versions
                                        SupportedRevitVersions = supportedCommandVersions.ToArray()
                                    };

                                    // Add to command list
                                    newCommandSet.Commands.Add(commandConfig);
                                    availableCommandNames.Add(command.CommandName);
                                }
                            }

                            // If there are available commands, add to command set list
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
                        // Only clean up the registry if we successfully found command sets —
                        // if availableCommandSets is empty it means the scan failed (wrong Revit
                        // version, missing DLLs, etc.) and we must NOT wipe the registry.
                        if (availableCommandSets.Count > 0 && validCommands.Count != registry.Commands.Count)
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
            // Reset search when switching command sets
            if (SearchTextBox != null)
                SearchTextBox.Text = string.Empty;

            var selectedCommandSet = CommandSetListBox.SelectedItem as CommandSet;
            if (selectedCommandSet != null)
            {
                NoSelectionTextBlock.Visibility = Visibility.Collapsed;
                FeaturesHeaderTextBlock.Text = $"{selectedCommandSet.Name} - Command List";
                // Load commands from selected command set, assigning category
                foreach (var command in selectedCommandSet.Commands)
                {
                    command.GroupCategory = GetCommandCategory(command.CommandName);
                    currentCommands.Add(command);
                }
                // Refresh grouping/filtering
                collectionView.Refresh();
            }
            else
            {
                NoSelectionTextBlock.Visibility = Visibility.Visible;
                FeaturesHeaderTextBlock.Text = "Command List";
            }

            // Hide detail panel when switching sets
            DetailPanel.Visibility = Visibility.Collapsed;
            UpdateCommandCounter();
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
                UpdateCommandCounter();
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
                UpdateCommandCounter();
            }
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string registryFilePath = PathManager.GetCommandRegistryFilePath();
                // Read existing registry to preserve complete command information
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
                // Create new registry object
                CommandRegistryJson registry = new CommandRegistryJson();
                registry.Commands = new List<CommandConfig>();
                // Collect all enabled commands
                foreach (var commandSet in commandSets)
                {
                    // Try to get developer information from command.json
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
                        catch { /* If parsing fails, use default values */ }
                    }

                    foreach (var command in commandSet.Commands)
                    {
                        // Only add enabled commands to the registry
                        if (command.Enabled)
                        {
                            CommandConfig newConfig;
                            // Check if the command already exists in the previous registry
                            if (existingCommandsDict.ContainsKey(command.CommandName))
                            {
                                // If it exists, preserve original info, only update enabled status and path template
                                newConfig = existingCommandsDict[command.CommandName];
                                newConfig.Enabled = true;
                                newConfig.AssemblyPath = command.AssemblyPath;
                                newConfig.SupportedRevitVersions = command.SupportedRevitVersions;
                            }
                            else
                            {
                                // If it's a new command, create new configuration
                                newConfig = new CommandConfig
                                {
                                    CommandName = command.CommandName,
                                    AssemblyPath = command.AssemblyPath ?? "",
                                    Enabled = true,
                                    Description = command.Description,
                                    SupportedRevitVersions = command.SupportedRevitVersions,
                                    Developer = commandSetDeveloper
                                };
                            }
                            registry.Commands.Add(newConfig);
                        }
                    }
                }
                // Build summary for display
                string enabledFeaturesText = "";
                int enabledCount = registry.Commands.Count;
                foreach (var command in registry.Commands)
                {
                    string commandSetName = commandSets
                        .FirstOrDefault(cs => cs.Commands.Any(c => c.CommandName == command.CommandName))?.Name ?? "Unknown";
                    string versions = command.SupportedRevitVersions != null && command.SupportedRevitVersions.Any()
                        ? $" (Revit {string.Join(", ", command.SupportedRevitVersions)})"
                        : "";
                    enabledFeaturesText += $"• {commandSetName}: {command.CommandName}\n";
                }
                // Serialize and save to file
                string json = JsonConvert.SerializeObject(registry, Formatting.Indented);
                File.WriteAllText(registryFilePath, json);
                MessageBox.Show($"Command set settings successfully saved!\n\nEnabled {enabledCount} commands:\n{enabledFeaturesText}",
                              "Settings Saved", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving settings: {ex.Message}", "Error",
                              MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ImportCommandSetButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dialog = new Microsoft.Win32.OpenFileDialog
                {
                    Title = "Select a command.json file to import",
                    Filter = "Command Set Definition (command.json)|command.json"
                };

                if (dialog.ShowDialog() == true)
                {
                    string selectedFile = dialog.FileName;
                    string sourceFolder = Path.GetDirectoryName(selectedFile);
                    string commandSetFolderName = Path.GetFileName(sourceFolder);
                    string commandsDirectory = PathManager.GetCommandsDirectoryPath();
                    string destinationFolder = Path.Combine(commandsDirectory, commandSetFolderName);

                    if (Directory.Exists(destinationFolder))
                    {
                        var result = MessageBox.Show(
                            $"A command set '{commandSetFolderName}' already exists. Overwrite?",
                            "Command Set Exists", MessageBoxButton.YesNo, MessageBoxImage.Question);
                        if (result != MessageBoxResult.Yes)
                            return;
                    }

                    CopyDirectory(sourceFolder, destinationFolder);
                    LoadCommandSets();

                    MessageBox.Show($"Command set '{commandSetFolderName}' imported successfully!",
                                  "Import Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error importing command set: {ex.Message}", "Error",
                              MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static void CopyDirectory(string sourceDir, string destinationDir)
        {
            Directory.CreateDirectory(destinationDir);

            foreach (string file in Directory.GetFiles(sourceDir))
            {
                string destFile = Path.Combine(destinationDir, Path.GetFileName(file));
                File.Copy(file, destFile, true);
            }

            foreach (string dir in Directory.GetDirectories(sourceDir))
            {
                string destSubDir = Path.Combine(destinationDir, Path.GetFileName(dir));
                CopyDirectory(dir, destSubDir);
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

        // ── New event handlers ──

        private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            string text = SearchTextBox.Text;
            // Toggle placeholder / clear button visibility
            SearchPlaceholder.Visibility = string.IsNullOrEmpty(text)
                ? Visibility.Visible : Visibility.Collapsed;
            SearchClearButton.Visibility = string.IsNullOrEmpty(text)
                ? Visibility.Collapsed : Visibility.Visible;

            // Refresh the collection view filter
            collectionView?.Refresh();
        }

        private void SearchClearButton_Click(object sender, RoutedEventArgs e)
        {
            SearchTextBox.Text = string.Empty;
            SearchTextBox.Focus();
        }

        private void CommandCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            UpdateCommandCounter();
        }

        private void FeaturesListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var selected = FeaturesListView.SelectedItem as CommandConfig;
            if (selected != null)
            {
                DetailCommandName.Text = selected.CommandName;
                DetailDescription.Text = string.IsNullOrWhiteSpace(selected.Description)
                    ? "(no description)" : selected.Description;

                if (selected.SupportedRevitVersions != null && selected.SupportedRevitVersions.Length > 0)
                {
                    DetailVersions.Text = "Supported Revit versions: " +
                                          string.Join(", ", selected.SupportedRevitVersions);
                }
                else
                {
                    DetailVersions.Text = "Supported Revit versions: unknown";
                }

                DetailPanel.Visibility = Visibility.Visible;
            }
            else
            {
                DetailPanel.Visibility = Visibility.Collapsed;
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
