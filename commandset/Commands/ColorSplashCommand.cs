using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPSDK.API.Base;
using RevitMCPCommandSet.Services;

namespace RevitMCPCommandSet.Commands
{
    public class ColorSplashCommand : RevitCommandBase
    {
        private ColorSplashEventHandler _handler => (ColorSplashEventHandler)Handler;

        /// <summary>
        /// Command name
        /// </summary>
        public override string CommandName => "color_splash";

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="uiApp">Revit UIApplication</param>
        public ColorSplashCommand(UIApplication uiApp)
            : base(new ColorSplashEventHandler(), uiApp)
        {
        }

        public override object Execute(JObject parameters, string requestId)
        {
            try
            {
                // Parse parameters
                string categoryName = null;
                if (parameters["categoryName"] != null)
                {
                    categoryName = parameters["categoryName"].ToString();
                }
                else
                {
                    throw new ArgumentException("Category name is required");
                }

                string parameterName = null;
                if (parameters["parameterName"] != null)
                {
                    parameterName = parameters["parameterName"].ToString();
                }
                else
                {
                    throw new ArgumentException("Parameter name is required");
                }

                bool useGradient = false;
                if (parameters["useGradient"] != null)
                {
                    useGradient = parameters["useGradient"].ToObject<bool>();
                }

                JArray customColors = null;
                if (parameters["customColors"] != null)
                {
                    customColors = parameters["customColors"] as JArray;
                }

                // Set parameters for the event handler
                _handler.SetParameters(categoryName, parameterName, useGradient, customColors);

                // Trigger external event and wait for completion
                if (RaiseAndWaitForCompletion(20000)) // 20 second timeout
                {
                    return _handler.ColoringResults;
                }
                else
                {
                    throw new TimeoutException("Color splash operation timed out");
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Color splash failed: {ex.Message}");
            }
        }
    }
}