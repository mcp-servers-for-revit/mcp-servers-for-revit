using System;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Services.Dwg;
using RevitMCPSDK.API.Base;

namespace RevitMCPCommandSet.Commands.Dwg
{
    /// <summary>
    /// Generate Revit grids from long straight lines on one DWG layer.
    /// Lines are ordered by angle bucket then position and labeled with a
    /// single alphabetic/numeric sequence (rename afterward as needed).
    /// </summary>
    public class CreateGridsFromDwgLayerCommand : ExternalEventCommandBase
    {
        private CreateGridsFromDwgLayerEventHandler _handler => (CreateGridsFromDwgLayerEventHandler)Handler;

        public override string CommandName => "create_grids_from_dwg_layer";

        public CreateGridsFromDwgLayerCommand(UIApplication uiApp)
            : base(new CreateGridsFromDwgLayerEventHandler(), uiApp)
        {
        }

        public override object Execute(JObject parameters, string requestId)
        {
            try
            {
                _handler.DwgNameOrId = parameters?["dwgNameOrId"]?.ToString();
                _handler.Layer = parameters?["layer"]?.ToString();

                _handler.MinLengthFt = 5.0;
                if (parameters?["minLengthFt"] != null && double.TryParse(parameters["minLengthFt"].ToString(), out double ml))
                    _handler.MinLengthFt = Math.Max(0.1, ml);

                _handler.NamingStyle = "alphabetic";
                string ns = parameters?["namingStyle"]?.ToString();
                if (!string.IsNullOrEmpty(ns)) _handler.NamingStyle = ns.ToLower() == "numeric" ? "numeric" : "alphabetic";

                _handler.StartLabel = parameters?["startLabel"]?.ToString();

                _handler.LevelId = -1;
                if (parameters?["levelId"] != null && long.TryParse(parameters["levelId"].ToString(), out long lid))
                    _handler.LevelId = lid;

                if (string.IsNullOrWhiteSpace(_handler.Layer))
                    throw new Exception("layer is required (e.g. 'A-GRID')");

                if (RaiseAndWaitForCompletion(120000))
                {
                    return _handler.Result;
                }
                else
                {
                    throw new TimeoutException("create_grids_from_dwg_layer operation timed out");
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to create grids from DWG layer: {ex.Message}", ex);
            }
        }
    }
}
