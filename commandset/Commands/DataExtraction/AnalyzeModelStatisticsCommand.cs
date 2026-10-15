using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Services.DataExtraction;
using RevitMCPSDK.API.Base;

namespace RevitMCPCommandSet.Commands.DataExtraction
{
    public class AnalyzeModelStatisticsCommand : RevitCommandBase
    {
        private AnalyzeModelStatisticsEventHandler _handler => (AnalyzeModelStatisticsEventHandler)Handler;

        public override string CommandName => "analyze_model_statistics";

        public AnalyzeModelStatisticsCommand(UIApplication uiApp)
            : base(new AnalyzeModelStatisticsEventHandler(), uiApp)
        {
        }

        public override object Execute(JObject parameters, string requestId)
        {
            try
            {
                // Parse parameters
                bool includeDetailedTypes = parameters?["includeDetailedTypes"]?.Value<bool>() ?? true;

                // Set parameters
                _handler.SetParameters(includeDetailedTypes);

                // Execute and wait
                if (RaiseAndWaitForCompletion(120000)) // 120 second timeout for large models
                {
                    return _handler.ResultInfo;
                }
                else
                {
                    throw new TimeoutException("Model statistics analysis timed out");
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to analyze model statistics: {ex.Message}");
            }
        }
    }
}
