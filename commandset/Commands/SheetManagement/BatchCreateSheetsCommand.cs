using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Services.SheetManagement;
using RevitMCPSDK.API.Base;

namespace RevitMCPCommandSet.Commands.SheetManagement
{
    public class BatchCreateSheetsCommand : ExternalEventCommandBase
    {
        private BatchCreateSheetsEventHandler _handler => (BatchCreateSheetsEventHandler)Handler;

        public override string CommandName => "batch_create_sheets";

        public BatchCreateSheetsCommand(UIApplication uiApp)
            : base(new BatchCreateSheetsEventHandler(), uiApp)
        {
        }

        public override object Execute(JObject parameters, string requestId)
        {
            try
            {
                var sheetsArray = parameters?["sheets"] as JArray;
                if (sheetsArray == null || sheetsArray.Count == 0)
                    throw new ArgumentException("'sheets' array is required");

                var sheets = sheetsArray.Select(s => new SheetDefinition
                {
                    Number = s["number"]?.ToString() ?? "",
                    Name = s["name"]?.ToString() ?? "",
                    TitleBlockName = s["titleBlockName"]?.ToString(),
                    ViewIds = (s["viewIds"] as JArray)?.Select(v => v.Value<long>()).ToList()
                }).ToList();

                string defaultTitleBlockName = parameters?["defaultTitleBlockName"]?.ToString();

                _handler.SetParameters(sheets, defaultTitleBlockName);

                if (RaiseAndWaitForCompletion(30000))
                    return _handler.Result;

                throw new TimeoutException("Batch create sheets timed out");
            }
            catch (Exception ex)
            {
                throw new Exception($"Batch create sheets failed: {ex.Message}");
            }
        }
    }
}
