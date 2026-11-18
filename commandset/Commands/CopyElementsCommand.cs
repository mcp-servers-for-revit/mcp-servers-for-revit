using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Services;
using RevitMCPSDK.API.Base;

namespace RevitMCPCommandSet.Commands
{
    public class CopyElementsCommand : ExternalEventCommandBase
    {
        private static readonly object _executionLock = new object();
        private CopyElementsEventHandler _handler => (CopyElementsEventHandler)Handler;

        public override string CommandName => "copy_elements";

        public CopyElementsCommand(UIApplication uiApp)
            : base(new CopyElementsEventHandler(), uiApp)
        {
        }

        public override object Execute(JObject parameters, string requestId)
        {
            lock (_executionLock)
            {
                try
                {
                    _handler.ElementIds = parameters?["elementIds"]?.ToObject<List<long>>() ?? new List<long>();
                    _handler.SourceViewId = parameters?["sourceViewId"]?.Value<long>() ?? 0;
                    _handler.TargetViewId = parameters?["targetViewId"]?.Value<long>() ?? 0;
                    _handler.OffsetX = parameters?["offsetX"]?.Value<double>() ?? 0;
                    _handler.OffsetY = parameters?["offsetY"]?.Value<double>() ?? 0;
                    _handler.OffsetZ = parameters?["offsetZ"]?.Value<double>() ?? 0;

                    if (RaiseAndWaitForCompletion(15000))
                    {
                        return _handler.Result;
                    }
                    else
                    {
                        throw new TimeoutException("Copy elements timed out");
                    }
                }
                catch (Exception ex)
                {
                    throw new Exception($"Copy elements failed: {ex.Message}");
                }
            }
        }
    }
}
