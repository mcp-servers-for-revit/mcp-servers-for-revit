using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace revit_mcp_plugin.Core
{
    /// <summary>
    /// Small borderless, topmost modeless notification shown when the MCP server
    /// starts with zero registered commands. Commands are disabled by default
    /// (opt-in design), so this guides the user to Settings -> enable -> Save.
    /// Closes itself after a few seconds or on click.
    /// </summary>
    public class FirstRunPromptWindow : Window
    {
        private const double AutoCloseSeconds = 6.0;

        public FirstRunPromptWindow()
        {
            Width = 420;
            SizeToContent = SizeToContent.Height;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Topmost = true;
            AllowsTransparency = true;
            Background = System.Windows.Media.Brushes.Transparent;
            ShowActivated = false;

            var border = new Border
            {
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(16, 12, 12, 12),
                Margin = new Thickness(8),
                Background = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0x50, 0x3B, 0x12)),
                BorderBrush = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0xE0, 0xA0, 0x30)),
                BorderThickness = new Thickness(1),
                Opacity = 0.96
            };

            var text = new TextBlock
            {
                Text = "No MCP commands are enabled yet.\n\n" +
                       "Commands are off by default so you control what the AI can touch. " +
                       "Open the plugin Settings page, enable the commands you want, " +
                       "click Save, then restart the server.",
                TextWrapping = TextWrapping.Wrap,
                Foreground = System.Windows.Media.Brushes.White,
                FontSize = 12
            };

            border.Child = text;
            Content = border;

            var work = SystemParameters.WorkArea;
            Left = work.Right - Width - 20;
            Top = work.Bottom - 140;

            MouseDown += (s, e) => Close();

            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(AutoCloseSeconds) };
            timer.Tick += (s, e) => { timer.Stop(); Close(); };
            timer.Start();
        }
    }
}
