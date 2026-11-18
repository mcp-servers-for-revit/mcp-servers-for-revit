using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Services;
using RevitMCPSDK.API.Base;

namespace RevitMCPCommandSet.Commands.Access
{
    public class GetPhasesCommand : ExternalEventCommandBase
    {
        private static readonly object _executionLock = new object();
        private GetPhasesEventHandler _handler => (GetPhasesEventHandler)Handler;

        public override string CommandName => "get_phases";

        public GetPhasesCommand(UIApplication uiApp)
            : base(new GetPhasesEventHandler(), uiApp)
        {
        }

        public override object Execute(JObject parameters, string requestId)
        {
            lock (_executionLock)
            {
                try
                {
                    _handler.IncludePhaseFilters = parameters?["includePhaseFilters"]?.Value<bool>() ?? true;

                    if (RaiseAndWaitForCompletion(15000))
                    {
                        return _handler.Result;
                    }
                    else
                    {
                        throw new TimeoutException("Get phases timed out");
                    }
                }
                catch (Exception ex)
                {
                    throw new Exception($"Get phases failed: {ex.Message}");
                }
            }
        }
    }
}
