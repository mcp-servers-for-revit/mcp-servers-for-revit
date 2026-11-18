using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Services;
using RevitMCPSDK.API.Base;

namespace RevitMCPCommandSet.Commands.Access
{
    public class GetSharedParametersCommand : ExternalEventCommandBase
    {
        private static readonly object _executionLock = new object();
        private GetSharedParametersEventHandler _handler => (GetSharedParametersEventHandler)Handler;

        public override string CommandName => "get_shared_parameters";

        public GetSharedParametersCommand(UIApplication uiApp)
            : base(new GetSharedParametersEventHandler(), uiApp)
        {
        }

        public override object Execute(JObject parameters, string requestId)
        {
            lock (_executionLock)
            {
                try
                {
                    _handler.CategoryFilter = parameters?["categoryFilter"]?.Value<string>();

                    if (RaiseAndWaitForCompletion(15000))
                    {
                        return _handler.Result;
                    }
                    else
                    {
                        throw new TimeoutException("Get shared parameters timed out");
                    }
                }
                catch (Exception ex)
                {
                    throw new Exception($"Get shared parameters failed: {ex.Message}");
                }
            }
        }
    }
}
