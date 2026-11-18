using System;
using Autodesk.Revit.UI;

namespace revit_mcp_plugin.UI
{
    public class MCPDockablePaneProvider : IDockablePaneProvider
    {
        private MCPDockablePanel _panel;

        public static readonly DockablePaneId PaneId = new DockablePaneId(new Guid("4dbb6508-9f47-4b2c-b13c-823caff775ad"));

        public void SetupDockablePane(DockablePaneProviderData data)
        {
            _panel = new MCPDockablePanel();
            data.FrameworkElement = _panel;
            data.InitialState = new DockablePaneState
            {
                DockPosition = DockPosition.Floating
            };
            data.VisibleByDefault = false;
        }
    }
}
