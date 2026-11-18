using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Services;
using RevitMCPSDK.API.Base;

namespace RevitMCPCommandSet.Commands.Access
{
    public class GetProjectInfoCommand : ExternalEventCommandBase
    {
        private static readonly object _executionLock = new object();
        private GetProjectInfoEventHandler _handler => (GetProjectInfoEventHandler)Handler;

        public override string CommandName => "get_project_info";

        public GetProjectInfoCommand(UIApplication uiApp)
            : base(new GetProjectInfoEventHandler(), uiApp)
        {
        }

        public override object Execute(JObject parameters, string requestId)
        {
            lock (_executionLock)
            {
                try
                {
                    _handler.IncludePhases = parameters?["includePhases"]?.Value<bool>() ?? true;
                    _handler.IncludeWorksets = parameters?["includeWorksets"]?.Value<bool>() ?? true;
                    _handler.IncludeLinks = parameters?["includeLinks"]?.Value<bool>() ?? true;
                    _handler.IncludeLevels = parameters?["includeLevels"]?.Value<bool>() ?? true;

                    if (RaiseAndWaitForCompletion(15000))
                    {
                        return _handler.Result;
                    }
                    else
                    {
                        throw new TimeoutException("Get project info timed out");
                    }
                }
                catch (Exception ex)
                {
                    throw new Exception($"Get project info failed: {ex.Message}");
                }
            }
        }
    }
}
