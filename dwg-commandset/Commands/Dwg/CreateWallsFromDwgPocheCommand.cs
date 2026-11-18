using System;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Services.Dwg;
using RevitMCPSDK.API.Base;

namespace RevitMCPCommandSet.Commands.Dwg
{
    /// <summary>
    /// Generate Revit walls from hatch boundary loops (poche regions) in a DWG
    /// import. Ignores layer intent: every closed polyline loop in the DWG (or
    /// restricted to pocheLayer) is a poché region; within each loop,
    /// consecutive collinear edges merge into straight runs, runs pair as the
    /// two faces of a wall, and collinear centerline pieces across loops merge
    /// independently. Thickness maps to the nearest Revit wall type.
    /// </summary>
    public class CreateWallsFromDwgPocheCommand : ExternalEventCommandBase
    {
        private CreateWallsFromDwgPocheEventHandler _handler => (CreateWallsFromDwgPocheEventHandler)Handler;

        public override string CommandName => "create_walls_from_dwg_poche";

        public CreateWallsFromDwgPocheCommand(UIApplication uiApp)
            : base(new CreateWallsFromDwgPocheEventHandler(), uiApp)
        {
        }

        public override object Execute(JObject parameters, string requestId)
        {
            try
            {
                _handler.DwgNameOrId = parameters?["dwgNameOrId"]?.ToString();
                _handler.PocheLayer = parameters?["pocheLayer"]?.ToString() ?? "";

                if (parameters?["heightFt"] != null && double.TryParse(parameters["heightFt"].ToString(), out double h))
                    _handler.HeightFt = Math.Max(0.5, h);
                if (parameters?["maxWallThicknessFt"] != null && double.TryParse(parameters["maxWallThicknessFt"].ToString(), out double mwt))
                    _handler.MaxWallThicknessFt = Math.Max(0.2, mwt);
                else
                    _handler.MaxWallThicknessFt = 5.0;
                if (parameters?["minWallLengthFt"] != null && double.TryParse(parameters["minWallLengthFt"].ToString(), out double mwl))
                    _handler.MinWallLengthFt = Math.Max(0.5, mwl);
                if (parameters?["maxWalls"] != null && int.TryParse(parameters["maxWalls"].ToString(), out int mw))
                    _handler.MaxWalls = Math.Max(1, Math.Min(5000, mw));

                _handler.WallTypeName = parameters?["wallTypeName"]?.ToString();
                _handler.ExcludeDoorArcs = true;
                if (parameters?["excludeDoorArcs"] != null && bool.TryParse(parameters["excludeDoorArcs"].ToString(), out bool da))
                    _handler.ExcludeDoorArcs = da;

                _handler.LevelId = -1;
                if (parameters?["levelId"] != null && long.TryParse(parameters["levelId"].ToString(), out long lid))
                    _handler.LevelId = lid;

                if (string.IsNullOrWhiteSpace(_handler.DwgNameOrId))
                    throw new Exception("dwgNameOrId is required");

                if (RaiseAndWaitForCompletion(180000))
                {
                    return _handler.Result;
                }
                else
                {
                    throw new TimeoutException("create_walls_from_dwg_poche operation timed out");
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to create walls from DWG poche: {ex.Message}", ex);
            }
        }
    }
}
