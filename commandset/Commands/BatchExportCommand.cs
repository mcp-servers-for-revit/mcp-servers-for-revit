using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Services;
using RevitMCPSDK.API.Base;

namespace RevitMCPCommandSet.Commands
{
    public class BatchExportCommand : ExternalEventCommandBase
    {
        private static readonly object _executionLock = new object();
        private BatchExportEventHandler _handler => (BatchExportEventHandler)Handler;

        public override string CommandName => "batch_export";

        public BatchExportCommand(UIApplication uiApp)
            : base(new BatchExportEventHandler(), uiApp)
        {
        }

        public override object Execute(JObject parameters, string requestId)
        {
            lock (_executionLock)
            {
                try
                {
                    _handler.Format = parameters?["format"]?.Value<string>() ?? "PDF";
                    _handler.SheetIds = parameters?["sheetIds"]?.ToObject<List<long>>() ?? new List<long>();
                    _handler.ViewIds = parameters?["viewIds"]?.ToObject<List<long>>() ?? new List<long>();
                    _handler.ExportPath = parameters?["exportPath"]?.Value<string>() ?? "";
                    _handler.PaperSize = parameters?["paperSize"]?.Value<string>() ?? "A4";

                    if (RaiseAndWaitForCompletion(60000))
                    {
                        return _handler.Result;
                    }
                    else
                    {
                        throw new TimeoutException("Batch export timed out");
                    }
                }
                catch (Exception ex)
                {
                    throw new Exception($"Batch export failed: {ex.Message}");
                }
            }
        }
    }
}
