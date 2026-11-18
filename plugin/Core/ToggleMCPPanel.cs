using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using revit_mcp_plugin.UI;

namespace revit_mcp_plugin.Core
{
    [Transaction(TransactionMode.Manual)]
    public class ToggleMCPPanel : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var pane = commandData.Application.GetDockablePane(MCPDockablePaneProvider.PaneId);
                if (pane != null)
                {
                    if (pane.IsShown())
                        pane.Hide();
                    else
                        pane.Show();
                }
            }
            catch
            {
                TaskDialog.Show("MCP Panel", "MCP Panel is not available. Please restart Revit.");
            }

            return Result.Succeeded;
        }
    }
}
