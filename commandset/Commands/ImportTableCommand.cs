using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Services;
using RevitMCPSDK.API.Base;

namespace RevitMCPCommandSet.Commands
{
    public class ImportTableCommand : ExternalEventCommandBase
    {
        private static readonly object _executionLock = new object();
        private ImportTableEventHandler _handler => (ImportTableEventHandler)Handler;

        public override string CommandName => "import_table";

        public ImportTableCommand(UIApplication uiApp)
            : base(new ImportTableEventHandler(), uiApp)
        {
        }

        public override object Execute(JObject parameters, string requestId)
        {
            lock (_executionLock)
            {
                try
                {
                    var filePath = parameters?["filePath"]?.ToString();
                    if (string.IsNullOrEmpty(filePath))
                        throw new ArgumentException("filePath is required");

                    var delimiter = parameters?["delimiter"]?.ToString() ?? ",";
                    var viewType = parameters?["viewType"]?.ToString() ?? "drafting";
                    var viewName = parameters?["viewName"]?.ToString();
                    var scale = parameters?["scale"]?.ToObject<int>() ?? 1;
                    var textSize = parameters?["textSize"]?.ToObject<double>() ?? 2.0;
                    var includeHeaders = parameters?["includeHeaders"]?.ToObject<bool>() ?? true;

                    _handler.SetParameters(filePath, delimiter, viewType, viewName, scale, textSize, includeHeaders);

                    if (RaiseAndWaitForCompletion(30000))
                    {
                        return _handler.Result;
                    }
                    else
                    {
                        throw new TimeoutException("Import table timed out");
                    }
                }
                catch (Exception ex)
                {
                    throw new Exception($"Import table failed: {ex.Message}");
                }
            }
        }
    }
}
