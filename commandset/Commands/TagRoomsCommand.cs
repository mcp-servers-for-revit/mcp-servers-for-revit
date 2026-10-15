using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Services;
using RevitMCPSDK.API.Base;

namespace RevitMCPCommandSet.Commands
{
    /// <summary>
    /// Command to create tags for rooms in the current view
    /// </summary>
    public class TagRoomsCommand : RevitCommandBase
    {
        private TagRoomsEventHandler _handler => (TagRoomsEventHandler)Handler;

        /// <summary>
        /// Command name - must match the MCP tool command name
        /// </summary>
        public override string CommandName => "tag_rooms";

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="uiApp">Revit UIApplication</param>
        public TagRoomsCommand(UIApplication uiApp)
            : base(new TagRoomsEventHandler(), uiApp)
        {
        }

        public override object Execute(JObject parameters, string requestId)
        {
            try
            {
                // Parse parameters
                bool useLeader = false;
                if (parameters["useLeader"] != null)
                {
                    useLeader = parameters["useLeader"].ToObject<bool>();
                }

                string tagTypeId = null;
                if (parameters["tagTypeId"] != null)
                {
                    tagTypeId = parameters["tagTypeId"].ToString();
                }

                List<long> roomIds = null;
                if (parameters["roomIds"] != null)
                {
                    roomIds = parameters["roomIds"].ToObject<List<long>>();
                }

                // Set parameters for the event handler
                _handler.SetParameters(useLeader, tagTypeId, roomIds);

                // Trigger external event and wait for completion
                if (RaiseAndWaitForCompletion(15000)) // 15 second timeout
                {
                    return _handler.TaggingResults;
                }
                else
                {
                    throw new TimeoutException("Tag rooms operation timed out");
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to tag rooms: {ex.Message}");
            }
        }
    }
}
