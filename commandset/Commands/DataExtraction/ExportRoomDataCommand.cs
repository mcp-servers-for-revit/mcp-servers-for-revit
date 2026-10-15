using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Services.DataExtraction;
using RevitMCPSDK.API.Base;

namespace RevitMCPCommandSet.Commands.DataExtraction
{
    public class ExportRoomDataCommand : RevitCommandBase
    {
        private ExportRoomDataEventHandler _handler => (ExportRoomDataEventHandler)Handler;

        public override string CommandName => "export_room_data";

        public ExportRoomDataCommand(UIApplication uiApp)
            : base(new ExportRoomDataEventHandler(), uiApp)
        {
        }

        public override object Execute(JObject parameters, string requestId)
        {
            try
            {
                // Parse optional parameters
                bool includeUnplacedRooms = parameters?["includeUnplacedRooms"]?.Value<bool>() ?? false;
                bool includeNotEnclosedRooms = parameters?["includeNotEnclosedRooms"]?.Value<bool>() ?? false;

                // Set parameters
                _handler.SetParameters(includeUnplacedRooms, includeNotEnclosedRooms);

                // Execute and wait
                if (RaiseAndWaitForCompletion(60000)) // 60 second timeout
                {
                    return _handler.ResultInfo;
                }
                else
                {
                    throw new TimeoutException("Export room data operation timed out");
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to export room data: {ex.Message}");
            }
        }
    }
}
