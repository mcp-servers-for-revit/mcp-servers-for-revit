using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Services.ViewManagement;
using RevitMCPSDK.API.Base;
using System;
using System.Collections.Generic;

namespace RevitMCPCommandSet.Commands.ViewManagement
{
    public class SetViewCropCommand : ExternalEventCommandBase
    {
        private static readonly object _executionLock = new object();
        private SetViewCropEventHandler _handler => (SetViewCropEventHandler)Handler;
        public override string CommandName => "set_view_crop";

        public SetViewCropCommand(UIApplication uiApp)
            : base(new SetViewCropEventHandler(), uiApp) { }

        public override object Execute(JObject parameters, string requestId)
        {
            lock (_executionLock)
            {
                try
                {
                    _handler.ViewId = parameters?["viewId"]?.Value<long>();
                    _handler.CropActive = parameters?["cropActive"]?.Value<bool>();
                    _handler.CropVisible = parameters?["cropVisible"]?.Value<bool>();
                    _handler.ElementIds = parameters?["elementIds"]?.ToObject<List<long>>() ?? new List<long>();
                    _handler.OffsetMm = parameters?["offsetMm"]?.Value<double>() ?? 300;
                    _handler.MinXMm = parameters?["minXMm"]?.Value<double>();
                    _handler.MinYMm = parameters?["minYMm"]?.Value<double>();
                    _handler.MaxXMm = parameters?["maxXMm"]?.Value<double>();
                    _handler.MaxYMm = parameters?["maxYMm"]?.Value<double>();
                    _handler.Reset = parameters?["reset"]?.Value<bool>() ?? false;

                    _handler.SetParameters();
                    if (RaiseAndWaitForCompletion(120000))
                        return _handler.Result;
                    throw new TimeoutException("Set view crop timed out");
                }
                catch (Exception ex) { throw new Exception($"Set view crop failed: {ex.Message}"); }
            }
        }
    }
}
