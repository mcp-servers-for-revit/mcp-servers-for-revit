using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Services.OffAxis;
using RevitMCPSDK.API.Base;
using System;

namespace RevitMCPCommandSet.Commands.OffAxis
{
    /// <summary>
    /// Warning-driven off-axis detector command for Revit.
    /// Aggregates warnings and returns actionable fix targets.
    /// </summary>
    public class DetectOffAxisHybridCommand : ExternalEventCommandBase
    {
        private DetectOffAxisHybridEventHandler _handler => (DetectOffAxisHybridEventHandler)Handler;

        public override string CommandName => "detect_off_axis_hybrid";

        public DetectOffAxisHybridCommand(UIApplication uiApp)
            : base(new DetectOffAxisHybridEventHandler(), uiApp)
        {
        }

        public override object Execute(JObject parameters, string requestId)
        {
            try
            {
                if (RaiseAndWaitForCompletion(30000))
                {
                    return _handler.Result;
                }
                else
                {
                    throw new TimeoutException("detect_off_axis_hybrid operation timed out");
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to detect off-axis warnings: {ex.Message}", ex);
            }
        }
    }
}
