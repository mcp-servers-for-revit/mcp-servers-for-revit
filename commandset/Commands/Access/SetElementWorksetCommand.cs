using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Models.Common;
using RevitMCPCommandSet.Services;
using RevitMCPSDK.API.Base;

namespace RevitMCPCommandSet.Commands.Access
{
    public class SetElementWorksetCommand : ExternalEventCommandBase
    {
        private static readonly object _executionLock = new object();
        private SetElementWorksetEventHandler _handler => (SetElementWorksetEventHandler)Handler;

        public override string CommandName => "set_element_workset";

        public SetElementWorksetCommand(UIApplication uiApp)
            : base(new SetElementWorksetEventHandler(), uiApp)
        {
        }

        public override object Execute(JObject parameters, string requestId)
        {
            lock (_executionLock)
            {
                try
                {
                    var requests = parameters?["requests"]?.ToObject<List<SetWorksetRequest>>();
                    if (requests == null || requests.Count == 0)
                        throw new ArgumentException("requests is required and cannot be empty");

                    _handler.Requests = requests;

                    if (RaiseAndWaitForCompletion(120000))
                    {
                        return _handler.Result;
                    }
                    else
                    {
                        throw new TimeoutException("Set element workset timed out");
                    }
                }
                catch (Exception ex)
                {
                    throw new Exception($"Set element workset failed: {ex.Message}");
                }
            }
        }
    }
}
