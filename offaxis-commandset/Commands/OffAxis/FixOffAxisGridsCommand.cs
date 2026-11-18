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
    /// Snaps slightly off-axis grids to nearest 0/45/90 orientation.
    /// </summary>
    public class FixOffAxisGridsCommand : ExternalEventCommandBase
    {
        private FixOffAxisGridsEventHandler _handler => (FixOffAxisGridsEventHandler)Handler;

        public override string CommandName => "fix_off_axis_grids";

        public FixOffAxisGridsCommand(UIApplication uiApp)
            : base(new FixOffAxisGridsEventHandler(), uiApp)
        {
        }

        public override object Execute(JObject parameters, string requestId)
        {
            try
            {
                var targetIds = new HashSet<int>();
                if (parameters?["elementIds"] != null)
                {
                    if (parameters["elementIds"] is JArray arr)
                    {
                        foreach (var token in arr)
                        {
                            if (int.TryParse(token.ToString(), out int id)) targetIds.Add(id);
                        }
                    }
                    else
                    {
                        string s = parameters["elementIds"].ToString();
                        string[] parts = s.Split(new[] { ',', ';', ' ', '\t', '[', ']' }, StringSplitOptions.RemoveEmptyEntries);
                        foreach (var p in parts)
                        {
                            if (int.TryParse(p, out int id)) targetIds.Add(id);
                        }
                    }
                }
                _handler.TargetIds = targetIds;

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

                if (RaiseAndWaitForCompletion(60000))
                {
                    return _handler.Result;
                }
                else
                {
                    throw new TimeoutException("fix_off_axis_grids operation timed out");
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to fix off-axis grids: {ex.Message}", ex);
            }
        }
    }
}
