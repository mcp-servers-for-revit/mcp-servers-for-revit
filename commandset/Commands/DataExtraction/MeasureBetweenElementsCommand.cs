using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Services.DataExtraction;
using RevitMCPSDK.API.Base;

namespace RevitMCPCommandSet.Commands.DataExtraction
{
    public class MeasureBetweenElementsCommand : ExternalEventCommandBase
    {
        private MeasureBetweenElementsEventHandler _handler => (MeasureBetweenElementsEventHandler)Handler;

        public override string CommandName => "measure_between_elements";

        public MeasureBetweenElementsCommand(UIApplication uiApp)
            : base(new MeasureBetweenElementsEventHandler(), uiApp)
        {
        }

        public override object Execute(JObject parameters, string requestId)
        {
            try
            {
                long elementId1 = parameters?["elementId1"]?.Value<long>() ?? 0;
                long elementId2 = parameters?["elementId2"]?.Value<long>() ?? 0;
                string measureType = parameters?["measureType"]?.ToString() ?? "center_to_center";

                double[] point1 = null;
                if (parameters?["point1"] is JObject p1)
                    point1 = new[] { p1["x"].Value<double>(), p1["y"].Value<double>(), p1["z"].Value<double>() };

                double[] point2 = null;
                if (parameters?["point2"] is JObject p2)
                    point2 = new[] { p2["x"].Value<double>(), p2["y"].Value<double>(), p2["z"].Value<double>() };

                _handler.SetParameters(elementId1, elementId2, point1, point2, measureType);

                if (RaiseAndWaitForCompletion(15000))
                    return _handler.Result;

                throw new TimeoutException("Measure operation timed out");
            }
            catch (Exception ex)
            {
                throw new Exception($"Measure failed: {ex.Message}");
            }
        }
    }
}
