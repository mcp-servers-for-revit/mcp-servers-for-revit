using System;
using System.Collections.Generic;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Services.OffAxis;
using RevitMCPCommandSet.Utils.OffAxis;
using RevitMCPSDK.API.Base;

namespace RevitMCPCommandSet.Commands.OffAxis
{
    /// <summary>
    /// Snaps slightly off-axis sketch lines in Floors, Ceilings, and Roofs,
    /// reconstructing exact corner intersections via ray-ray solver.
    /// </summary>
    public class FixOffAxisSketchesCommand : ExternalEventCommandBase
    {
        private FixOffAxisSketchesEventHandler _handler => (FixOffAxisSketchesEventHandler)Handler;

        public override string CommandName => "fix_off_axis_sketches";

        public FixOffAxisSketchesCommand(UIApplication uiApp)
            : base(new FixOffAxisSketchesEventHandler(), uiApp)
        {
        }

        public override object Execute(JObject parameters, string requestId)
        {
            try
            {
                var targetHosts = new HashSet<int>();
                var targetLines = new HashSet<int>();

                if (parameters?["hostIds"] != null)
                {
                    if (parameters["hostIds"] is JArray arr)
                    {
                        foreach (var token in arr)
                        {
                            if (int.TryParse(token.ToString(), out int id)) targetHosts.Add(id);
                        }
                    }
                    else
                    {
                        string s = parameters["hostIds"].ToString();
                        string[] parts = s.Split(new[] { ',', ';', ' ', '\t', '[', ']' }, StringSplitOptions.RemoveEmptyEntries);
                        foreach (var p in parts)
                        {
                            if (int.TryParse(p, out int id)) targetHosts.Add(id);
                        }
                    }
                }

                if (parameters?["lineIds"] != null)
                {
                    if (parameters["lineIds"] is JArray arr)
                    {
                        foreach (var token in arr)
                        {
                            if (int.TryParse(token.ToString(), out int id)) targetLines.Add(id);
                        }
                    }
                    else
                    {
                        string s = parameters["lineIds"].ToString();
                        string[] parts = s.Split(new[] { ',', ';', ' ', '\t', '[', ']' }, StringSplitOptions.RemoveEmptyEntries);
                        foreach (var p in parts)
                        {
                            if (int.TryParse(p, out int id)) targetLines.Add(id);
                        }
                    }
                }

                _handler.TargetHosts = targetHosts;
                _handler.TargetLines = targetLines;

                double minAng = 0.0000001, maxAng = 0.1;
                if (parameters?["minAngleDeg"] != null && double.TryParse(parameters["minAngleDeg"].ToString(), out double m1)) minAng = m1;
                if (parameters?["maxAngleDeg"] != null && double.TryParse(parameters["maxAngleDeg"].ToString(), out double m2)) maxAng = m2;
                string bandErr = OffAxisGeometryUtils.ValidateDeviationBand(minAng, maxAng);
                if (bandErr != null) throw new Exception(bandErr);
                _handler.MinDeviationDeg = minAng;
                _handler.MaxDeviationDeg = maxAng;

                if (parameters?["maxMoveInches"] != null && double.TryParse(parameters["maxMoveInches"].ToString(), out double mmi))
                    _handler.MaxMoveInches = mmi;
                else
                    _handler.MaxMoveInches = OffAxisGeometryUtils.DefaultMaxMoveInches;

                if (parameters?["previewOnly"] != null && bool.TryParse(parameters["previewOnly"].ToString(), out bool pv))
                    _handler.PreviewOnly = pv;

                if (parameters?["maxElements"] != null && int.TryParse(parameters["maxElements"].ToString(), out int me) && me > 0)
                    _handler.MaxElementsPerRun = me;

                if (RaiseAndWaitForCompletion(60000))
                {
                    return _handler.Result;
                }
                else
                {
                    throw new TimeoutException("fix_off_axis_sketches operation timed out");
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to fix off-axis sketches: {ex.Message}", ex);
            }
        }
    }
}
