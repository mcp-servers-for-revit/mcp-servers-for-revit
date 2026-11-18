using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Services;
using RevitMCPSDK.API.Base;

namespace RevitMCPCommandSet.Commands
{
    public class RenameViewsCommand : ExternalEventCommandBase
    {
        private static readonly object _executionLock = new object();
        private RenameViewsEventHandler _handler => (RenameViewsEventHandler)Handler;

        public override string CommandName => "rename_views";

        public RenameViewsCommand(UIApplication uiApp)
            : base(new RenameViewsEventHandler(), uiApp)
        {
        }

        public override object Execute(JObject parameters, string requestId)
        {
            lock (_executionLock)
            {
                try
                {
                    _handler.SetParameters(
                        parameters?["operation"]?.Value<string>() ?? "find_replace",
                        parameters?["prefix"]?.Value<string>() ?? "",
                        parameters?["suffix"]?.Value<string>() ?? "",
                        parameters?["findText"]?.Value<string>() ?? "",
                        parameters?["replaceText"]?.Value<string>() ?? "",
                        parameters?["viewTypes"]?.ToObject<List<string>>() ?? new List<string>(),
                        parameters?["filterName"]?.Value<string>() ?? "",
                        parameters?["dryRun"]?.Value<bool>() ?? false
                    );

                    if (RaiseAndWaitForCompletion(120000)) // 2-minute timeout for large model scans
                        return _handler.Result;

                    throw new TimeoutException("Rename views timed out — try narrowing scope with viewTypes or filterName");
                }
                catch (Exception ex)
                {
                    throw new Exception($"Rename views failed: {ex.Message}");
                }
            }
        }
    }
}
