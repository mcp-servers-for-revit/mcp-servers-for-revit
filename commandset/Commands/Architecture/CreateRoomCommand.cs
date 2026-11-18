using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPSDK.API.Base;
using RevitMCPCommandSet.Models.Architecture;
using RevitMCPCommandSet.Services.Architecture;

namespace RevitMCPCommandSet.Commands.Architecture
{
    /// <summary>
    /// Command to create and place rooms in Revit
    /// </summary>
    public class CreateRoomCommand : ExternalEventCommandBase
    {
        private CreateRoomEventHandler _handler => (CreateRoomEventHandler)Handler;

        /// <summary>
        /// Command name - must match the MCP tool name
        /// </summary>
        public override string CommandName => "create_room";

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="uiApp">Revit UIApplication</param>
        public CreateRoomCommand(UIApplication uiApp)
            : base(new CreateRoomEventHandler(), uiApp)
        {
        }

        public override object Execute(JObject parameters, string requestId)
        {
            try
            {
                // Parse parameters
                List<RoomCreationInfo> data = parameters?["data"]?.ToObject<List<RoomCreationInfo>>();
                if (data == null)
                    throw new ArgumentNullException(nameof(data), "No room data provided");

                // Set parameters for the event handler
                _handler.SetParameters(data);

                // Trigger external event and wait for completion
                if (RaiseAndWaitForCompletion(15000)) // 15 second timeout
                {
                    return _handler.Result;
                }
                else
                {
                    throw new TimeoutException("Create room operation timed out");
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to create room: {ex.Message}");
            }
        }
    }
}
