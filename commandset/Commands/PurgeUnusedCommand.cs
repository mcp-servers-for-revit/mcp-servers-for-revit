using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Services;
using RevitMCPSDK.API.Base;

namespace RevitMCPCommandSet.Commands
{
    public class PurgeUnusedCommand : ExternalEventCommandBase
    {
        private static readonly object _executionLock = new object();
        private PurgeUnusedEventHandler _handler => (PurgeUnusedEventHandler)Handler;

        public override string CommandName => "purge_unused";

        public PurgeUnusedCommand(UIApplication uiApp)
            : base(new PurgeUnusedEventHandler(), uiApp)
        {
        }

        public override object Execute(JObject parameters, string requestId)
        {
            lock (_executionLock)
            {
                try
                {
                    _handler.DryRun = parameters?["dryRun"]?.Value<bool>() ?? true;
                    _handler.MaxElements = parameters?["maxElements"]?.Value<int>() ?? 500;

                    if (RaiseAndWaitForCompletion(30000))
                    {
                        return _handler.Result;
                    }
                    else
                    {
                        throw new TimeoutException("Purge unused timed out");
                    }
                }
                catch (Exception ex)
                {
                    throw new Exception($"Purge unused failed: {ex.Message}");
                }
            }
        }
    }
}
