using System;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Services.OffAxis;
using RevitMCPCommandSet.Utils.OffAxis;
using RevitMCPSDK.API.Base;

namespace RevitMCPCommandSet.Commands.OffAxis
{
    /// <summary>
    /// Geometric full-scan off-axis detector command for Revit.
    /// </summary>
    public class DetectOffAxisLinesCommand : ExternalEventCommandBase
    {
        private DetectOffAxisLinesEventHandler _handler => (DetectOffAxisLinesEventHandler)Handler;

        public override string CommandName => "detect_off_axis_lines";

        public DetectOffAxisLinesCommand(UIApplication uiApp)
            : base(new DetectOffAxisLinesEventHandler(), uiApp)
        {
        }

        public override object Execute(JObject parameters, string requestId)
        {
            try
            {
                double minAng = 0.001, maxAng = 0.1;
                if (parameters?["minAngleDeg"] != null && double.TryParse(parameters["minAngleDeg"].ToString(), out double m1)) minAng = m1;
                if (parameters?["maxAngleDeg"] != null && double.TryParse(parameters["maxAngleDeg"].ToString(), out double m2)) maxAng = m2;
                string bandErr = OffAxisGeometryUtils.ValidateDeviationBand(minAng, maxAng);
                if (bandErr != null) throw new Exception(bandErr);
                _handler.MinDeviationDeg = minAng;
                _handler.MaxDeviationDeg = maxAng;

                if (RaiseAndWaitForCompletion(30000))
                {
                    return _handler.Result;
                }
                else
                {
                    throw new TimeoutException("detect_off_axis_lines operation timed out");
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to detect off-axis lines: {ex.Message}", ex);
            }
        }
    }
}
