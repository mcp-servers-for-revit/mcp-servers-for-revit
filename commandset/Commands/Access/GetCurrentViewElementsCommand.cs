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
                // Parameters parsen
                List<string> modelCategoryList = parameters?["modelCategoryList"]?.ToObject<List<string>>() ?? new List<string>();
                List<string> annotationCategoryList = parameters?["annotationCategoryList"]?.ToObject<List<string>>() ?? new List<string>();
                bool includeHidden = parameters?["includeHidden"]?.Value<bool>() ?? false;
                // Geen fallbackwaarde: zonder expliciete limiet worden alle elementen geretourneerd
                int? limit = parameters?["limit"]?.Value<int>();

                // Queryparameters instellen
                _handler.SetQueryParameters(modelCategoryList, annotationCategoryList, includeHidden, limit);

                // Extern event activeren en wachten op voltooiing
                if (RaiseAndWaitForCompletion(60000)) // 60 seconden time-out
                {
                    if (_handler.ErrorMessage != null)
                        throw new Exception(_handler.ErrorMessage);
                    return _handler.ResultInfo;
                }
                else
                {
                    throw new TimeoutException("Time-out bij ophalen van weergave-elementen");
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Ophalen van weergave-elementen mislukt: {ex.Message}");
            }
        }
    }
}
