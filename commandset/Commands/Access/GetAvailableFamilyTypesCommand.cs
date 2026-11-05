using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Services;
using RevitMCPSDK.API.Base;

namespace RevitMCPCommandSet.Commands.Access
{
    public class GetAvailableFamilyTypesCommand : ExternalEventCommandBase
    {
        private static readonly object _executionLock = new object();
        private GetAvailableFamilyTypesEventHandler _handler => (GetAvailableFamilyTypesEventHandler)Handler;

        public override string CommandName => "get_available_family_types";

        public GetAvailableFamilyTypesCommand(UIApplication uiApp)
            : base(new GetAvailableFamilyTypesEventHandler(), uiApp)
        {
        }

        public override object Execute(JObject parameters, string requestId)
        {
            lock (_executionLock)
            {
                try
                {
                    // Parameters parsen
                    List<string> categoryList = parameters?["categoryList"]?.ToObject<List<string>>() ?? new List<string>();
                    string familyNameFilter = parameters?["familyNameFilter"]?.Value<string>();
                    int? limit = parameters?["limit"]?.Value<int>();

                    // Queryparameters instellen
                    _handler.ResetCompletion();
                    _handler.CategoryList = categoryList;
                    _handler.FamilyNameFilter = familyNameFilter;
                    _handler.Limit = limit;

                    // Extern event activeren en wachten op voltooiing, maximaal 15 seconden wachten
                    if (RaiseAndWaitForCompletion(15000))
                    {
                        if (_handler.ErrorMessage != null)
                            throw new Exception(_handler.ErrorMessage);
                        return _handler.ResultFamilyTypes;
                    }
                    else
                    {
                        throw new TimeoutException("Time-out bij ophalen van beschikbare familietypes");
                    }
                }
                catch (Exception ex)
                {
                    throw new Exception($"Ophalen van beschikbare familietypes mislukt: {ex.Message}");
                }
            }
        }
    }
}
