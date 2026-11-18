using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Services;
using RevitMCPSDK.API.Base;

namespace RevitMCPCommandSet.Commands
{
    public class ExportFamiliesCommand : ExternalEventCommandBase
    {
        private static readonly object _executionLock = new object();
        private ExportFamiliesEventHandler _handler => (ExportFamiliesEventHandler)Handler;

        public override string CommandName => "export_families";

        public ExportFamiliesCommand(UIApplication uiApp)
            : base(new ExportFamiliesEventHandler(), uiApp)
        {
        }

        public override object Execute(JObject parameters, string requestId)
        {
            lock (_executionLock)
            {
                try
                {
                    _handler.SetParameters(
                        parameters?["outputDirectory"]?.Value<string>() ?? "",
                        parameters?["categories"]?.ToObject<List<string>>() ?? new List<string>(),
                        parameters?["groupByCategory"]?.Value<bool>() ?? true,
                        parameters?["overwrite"]?.Value<bool>() ?? false
                    );

                    if (RaiseAndWaitForCompletion(60000))
                    {
                        return _handler.Result;
                    }
                    else
                    {
                        throw new TimeoutException("Export families timed out");
                    }
                }
                catch (Exception ex)
                {
                    throw new Exception($"Export families failed: {ex.Message}");
                }
            }
        }
    }
}
