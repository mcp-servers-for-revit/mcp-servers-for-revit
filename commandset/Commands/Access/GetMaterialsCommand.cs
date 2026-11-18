using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Services;
using RevitMCPSDK.API.Base;

namespace RevitMCPCommandSet.Commands.Access
{
    public class GetMaterialsCommand : ExternalEventCommandBase
    {
        private static readonly object _executionLock = new object();
        private GetMaterialsEventHandler _handler => (GetMaterialsEventHandler)Handler;

        public override string CommandName => "get_materials";

        public GetMaterialsCommand(UIApplication uiApp)
            : base(new GetMaterialsEventHandler(), uiApp)
        {
        }

        public override object Execute(JObject parameters, string requestId)
        {
            lock (_executionLock)
            {
                try
                {
                    _handler.MaterialClass = parameters?["materialClass"]?.Value<string>();
                    _handler.NameFilter = parameters?["nameFilter"]?.Value<string>();

                    if (RaiseAndWaitForCompletion(15000))
                    {
                        return _handler.Result;
                    }
                    else
                    {
                        throw new TimeoutException("Get materials timed out");
                    }
                }
                catch (Exception ex)
                {
                    throw new Exception($"Get materials failed: {ex.Message}");
                }
            }
        }
    }
}
