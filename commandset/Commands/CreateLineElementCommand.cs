using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Models.Common;
using RevitMCPCommandSet.Services;
using RevitMCPSDK.API.Base;

namespace RevitMCPCommandSet.Commands
{
    public class CreateLineElementCommand : ExternalEventCommandBase
    {
        private CreateLineElementEventHandler _handler => (CreateLineElementEventHandler)Handler;

        /// <summary>
        /// Command name
        /// </summary>
        public override string CommandName => "create_line_based_element";

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="uiApp">Revit UIApplication</param>
        public CreateLineElementCommand(UIApplication uiApp)
            : base(new CreateLineElementEventHandler(), uiApp)
        {
        }

        public override object Execute(JObject parameters, string requestId)
        {
            try
            {
                List<LineElement> data = new List<LineElement>();
                // Parse parameters
                data = parameters?["data"]?.ToObject<List<LineElement>>();
                if (data == null)
                    throw new ArgumentNullException(nameof(data), "AI input data is null");

                // Set line-based element parameters
                _handler.SetParameters(data);

                // Raise external event and wait for completion
                if (RaiseAndWaitForCompletion(10000))
                {
                    return _handler.Result;
                }
                else
                {
                    throw new TimeoutException("Line-based element creation timed out");
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to create line-based element: {ex.Message}");
            }
        }
    }
}
