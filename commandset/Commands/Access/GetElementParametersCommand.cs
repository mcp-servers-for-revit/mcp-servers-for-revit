using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Services;
using RevitMCPSDK.API.Base;

namespace RevitMCPCommandSet.Commands.Access
{
    public class GetElementParametersCommand : ExternalEventCommandBase
    {
        private static readonly object _executionLock = new object();
        private GetElementParametersEventHandler _handler => (GetElementParametersEventHandler)Handler;

        public override string CommandName => "get_element_parameters";

        public GetElementParametersCommand(UIApplication uiApp)
            : base(new GetElementParametersEventHandler(), uiApp)
        {
        }

        public override object Execute(JObject parameters, string requestId)
        {
            lock (_executionLock)
            {
                try
                {
                    var elementIds = parameters?["elementIds"]?.ToObject<long[]>();
                    if (elementIds == null || elementIds.Length == 0)
                        throw new ArgumentException("elementIds is required and cannot be empty");

                    _handler.ElementIds = elementIds;
                    _handler.IncludeTypeParameters = parameters?["includeTypeParameters"]?.Value<bool>() ?? true;

                    if (RaiseAndWaitForCompletion(15000))
                    {
                        return _handler.Result;
                    }
                    else
                    {
                        throw new TimeoutException("Get element parameters timed out");
                    }
                }
                catch (Exception ex)
                {
                    throw new Exception($"Get element parameters failed: {ex.Message}");
                }
            }
        }
    }
}
