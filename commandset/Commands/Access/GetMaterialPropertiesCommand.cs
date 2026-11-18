using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Services;
using RevitMCPSDK.API.Base;

namespace RevitMCPCommandSet.Commands.Access
{
    public class GetMaterialPropertiesCommand : ExternalEventCommandBase
    {
        private static readonly object _executionLock = new object();
        private GetMaterialPropertiesEventHandler _handler => (GetMaterialPropertiesEventHandler)Handler;

        public override string CommandName => "get_material_properties";

        public GetMaterialPropertiesCommand(UIApplication uiApp)
            : base(new GetMaterialPropertiesEventHandler(), uiApp)
        {
        }

        public override object Execute(JObject parameters, string requestId)
        {
            lock (_executionLock)
            {
                try
                {
                    _handler.MaterialId = parameters?["materialId"]?.Value<long?>();
                    _handler.MaterialName = parameters?["materialName"]?.Value<string>();

                    if (_handler.MaterialId == null && string.IsNullOrEmpty(_handler.MaterialName))
                        throw new ArgumentException("Either materialId or materialName must be provided");

                    if (RaiseAndWaitForCompletion(15000))
                    {
                        return _handler.Result;
                    }
                    else
                    {
                        throw new TimeoutException("Get material properties timed out");
                    }
                }
                catch (Exception ex)
                {
                    throw new Exception($"Get material properties failed: {ex.Message}");
                }
            }
        }
    }
}
