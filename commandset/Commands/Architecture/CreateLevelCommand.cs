using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPSDK.API.Base;
using RevitMCPCommandSet.Services.Architecture;
using LevelCreationInfo = RevitMCPCommandSet.Models.Architecture.LevelInfo;

namespace RevitMCPCommandSet.Commands.Architecture
{
    /// <summary>
    /// Command to create levels in Revit with automatic floor plan view generation
    /// </summary>
    public class CreateLevelCommand : ExternalEventCommandBase
    {
        private CreateLevelEventHandler _handler => (CreateLevelEventHandler)Handler;

        /// <summary>
        /// Command name - must match the MCP tool name
        /// </summary>
        public override string CommandName => "create_level";

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="uiApp">Revit UIApplication</param>
        public CreateLevelCommand(UIApplication uiApp)
            : base(new CreateLevelEventHandler(), uiApp)
        {
        }

        public override object Execute(JObject parameters, string requestId)
        {
            try
            {
                // Parse parameters
                List<LevelCreationInfo> data = parameters?["data"]?.ToObject<List<LevelCreationInfo>>();
                if (data == null || data.Count == 0)
                    throw new ArgumentNullException(nameof(data), "No level data provided — expected: {\"data\": [{\"name\": \"Level 2\", \"elevation\": 3000}]}");

                // Set parameters for the event handler
                _handler.SetParameters(data);

                // Trigger external event and wait for completion
                if (RaiseAndWaitForCompletion(15000)) // 15 second timeout
                {
                    return _handler.Result;
                }
                else
                {
                    throw new TimeoutException("Create level operation timed out");
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to create level: {ex.Message}");
            }
        }
    }
}
