using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Services;
using RevitMCPSDK.API.Base;

namespace RevitMCPCommandSet.Commands
{
    public class ChangeElementTypeCommand : ExternalEventCommandBase
    {
        private static readonly object _executionLock = new object();
        private ChangeElementTypeEventHandler _handler => (ChangeElementTypeEventHandler)Handler;

        public override string CommandName => "change_element_type";

        public ChangeElementTypeCommand(UIApplication uiApp)
            : base(new ChangeElementTypeEventHandler(), uiApp)
        {
        }

        public override object Execute(JObject parameters, string requestId)
        {
            lock (_executionLock)
            {
                try
                {
                    _handler.ElementIds = parameters?["elementIds"]?.ToObject<List<long>>() ?? new List<long>();
                    _handler.TargetTypeId = parameters?["targetTypeId"]?.Value<long>() ?? 0;
                    _handler.TargetTypeName = parameters?["targetTypeName"]?.Value<string>() ?? "";
                    _handler.TargetFamilyName = parameters?["targetFamilyName"]?.Value<string>() ?? "";

                    // Scale timeout with number of elements (3s base + 2s per element)
                    int timeoutMs = Math.Max(15000, 3000 + _handler.ElementIds.Count * 2000);
                    if (RaiseAndWaitForCompletion(timeoutMs))
                    {
                        return _handler.Result;
                    }
                    else
                    {
                        throw new TimeoutException("Change element type timed out");
                    }
                }
                catch (Exception ex)
                {
                    throw new Exception($"Change element type failed: {ex.Message}");
                }
            }
        }
    }
}
