using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace revit_mcp_plugin.Core
{
    /// <summary>
    /// Small borderless, topmost modeless notification window that confirms
    /// MCP server state changes. Closes itself after a few seconds, or
    /// immediately on click. Styled as a bottom-right toast.
    /// </summary>
    public class McpToastWindow : Window
    {
        private const double AutoCloseSeconds = 2.5;
        private readonly DispatcherTimer _timer;

        public McpToastWindow(string message, bool isError)
        {
            Width = 320;
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
                Padding = new Thickness(14, 10, 10, 10),
                Margin = new Thickness(8),
                Background = new System.Windows.Media.SolidColorBrush(
                    isError ? System.Windows.Media.Color.FromRgb(0xB0, 0x30, 0x30)
                            : System.Windows.Media.Color.FromRgb(0x25, 0x50, 0x25)),
                BorderBrush = new System.Windows.Media.SolidColorBrush(
                    isError ? System.Windows.Media.Color.FromRgb(0xE0, 0x50, 0x50)
                            : System.Windows.Media.Color.FromRgb(0x2E, 0xCC, 0x40)),
                BorderThickness = new Thickness(1),
                Opacity = 0.95
            };

            var text = new TextBlock
            {
                Text = message,
                TextWrapping = TextWrapping.Wrap,
                Foreground = System.Windows.Media.Brushes.White,
                FontSize = 12
            };

            border.Child = text;
            Content = border;

            // Position bottom-right of the primary screen's work area.
            var work = SystemParameters.WorkArea;
            Left = work.Right - Width - 20;
            Top = work.Bottom - 80;

            MouseDown += (s, e) => Close();

            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(AutoCloseSeconds) };
            _timer.Tick += (s, e) => { _timer.Stop(); Close(); };
            _timer.Start();

            Deactivated += (s, e) => { /* keep topmost; Revit stays focused */ };
        }
    }
}
