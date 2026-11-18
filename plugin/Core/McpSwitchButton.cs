using System;
using System.Windows.Media.Imaging;
using Autodesk.Revit.UI;

namespace revit_mcp_plugin.Core
{
    /// <summary>
    /// Keeps the "Revit MCP Switch" ribbon button visually in sync with the
    /// SocketService state (icon color + tooltip), and shows a modeless
    /// confirmation message after state changes.
    /// All ribbon mutations must happen on Revit's UI thread - every caller of
    /// this class (OnStartup, the toggle IExternalCommand, OnShutdown) qualifies.
    /// </summary>
    public static class McpSwitchButton
    {
        private static PushButton _switchButton;

        private static BitmapImage LoadIcon(string fileName)
        {
            return new BitmapImage(
                new Uri("/RevitMCPPlugin;component/Core/Ressources/" + fileName, UriKind.RelativeOrAbsolute));
        }

        /// <summary>
        /// Capture the ribbon button created in OnStartup and set its initial state.
        /// </summary>
        public static void Register(PushButton switchButton)
        {
            _switchButton = switchButton;
            Update();
        }

        /// <summary>
        /// Refresh icon + tooltip from the current SocketService state.
        /// </summary>
        public static void Update()
        {
            if (_switchButton == null) return;

            bool running = SocketService.Instance.IsRunning;

            _switchButton.Image = new BitmapImage(
                new Uri("/RevitMCPPlugin;component/Core/Ressources/" + (running ? "icon-16-on.png" : "icon-16-off.png"), UriKind.RelativeOrAbsolute));
            _switchButton.LargeImage = new BitmapImage(
                new Uri("/RevitMCPPlugin;component/Core/Ressources/" + (running ? "icon-32-on.png" : "icon-32-off.png"), UriKind.RelativeOrAbsolute));
            _switchButton.ToolTip = running
                ? "MCP server is RUNNING on port " + SocketService.Instance.Port + ". Click to stop."
                : "MCP server is STOPPED. Click to start.";
        }

        /// <summary>
        /// Modeless, auto-closing notification. Safe to call from the UI thread.
        /// </summary>
        public static void ShowToast(string message, bool isError)
        {
            try
            {
                var toast = new McpToastWindow(message, isError);
                toast.Show();
            }
            catch
            {
                // Toast is cosmetic only - never let it break the command.
            }
        }
    }
}
