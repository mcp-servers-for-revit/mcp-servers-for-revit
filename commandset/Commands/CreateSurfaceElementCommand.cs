using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPSDK.API.Base;
using RevitMCPCommandSet.Models.Common;
using RevitMCPCommandSet.Services;

namespace RevitMCPCommandSet.Commands
{
    public class CreateSurfaceElementCommand : ExternalEventCommandBase
    {
        private CreateSurfaceElementEventHandler _handler => (CreateSurfaceElementEventHandler)Handler;

        /// <summary>
        /// Command name
        /// </summary>
        public override string CommandName => "create_surface_based_element";

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="uiApp">Revit UIApplication</param>
        public CreateSurfaceElementCommand(UIApplication uiApp)
            : base(new CreateSurfaceElementEventHandler(), uiApp)
        {
        }

        public override object Execute(JObject parameters, string requestId)
        {
            try
            {
                List<SurfaceElement> data = new List<SurfaceElement>();
                // Parse parameters
                data = parameters?["data"]?.ToObject<List<SurfaceElement>>();
                if (data == null)
                    throw new ArgumentNullException(nameof(data), "AI input data is null");

                // Set surface-based element parameters
                _handler.SetParameters(data);

                // Raise external event and wait for completion
                if (RaiseAndWaitForCompletion(10000))
                {
                    return _handler.Result;
                }
                else
                {
                    throw new TimeoutException("Surface-based element creation timed out");
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to create surface-based element: {ex.Message}");
            }
        }
    }
}
