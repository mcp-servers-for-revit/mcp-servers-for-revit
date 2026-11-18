using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Services;
using RevitMCPSDK.API.Base;

namespace RevitMCPCommandSet.Commands.Access
{
    public class GetWarningsCommand : ExternalEventCommandBase
    {
        private static readonly object _executionLock = new object();
        private GetWarningsEventHandler _handler => (GetWarningsEventHandler)Handler;

        public override string CommandName => "get_warnings";

        public GetWarningsCommand(UIApplication uiApp)
            : base(new GetWarningsEventHandler(), uiApp)
        {
        }

        public override object Execute(JObject parameters, string requestId)
        {
            lock (_executionLock)
            {
                try
                {
                    _handler.SeverityFilter = parameters?["severityFilter"]?.Value<string>() ?? "All";
                    _handler.MaxWarnings = parameters?["maxWarnings"]?.Value<int>() ?? 500;
                    _handler.CategoryFilter = parameters?["categoryFilter"]?.Value<string>() ?? "";

                    if (RaiseAndWaitForCompletion(15000))
                    {
                        return _handler.Result;
                    }
                    else
                    {
                        throw new TimeoutException("Get warnings timed out");
                    }
                }
                catch (Exception ex)
                {
                    throw new Exception($"Get warnings failed: {ex.Message}");
                }
            }
        }
    }
}
