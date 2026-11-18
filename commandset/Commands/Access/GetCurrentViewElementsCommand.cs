using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Services;
using RevitMCPSDK.API.Base;

namespace RevitMCPCommandSet.Commands.Access
{
    public class GetCurrentViewElementsCommand : ExternalEventCommandBase
    {
        private GetCurrentViewElementsEventHandler _handler => (GetCurrentViewElementsEventHandler)Handler;

        public override string CommandName => "get_current_view_elements";

        public GetCurrentViewElementsCommand(UIApplication uiApp)
            : base(new GetCurrentViewElementsEventHandler(), uiApp)
        {
        }

        public override object Execute(JObject parameters, string requestId)
        {
            try
            {
                // Parse parameters
                List<string> modelCategoryList = parameters?["modelCategoryList"]?.ToObject<List<string>>() ?? new List<string>();
                List<string> annotationCategoryList = parameters?["annotationCategoryList"]?.ToObject<List<string>>() ?? new List<string>();
                bool includeHidden = parameters?["includeHidden"]?.Value<bool>() ?? false;
                int limit = parameters?["limit"]?.Value<int>() ?? 100;
                List<string> fields = parameters?["fields"]?.ToObject<List<string>>();

                // Set query parameters
                _handler.SetQueryParameters(modelCategoryList, annotationCategoryList, includeHidden, limit, fields);

                // Raise external event and wait for completion
                if (RaiseAndWaitForCompletion(60000)) // 60 second timeout
                {
                    if (_handler.ErrorMessage != null)
                        throw new Exception(_handler.ErrorMessage);
                    return _handler.ResultInfo;
                }
                else
                {
                    throw new TimeoutException("Get view elements timed out");
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to get view elements: {ex.Message}");
            }
        }
    }
}
