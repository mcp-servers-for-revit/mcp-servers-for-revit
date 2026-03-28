using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;

namespace revit_mcp_plugin.UI
{
    public partial class MCPDockablePanel : Page
    {
        private static MCPDockablePanel _instance;
        private readonly DispatcherTimer _statusTimer;
        private bool _isWebViewInitialized;

        private const string CLAUDE_URL = "https://claude.ai/new";

        public static MCPDockablePanel Instance => _instance;

        public MCPDockablePanel()
        {
            InitializeComponent();
            _instance = this;

            _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _statusTimer.Tick += (s, e) => UpdateStatus();
        }

        private async void Page_Loaded(object sender, RoutedEventArgs e)
        {
            _statusTimer.Start();
            UpdateStatus();

            if (_isWebViewInitialized) return;

            try
            {
                // Use a persistent user data folder so login session is saved
                string userDataFolder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "RevitMCP", "WebView2");

                Directory.CreateDirectory(userDataFolder);

                var env = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
                await WebBrowser.EnsureCoreWebView2Async(env);

                _isWebViewInitialized = true;

                // Configure WebView2
                WebBrowser.CoreWebView2.Settings.IsStatusBarEnabled = false;
                WebBrowser.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
                WebBrowser.CoreWebView2.Settings.IsZoomControlEnabled = true;

                // Navigate to Claude
                WebBrowser.CoreWebView2.Navigate(CLAUDE_URL);

                // Hide loading overlay when navigation completes
                WebBrowser.CoreWebView2.NavigationCompleted += (s2, e2) =>
                {
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        LoadingOverlay.Visibility = Visibility.Collapsed;
                    }));
                };
            }
            catch (Exception ex)
            {
                LoadingOverlay.Visibility = Visibility.Visible;
                var stack = (LoadingOverlay.Child as StackPanel);
                if (stack != null && stack.Children.Count >= 2)
                {
                    (stack.Children[0] as System.Windows.Controls.TextBlock).Text = "WebView2 Error";
                    (stack.Children[1] as System.Windows.Controls.TextBlock).Text =
                        $"Install WebView2 Runtime from Microsoft.\n{ex.Message}";
                }
            }
        }

        private void UpdateStatus()
        {
            try
            {
                bool isRunning = Core.SocketService.Instance.IsRunning;
                StatusIndicator.Fill = new SolidColorBrush(isRunning
                    ? Color.FromRgb(68, 204, 136)
                    : Color.FromRgb(255, 68, 68));
                StatusText.Text = isRunning ? "MCP On" : "MCP Off";
                StatusText.Foreground = StatusIndicator.Fill;
            }
            catch { }
        }

        private void GoHome_Click(object sender, RoutedEventArgs e)
        {
            if (_isWebViewInitialized)
            {
                WebBrowser.CoreWebView2.Navigate(CLAUDE_URL);
            }
        }

        private void Reload_Click(object sender, RoutedEventArgs e)
        {
            if (_isWebViewInitialized)
            {
                WebBrowser.CoreWebView2.Reload();
            }
        }

        /// <summary>
        /// Log a command execution from SocketService (shows as notification)
        /// </summary>
        public void LogCommand(string commandName, bool success, string message, double durationMs)
        {
            // Could inject a JS notification into the page if needed
        }
    }
}
