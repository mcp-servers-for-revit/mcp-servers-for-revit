using System;
using System.Collections.Generic;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Services.OffAxis;
using RevitMCPSDK.API.Base;

namespace RevitMCPCommandSet.Commands.OffAxis
{
    /// <summary>
    /// Regularizes perpendicular spacing of walls and beams onto a 1/4" lattice relative to grids.
    /// </summary>
    public class FixSpacingElementsCommand : ExternalEventCommandBase
    {
        private FixSpacingElementsEventHandler _handler => (FixSpacingElementsEventHandler)Handler;

        public override string CommandName => "fix_spacing_elements";

        public FixSpacingElementsCommand(UIApplication uiApp)
            : base(new FixSpacingElementsEventHandler(), uiApp)
        {
        }

        public override object Execute(JObject parameters, string requestId)
        {
            try
            {
                var targetIds = new List<int>();
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

                if (parameters?["maxMoveInches"] != null && double.TryParse(parameters["maxMoveInches"].ToString(), out double maxMove))
                    _handler.MaxMoveInches = maxMove;
                else
                    _handler.MaxMoveInches = 1.0;

                if (parameters?["previewOnly"] != null && bool.TryParse(parameters["previewOnly"].ToString(), out bool pv))
                    _handler.PreviewOnly = pv;

                if (RaiseAndWaitForCompletion(60000))
                {
                    return _handler.Result;
                }
                else
                {
                    throw new TimeoutException("fix_spacing_elements operation timed out");
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to fix spacing elements: {ex.Message}", ex);
            }
        }
    }
}
