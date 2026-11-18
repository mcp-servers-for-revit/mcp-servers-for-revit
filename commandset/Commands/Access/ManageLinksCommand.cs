using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Services;
using RevitMCPSDK.API.Base;

namespace RevitMCPCommandSet.Commands.Access
{
    public class ManageLinksCommand : ExternalEventCommandBase
    {
        private static readonly object _executionLock = new object();
        private ManageLinksEventHandler _handler => (ManageLinksEventHandler)Handler;

        public override string CommandName => "manage_links";

        public ManageLinksCommand(UIApplication uiApp)
            : base(new ManageLinksEventHandler(), uiApp)
        {
        }

        public override object Execute(JObject parameters, string requestId)
        {
            lock (_executionLock)
            {
                try
                {
                    _handler.Action = parameters?["action"]?.Value<string>() ?? "list";
                    _handler.LinkId = parameters?["linkId"]?.Value<long>() ?? 0;

                    if (RaiseAndWaitForCompletion(30000))
                    {
                        return _handler.Result;
                    }
                    else
                    {
                        throw new TimeoutException("Manage links timed out");
                    }
                }
                catch (Exception ex)
                {
                    throw new Exception($"Manage links failed: {ex.Message}");
                }
            }
        }
    }
}
