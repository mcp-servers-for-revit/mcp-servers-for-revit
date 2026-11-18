using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace revit_mcp_plugin.UI
{
    /// <summary>
    /// Interaction logic for ApiKeySettingsPage.xaml
    /// </summary>
    public partial class ApiKeySettingsPage : Page
    {
        private bool isPasswordVisible = false;
        private string currentApiKey = string.Empty;

        public ApiKeySettingsPage()
        {
            InitializeComponent();
            EnvVarRadio.IsChecked = true;
            DetectCurrentApiKey();
        }

        private void DetectCurrentApiKey()
        {
            string envKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY")?.Trim();
            string filePath = GetApiKeyFilePath();
            string fileKey = null;

            if (File.Exists(filePath))
            {
                try
                {
                    fileKey = File.ReadAllText(filePath).Trim();
                    if (string.IsNullOrEmpty(fileKey))
                        fileKey = null;
                }
                catch
                {
                    fileKey = null;
                }
            }

            if (!string.IsNullOrEmpty(envKey))
            {
                StatusText.Text = "Configured";
                StatusText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#4CAF50"));
                StatusSourceText.Text = "(from environment variable)";
                EnvVarRadio.IsChecked = true;
            }
            else if (!string.IsNullOrEmpty(fileKey))
            {
                StatusText.Text = "Configured";
                StatusText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#4CAF50"));
                StatusSourceText.Text = "(from file)";
                FileRadio.IsChecked = true;
            }
            else
            {
                StatusText.Text = "Not configured";
                StatusText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F44336"));
                StatusSourceText.Text = string.Empty;
            }
        }

        private static string GetApiKeyFilePath()
        {
            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(userProfile, ".claude", "api_key.txt");
        }

        private void ToggleVisibilityButton_Click(object sender, RoutedEventArgs e)
        {
            isPasswordVisible = !isPasswordVisible;

            if (isPasswordVisible)
            {
                ApiKeyTextBox.Text = currentApiKey;
                ApiKeyPasswordBox.Visibility = Visibility.Collapsed;
                ApiKeyTextBox.Visibility = Visibility.Visible;
                ToggleVisibilityButton.Content = "Hide";
            }
            else
            {
                ApiKeyPasswordBox.Password = currentApiKey;
                ApiKeyTextBox.Visibility = Visibility.Collapsed;
                ApiKeyPasswordBox.Visibility = Visibility.Visible;
                ToggleVisibilityButton.Content = "Show";
            }
        }

        private void ApiKeyPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            currentApiKey = ApiKeyPasswordBox.Password;
        }

        private void ApiKeyTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            currentApiKey = ApiKeyTextBox.Text;
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            string apiKey = currentApiKey?.Trim();

            if (string.IsNullOrEmpty(apiKey))
            {
                MessageBox.Show("Please enter an API key.", "Missing API Key",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                if (EnvVarRadio.IsChecked == true)
                {
                    Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", apiKey, EnvironmentVariableTarget.User);
                    Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", apiKey);
                    MessageBox.Show(
                        "API key saved to ANTHROPIC_API_KEY. The chat panel will use it on the next message.",
                        "API Key Saved", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else if (FileRadio.IsChecked == true)
                {
                    string filePath = GetApiKeyFilePath();
                    string directory = Path.GetDirectoryName(filePath);
                    if (!Directory.Exists(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }
                    File.WriteAllText(filePath, apiKey);
                    MessageBox.Show(
                        $"API key saved to {filePath}." +
                        (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY"))
                            ? " The chat panel will use it on the next message."
                            : " ANTHROPIC_API_KEY is configured and takes precedence over this file."),
                        "API Key Saved", MessageBoxButton.OK, MessageBoxImage.Information);
                }

                // Refresh status display
                DetectCurrentApiKey();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to save API key: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
