using System;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Services.OffAxis;
using RevitMCPSDK.API.Base;

namespace RevitMCPCommandSet.Commands.OffAxis
{
    /// <summary>
    /// Spacing detector command for Revit (1/4" lattice regularizer).
    /// </summary>
    public class DetectSpacingElementsCommand : ExternalEventCommandBase
    {
        private DetectSpacingElementsEventHandler _handler => (DetectSpacingElementsEventHandler)Handler;

        public override string CommandName => "detect_spacing_elements";

        public DetectSpacingElementsCommand(UIApplication uiApp)
            : base(new DetectSpacingElementsEventHandler(), uiApp)
        {
        }

        public override object Execute(JObject parameters, string requestId)
        {
            try
            {
                if (parameters?["limit"] != null && int.TryParse(parameters["limit"].ToString(), out int lim) && lim > 0)
                {
                    _handler.Limit = lim;
                }
                else
                {
                    _handler.Limit = 50;
                }

                if (RaiseAndWaitForCompletion(30000))
                {
                    return _handler.Result;
                }
                else
                {
                    throw new TimeoutException("detect_spacing_elements operation timed out");
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to detect spacing elements: {ex.Message}", ex);
            }
        }
    }
}
