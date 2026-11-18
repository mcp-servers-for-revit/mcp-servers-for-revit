using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Services.DataExtraction;
using RevitMCPSDK.API.Base;

namespace RevitMCPCommandSet.Commands.DataExtraction
{
    public class SyncCsvParametersCommand : ExternalEventCommandBase
    {
        private SyncCsvParametersEventHandler _handler => (SyncCsvParametersEventHandler)Handler;

        public override string CommandName => "sync_csv_parameters";

        public SyncCsvParametersCommand(UIApplication uiApp)
            : base(new SyncCsvParametersEventHandler(), uiApp)
        {
        }

        public override object Execute(JObject parameters, string requestId)
        {
            try
            {
                bool dryRun = parameters?["dryRun"]?.Value<bool>() ?? true;

                var dataArray = parameters?["data"] as JArray;
                if (dataArray == null || dataArray.Count == 0)
                    throw new ArgumentException("'data' array is required");

                var updates = dataArray.Select(item => new ElementParameterUpdate
                {
                    ElementId = item["elementId"]?.Value<long>() ?? 0,
                    Parameters = (item["parameters"] as JObject)?
                        .Properties()
                        .ToDictionary(p => p.Name, p => (object)p.Value.ToString())
                        ?? new Dictionary<string, object>()
                }).ToList();

                _handler.SetParameters(updates, dryRun);

                if (RaiseAndWaitForCompletion(30000))
                    return _handler.Result;

                throw new TimeoutException("Sync CSV parameters timed out");
            }
            catch (Exception ex)
            {
                throw new Exception($"Sync CSV parameters failed: {ex.Message}");
            }
        }
    }
}
