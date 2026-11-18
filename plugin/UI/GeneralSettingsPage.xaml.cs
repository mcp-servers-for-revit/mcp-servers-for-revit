using Newtonsoft.Json;
using revit_mcp_plugin.Configuration;
using revit_mcp_plugin.Utils;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;

namespace revit_mcp_plugin.UI
{
    /// <summary>
    /// Interaction logic for GeneralSettingsPage.xaml
    /// </summary>
    public partial class GeneralSettingsPage : Page
    {
        private const int DefaultPort = 8080;
        private const string DefaultLogLevel = "Info";

        public GeneralSettingsPage()
        {
            InitializeComponent();
            LoadCurrentSettings();
            LoadVersionInfo();
        }

        private void LoadCurrentSettings()
        {
            try
            {
                string registryPath = PathManager.GetCommandRegistryFilePath();
                if (File.Exists(registryPath))
                {
                    string json = File.ReadAllText(registryPath);
                    var config = JsonConvert.DeserializeObject<FrameworkConfig>(json);

                    if (config?.Settings != null)
                    {
                        PortTextBox.Text = config.Settings.Port.ToString();
                        SetLogLevelSelection(config.Settings.LogLevel ?? DefaultLogLevel);
                    }
                    else
                    {
                        SetDefaults();
                    }
                }
                else
                {
                    SetDefaults();
                }
            }
            catch
            {
                SetDefaults();
            }
        }

        private void LoadVersionInfo()
        {
            // Plugin version from assembly
            try
            {
                var version = Assembly.GetExecutingAssembly().GetName().Version;
                PluginVersionText.Text = version != null ? version.ToString() : "Unknown";
            }
            catch
            {
                PluginVersionText.Text = "Unknown";
            }

            // Revit version - read from the assembly location path which contains the version year
            try
            {
                string assemblyPath = Assembly.GetExecutingAssembly().Location;
                // Path typically contains "Addins\2025\" or similar
                string[] pathParts = assemblyPath.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar });
                string revitYear = "Unknown";
                for (int i = 0; i < pathParts.Length; i++)
                {
                    if (pathParts[i].Equals("Addins", StringComparison.OrdinalIgnoreCase) && i + 1 < pathParts.Length)
                    {
                        revitYear = pathParts[i + 1];
                        break;
                    }
                }
                RevitVersionText.Text = revitYear;
            }
            catch
            {
                RevitVersionText.Text = "Unknown";
            }
        }

        private void SetDefaults()
        {
            PortTextBox.Text = DefaultPort.ToString();
            SetLogLevelSelection(DefaultLogLevel);
        }

        private void SetLogLevelSelection(string logLevel)
        {
            foreach (ComboBoxItem item in LogLevelComboBox.Items)
            {
                if (item.Content.ToString().Equals(logLevel, StringComparison.OrdinalIgnoreCase))
                {
                    LogLevelComboBox.SelectedItem = item;
                    return;
                }
            }
            // Default to Info if not found
            LogLevelComboBox.SelectedIndex = 1;
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            // Validate port
            if (!int.TryParse(PortTextBox.Text.Trim(), out int port) || port < 1 || port > 65535)
            {
                MessageBox.Show("Please enter a valid port number (1-65535).", "Invalid Port",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string logLevel = (LogLevelComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? DefaultLogLevel;

            try
            {
                string registryPath = PathManager.GetCommandRegistryFilePath();
                FrameworkConfig config;

                if (File.Exists(registryPath))
                {
                    string json = File.ReadAllText(registryPath);
                    config = JsonConvert.DeserializeObject<FrameworkConfig>(json) ?? new FrameworkConfig();
                }
                else
                {
                    config = new FrameworkConfig();
                }

                if (config.Settings == null)
                {
                    config.Settings = new ServiceSettings();
                }

                config.Settings.Port = port;
                config.Settings.LogLevel = logLevel;

                string updatedJson = JsonConvert.SerializeObject(config, Formatting.Indented);
                File.WriteAllText(registryPath, updatedJson);

                MessageBox.Show("Settings saved successfully. A Revit restart may be required for port changes to take effect.",
                    "Settings Saved", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to save settings: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ResetDefaultsButton_Click(object sender, RoutedEventArgs e)
        {
            SetDefaults();
        }
    }
}
