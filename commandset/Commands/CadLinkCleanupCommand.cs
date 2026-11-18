using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Services;
using RevitMCPSDK.API.Base;

namespace RevitMCPCommandSet.Commands
{
    public class CadLinkCleanupCommand : ExternalEventCommandBase
    {
        private static readonly object _executionLock = new object();
        private CadLinkCleanupEventHandler _handler => (CadLinkCleanupEventHandler)Handler;

        public override string CommandName => "cad_link_cleanup";

        public CadLinkCleanupCommand(UIApplication uiApp)
            : base(new CadLinkCleanupEventHandler(), uiApp)
        {
        }

        public override object Execute(JObject parameters, string requestId)
        {
            lock (_executionLock)
            {
                try
                {
                    _handler.Action = parameters?["action"]?.Value<string>() ?? "list";
                    _handler.DeleteImports = parameters?["deleteImports"]?.Value<bool>() ?? false;
                    _handler.DeleteLinks = parameters?["deleteLinks"]?.Value<bool>() ?? false;
                    _handler.ElementIds = parameters?["elementIds"]?.ToObject<List<long>>() ?? new List<long>();

                    if (RaiseAndWaitForCompletion(15000))
                    {
                        return _handler.Result;
                    }
                    else
                    {
                        throw new TimeoutException("CAD link cleanup timed out");
                    }
                }
                catch (Exception ex)
                {
                    throw new Exception($"CAD link cleanup failed: {ex.Message}");
                }
            }
        }
    }
}
