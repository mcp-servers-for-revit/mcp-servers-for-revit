using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Services;
using RevitMCPSDK.API.Base;

namespace RevitMCPCommandSet.Commands.Access
{
    public class GetCompoundStructureCommand : ExternalEventCommandBase
    {
        private static readonly object _executionLock = new object();
        private GetCompoundStructureEventHandler _handler => (GetCompoundStructureEventHandler)Handler;

        public override string CommandName => "get_compound_structure";

        public GetCompoundStructureCommand(UIApplication uiApp)
            : base(new GetCompoundStructureEventHandler(), uiApp)
        {
        }

        public override object Execute(JObject parameters, string requestId)
        {
            lock (_executionLock)
            {
                try
                {
                    _handler.TypeId = parameters?["typeId"]?.Value<long?>();
                    _handler.TypeName = parameters?["typeName"]?.Value<string>();
                    _handler.Category = parameters?["category"]?.Value<string>();

                    if (_handler.TypeId == null
                        && string.IsNullOrEmpty(_handler.TypeName))
                        throw new ArgumentException("Either typeId or typeName (with category) must be provided");

                    if (RaiseAndWaitForCompletion(120000))
                    {
                        return _handler.Result;
                    }
                    else
                    {
                        throw new TimeoutException("Get compound structure timed out");
                    }
                }
                catch (Exception ex)
                {
                    throw new Exception($"Get compound structure failed: {ex.Message}");
                }
            }
        }
    }
}
