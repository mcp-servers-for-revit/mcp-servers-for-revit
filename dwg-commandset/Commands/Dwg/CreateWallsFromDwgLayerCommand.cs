using System;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Services.Dwg;
using RevitMCPSDK.API.Base;

namespace RevitMCPCommandSet.Commands.Dwg
{
    /// <summary>
    /// Generate Revit walls from paired face lines on one DWG layer.
    /// DWGs draft walls as pairs of parallel lines (the two faces); the parser
    /// pairs them (parallel + thickness range + overlap), rejects short jamb
    /// linework, and maps pair thickness to the nearest Revit wall type.
    /// </summary>
    public class CreateWallsFromDwgLayerCommand : ExternalEventCommandBase
    {
        private CreateWallsFromDwgLayerEventHandler _handler => (CreateWallsFromDwgLayerEventHandler)Handler;

        public override string CommandName => "create_walls_from_dwg_layer";

        public CreateWallsFromDwgLayerCommand(UIApplication uiApp)
            : base(new CreateWallsFromDwgLayerEventHandler(), uiApp)
        {
        }

        public override object Execute(JObject parameters, string requestId)
        {
            try
            {
                _handler.DwgNameOrId = parameters?["dwgNameOrId"]?.ToString();
                _handler.Layer = parameters?["layer"]?.ToString();

                if (parameters?["heightFt"] != null && double.TryParse(parameters["heightFt"].ToString(), out double h))
                    _handler.HeightFt = Math.Max(0.5, h);
                if (parameters?["maxWallThicknessFt"] != null && double.TryParse(parameters["maxWallThicknessFt"].ToString(), out double mwt))
                    _handler.MaxWallThicknessFt = Math.Max(0.2, mwt);
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

                if (string.IsNullOrWhiteSpace(_handler.Layer))
                    throw new Exception("layer is required (e.g. 'A-WALL')");

                if (RaiseAndWaitForCompletion(180000))
                {
                    return _handler.Result;
                }
                else
                {
                    throw new TimeoutException("create_walls_from_dwg_layer operation timed out");
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to create walls from DWG layer: {ex.Message}", ex);
            }
        }
    }
}
