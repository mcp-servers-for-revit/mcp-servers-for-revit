using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Services;
using RevitMCPSDK.API.Base;

namespace RevitMCPCommandSet.Commands.Access
{
    public class DeleteWorksetCommand : ExternalEventCommandBase
    {
        private static readonly object _executionLock = new object();
        private DeleteWorksetEventHandler _handler => (DeleteWorksetEventHandler)Handler;

        public override string CommandName => "delete_workset";

        public DeleteWorksetCommand(UIApplication uiApp)
            : base(new DeleteWorksetEventHandler(), uiApp)
        {
        }

        public override object Execute(JObject parameters, string requestId)
        {
            lock (_executionLock)
            {
                try
                {
                    var worksetName = parameters?["worksetName"]?.Value<string>();
                    var moveToWorksetName = parameters?["moveToWorksetName"]?.Value<string>();

                    if (string.IsNullOrWhiteSpace(worksetName))
                        throw new ArgumentException("worksetName is required");
                    if (string.IsNullOrWhiteSpace(moveToWorksetName))
                        throw new ArgumentException("moveToWorksetName is required");

                    _handler.WorksetName = worksetName;
                    _handler.MoveToWorksetName = moveToWorksetName;

                    if (RaiseAndWaitForCompletion(120000))
                    {
                        return _handler.Result;
                    }
                    else
                    {
                        throw new TimeoutException("Delete workset timed out");
                    }
                }
                catch (Exception ex)
                {
                    throw new Exception($"Delete workset failed: {ex.Message}");
                }
            }
        }
    }
}
