using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Services.DataExtraction;
using RevitMCPSDK.API.Base;

namespace RevitMCPCommandSet.Commands.DataExtraction
{
    public class GetMaterialQuantitiesCommand : ExternalEventCommandBase
    {
        private GetMaterialQuantitiesEventHandler _handler => (GetMaterialQuantitiesEventHandler)Handler;

        public override string CommandName => "get_material_quantities";

        public GetMaterialQuantitiesCommand(UIApplication uiApp)
            : base(new GetMaterialQuantitiesEventHandler(), uiApp)
        {
        }

        public override object Execute(JObject parameters, string requestId)
        {
            try
            {
                // Parse parameters
                List<string> categoryFilters = parameters?["categoryFilters"]?.ToObject<List<string>>();
                bool selectedElementsOnly = parameters?["selectedElementsOnly"]?.Value<bool>() ?? false;
                int maxResults = parameters?["maxResults"]?.Value<int>() ?? 50;

                // Set parameters
                _handler.SetParameters(categoryFilters, selectedElementsOnly, maxResults);

                // Execute and wait
                if (RaiseAndWaitForCompletion(120000)) // 120 second timeout for large projects
                {
                    return _handler.ResultInfo;
                }
                else
                {
                    throw new TimeoutException("Material quantities calculation timed out");
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to get material quantities: {ex.Message}");
            }
        }
    }
}
