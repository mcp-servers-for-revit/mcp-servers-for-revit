using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Models.Common;
using RevitMCPCommandSet.Services;
using RevitMCPSDK.API.Base;

namespace RevitMCPCommandSet.Commands.Access
{
    public class SetElementPhaseCommand : ExternalEventCommandBase
    {
        private static readonly object _executionLock = new object();
        private SetElementPhaseEventHandler _handler => (SetElementPhaseEventHandler)Handler;

        public override string CommandName => "set_element_phase";

        public SetElementPhaseCommand(UIApplication uiApp)
            : base(new SetElementPhaseEventHandler(), uiApp)
        {
        }

        public override object Execute(JObject parameters, string requestId)
        {
            lock (_executionLock)
            {
                try
                {
                    var requests = parameters?["requests"]?.ToObject<List<SetElementPhaseRequest>>();
                    if (requests == null || requests.Count == 0)
                        throw new ArgumentException("requests is required and cannot be empty");

                    _handler.Requests = requests;

                    if (RaiseAndWaitForCompletion(15000))
                    {
                        return _handler.Result;
                    }
                    else
                    {
                        throw new TimeoutException("Set element phase timed out");
                    }
                }
                catch (Exception ex)
                {
                    throw new Exception($"Set element phase failed: {ex.Message}");
                }
            }
        }
    }
}
