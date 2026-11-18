using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Services.SheetManagement;
using RevitMCPSDK.API.Base;

namespace RevitMCPCommandSet.Commands.SheetManagement
{
    public class CreatePlaceholderSheetsCommand : ExternalEventCommandBase
    {
        private CreatePlaceholderSheetsEventHandler _handler => (CreatePlaceholderSheetsEventHandler)Handler;

        public override string CommandName => "create_placeholder_sheets";

        public CreatePlaceholderSheetsCommand(UIApplication uiApp)
            : base(new CreatePlaceholderSheetsEventHandler(), uiApp)
        {
        }

        public override object Execute(JObject parameters, string requestId)
        {
            try
            {
                var action = parameters?["action"]?.ToString();
                if (string.IsNullOrEmpty(action))
                    throw new ArgumentException("'action' is required");

                List<PlaceholderSheetDefinition> sheets = null;
                List<long> sheetIds = null;
                long? titleBlockId = null;

                if (action == "create")
                {
                    var sheetsArray = parameters?["sheets"] as JArray;
                    if (sheetsArray == null || sheetsArray.Count == 0)
                        throw new ArgumentException("'sheets' array is required for 'create' action");

                    sheets = sheetsArray.Select(s => new PlaceholderSheetDefinition
                    {
                        Number = s["number"]?.ToString() ?? "",
                        Name = s["name"]?.ToString() ?? ""
                    }).ToList();
                }

                if (action == "convert" || action == "delete")
                {
                    var idsArray = parameters?["sheetIds"] as JArray;
                    if (idsArray == null || idsArray.Count == 0)
                        throw new ArgumentException($"'sheetIds' array is required for '{action}' action");

                    sheetIds = idsArray.Select(id => id.Value<long>()).ToList();
                }

                if (action == "convert")
                {
                    var tbId = parameters?["titleBlockId"];
                    if (tbId == null)
                        throw new ArgumentException("'titleBlockId' is required for 'convert' action");

                    titleBlockId = tbId.Value<long>();
                }

                _handler.SetParameters(action, sheets, sheetIds, titleBlockId);

                if (RaiseAndWaitForCompletion(30000))
                    return _handler.Result;

                throw new TimeoutException("Create placeholder sheets timed out");
            }
            catch (Exception ex)
            {
                throw new Exception($"Create placeholder sheets failed: {ex.Message}");
            }
        }
    }
}
