using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Services;
using RevitMCPSDK.API.Base;

namespace RevitMCPCommandSet.Commands
{
    public class OverrideGraphicsCommand : ExternalEventCommandBase
    {
        private static readonly object _executionLock = new object();
        private OverrideGraphicsEventHandler _handler => (OverrideGraphicsEventHandler)Handler;

        public override string CommandName => "override_graphics";

        public OverrideGraphicsCommand(UIApplication uiApp)
            : base(new OverrideGraphicsEventHandler(), uiApp)
        {
        }

        public override object Execute(JObject parameters, string requestId)
        {
            lock (_executionLock)
            {
                try
                {
                    _handler.ElementIds = parameters?["elementIds"]?.ToObject<List<long>>() ?? new List<long>();
                    _handler.ViewId = parameters?["viewId"]?.Value<long>() ?? 0;
                    _handler.ProjectionLineColorR = parameters?["projectionLineColor"]?["r"]?.Value<int>() ?? -1;
                    _handler.ProjectionLineColorG = parameters?["projectionLineColor"]?["g"]?.Value<int>() ?? -1;
                    _handler.ProjectionLineColorB = parameters?["projectionLineColor"]?["b"]?.Value<int>() ?? -1;
                    _handler.SurfaceForegroundColorR = parameters?["surfaceForegroundColor"]?["r"]?.Value<int>() ?? -1;
                    _handler.SurfaceForegroundColorG = parameters?["surfaceForegroundColor"]?["g"]?.Value<int>() ?? -1;
                    _handler.SurfaceForegroundColorB = parameters?["surfaceForegroundColor"]?["b"]?.Value<int>() ?? -1;
                    _handler.Transparency = parameters?["transparency"]?.Value<int>() ?? -1;
                    _handler.IsHalftone = parameters?["halftone"]?.Value<bool>();
                    _handler.ProjectionLineWeight = parameters?["projectionLineWeight"]?.Value<int>() ?? -1;
                    _handler.Action = parameters?["action"]?.Value<string>() ?? "set";

                    if (RaiseAndWaitForCompletion(15000))
                    {
                        return _handler.Result;
                    }
                    else
                    {
                        throw new TimeoutException("Override graphics timed out");
                    }
                }
                catch (Exception ex)
                {
                    throw new Exception($"Override graphics failed: {ex.Message}");
                }
            }
        }
    }
}
