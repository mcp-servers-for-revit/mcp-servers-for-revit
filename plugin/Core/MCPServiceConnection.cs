using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;

namespace revit_mcp_plugin.Core
{
    [Transaction(TransactionMode.Manual)]
    public class MCPServiceConnection : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                // Obtain socket service.
                SocketService service = SocketService.Instance;

                if (service.IsRunning)
                {
                    service.Stop();
                }
                else
                {
                    service.Initialize(commandData.Application);
                    service.Start();
                }

                // Refresh ribbon indicator and confirm with a modeless toast.
                McpSwitchButton.Update();
                if (service.IsRunning)
                {
                    McpSwitchButton.ShowToast("Revit MCP server started on port " + service.Port + ".", false);
                }
                else
                {
                    McpSwitchButton.ShowToast("Revit MCP server stopped.", false);
                }

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                McpSwitchButton.Update();
                McpSwitchButton.ShowToast("Revit MCP server error: " + ex.Message, true);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}
