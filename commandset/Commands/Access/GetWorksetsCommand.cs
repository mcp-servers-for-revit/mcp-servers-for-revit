using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Services;
using RevitMCPSDK.API.Base;

namespace RevitMCPCommandSet.Commands.Access
{
    public class GetWorksetsCommand : ExternalEventCommandBase
    {
        private static readonly object _executionLock = new object();
        private GetWorksetsEventHandler _handler => (GetWorksetsEventHandler)Handler;

        public override string CommandName => "get_worksets";

        public GetWorksetsCommand(UIApplication uiApp)
            : base(new GetWorksetsEventHandler(), uiApp)
        {
        }

        public override object Execute(JObject parameters, string requestId)
        {
            lock (_executionLock)
            {
                try
                {
                    _handler.IncludeSystemWorksets = parameters?["includeSystemWorksets"]?.Value<bool>() ?? false;

                    if (RaiseAndWaitForCompletion(15000))
                    {
                        return _handler.Result;
                    }
                    else
                    {
                        throw new TimeoutException("Get worksets timed out");
                    }
                }
                catch (Exception ex)
                {
                    throw new Exception($"Get worksets failed: {ex.Message}");
                }
            }
        }
    }
}
