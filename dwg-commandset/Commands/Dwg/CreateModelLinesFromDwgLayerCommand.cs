using System;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Services.Dwg;
using RevitMCPSDK.API.Base;

namespace RevitMCPCommandSet.Commands.Dwg
{
    /// <summary>
    /// Draw the curves of one DWG layer as Revit model lines (round-trip demo
    /// for extract_dwg_curves). Lines become bound lines, bounded arcs become
    /// 3-point arcs, and other curve types become tessellated polylines.
    /// </summary>
    public class CreateModelLinesFromDwgLayerCommand : ExternalEventCommandBase
    {
        private CreateModelLinesFromDwgLayerEventHandler _handler => (CreateModelLinesFromDwgLayerEventHandler)Handler;

        public override string CommandName => "create_model_lines_from_dwg_layer";

        public CreateModelLinesFromDwgLayerCommand(UIApplication uiApp)
            : base(new CreateModelLinesFromDwgLayerEventHandler(), uiApp)
        {
        }

        public override object Execute(JObject parameters, string requestId)
        {
            try
            {
                _handler.DwgNameOrId = parameters?["dwgNameOrId"]?.ToString();
                _handler.Layer = parameters?["layer"]?.ToString();
                _handler.MaxLines = 200;
                if (parameters?["maxLines"] != null && int.TryParse(parameters["maxLines"].ToString(), out int ml))
                    _handler.MaxLines = Math.Max(1, Math.Min(5000, ml));

                if (string.IsNullOrWhiteSpace(_handler.Layer))
                    throw new Exception("layer is required (e.g. 'A-GRID')");

                if (RaiseAndWaitForCompletion(120000))
                {
                    return _handler.Result;
                }
                else
                {
                    throw new TimeoutException("create_model_lines_from_dwg_layer operation timed out");
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to create model lines from DWG layer: {ex.Message}", ex);
            }
        }
    }
}
