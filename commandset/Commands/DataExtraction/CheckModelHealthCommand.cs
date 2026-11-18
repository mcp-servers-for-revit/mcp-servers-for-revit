using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Services.DataExtraction;
using RevitMCPSDK.API.Base;

namespace RevitMCPCommandSet.Commands.DataExtraction
{
    public class CheckModelHealthCommand : ExternalEventCommandBase
    {
        private CheckModelHealthEventHandler _handler => (CheckModelHealthEventHandler)Handler;

        public override string CommandName => "check_model_health";

        public CheckModelHealthCommand(UIApplication uiApp)
            : base(new CheckModelHealthEventHandler(), uiApp)
        {
        }

        public override object Execute(JObject parameters, string requestId)
        {
            try
            {
                _handler.SetParameters();

                if (RaiseAndWaitForCompletion(30000))
                {
                    return _handler.Result;
                }
                throw new TimeoutException("Check model health timed out");
            }
            catch (Exception ex)
            {
                throw new Exception($"Check model health failed: {ex.Message}");
            }
        }
    }
}
