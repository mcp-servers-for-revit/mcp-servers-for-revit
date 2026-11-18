using System;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Services.Dwg;
using RevitMCPSDK.API.Base;

namespace RevitMCPCommandSet.Commands.Dwg
{
    /// <summary>
    /// Extract curve geometry from an imported or linked DWG file.
    /// Omit dwgNameOrId to list available DWGs.
    /// </summary>
    public class ExtractDwgCurvesCommand : ExternalEventCommandBase
    {
        private ExtractDwgCurvesEventHandler _handler => (ExtractDwgCurvesEventHandler)Handler;

        public override string CommandName => "extract_dwg_curves";

        public ExtractDwgCurvesCommand(UIApplication uiApp)
            : base(new ExtractDwgCurvesEventHandler(), uiApp)
        {
        }

        public override object Execute(JObject parameters, string requestId)
        {
            try
            {
                _handler.DwgNameOrId = parameters?["dwgNameOrId"]?.ToString();
                _handler.LayerFilter = parameters?["layerFilter"]?.ToString();
                _handler.MaxCurves = 500;
                if (parameters?["maxCurves"] != null && int.TryParse(parameters["maxCurves"].ToString(), out int mc))
                    _handler.MaxCurves = Math.Max(1, mc);

                bool includeTess = true;
                if (parameters?["includeTessellation"] != null)
                    bool.TryParse(parameters["includeTessellation"].ToString(), out includeTess);
                _handler.IncludeTessellation = includeTess;

                if (parameters?["maxPointsPerCurve"] != null && int.TryParse(parameters["maxPointsPerCurve"].ToString(), out int mpc))
                    _handler.MaxPointsPerCurve = Math.Max(0, mpc);

                bool countAll = true;
                if (parameters?["countAll"] != null)
                    bool.TryParse(parameters["countAll"].ToString(), out countAll);
                _handler.CountAll = countAll;

                if (RaiseAndWaitForCompletion(60000))
                {
                    return _handler.Result;
                }
                else
                {
                    throw new TimeoutException("extract_dwg_curves operation timed out");
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to extract DWG curves: {ex.Message}", ex);
            }
        }
    }
}
