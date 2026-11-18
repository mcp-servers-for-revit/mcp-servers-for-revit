using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Services;
using RevitMCPSDK.API.Base;

namespace RevitMCPCommandSet.Commands.Access
{
    public class SetActiveWorksetCommand : ExternalEventCommandBase
    {
        private static readonly object _executionLock = new object();
        private SetActiveWorksetEventHandler _handler => (SetActiveWorksetEventHandler)Handler;

        public override string CommandName => "set_active_workset";

        public SetActiveWorksetCommand(UIApplication uiApp)
            : base(new SetActiveWorksetEventHandler(), uiApp)
        {
        }

        public override object Execute(JObject parameters, string requestId)
        {
            lock (_executionLock)
            {
                try
                {
                    var worksetName = parameters?["worksetName"]?.Value<string>();
                    if (string.IsNullOrWhiteSpace(worksetName))
                        throw new ArgumentException("worksetName is required");

                    _handler.WorksetName = worksetName;

                    if (RaiseAndWaitForCompletion(120000))
                    {
                        return _handler.Result;
                    }
                    else
                    {
                        throw new TimeoutException("Set active workset timed out");
                    }
                }
                catch (Exception ex)
                {
                    throw new Exception($"Set active workset failed: {ex.Message}");
                }
            }
        }
    }
}
