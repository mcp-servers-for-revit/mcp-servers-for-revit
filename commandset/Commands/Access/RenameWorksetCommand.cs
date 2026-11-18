using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Services;
using RevitMCPSDK.API.Base;

namespace RevitMCPCommandSet.Commands.Access
{
    public class RenameWorksetCommand : ExternalEventCommandBase
    {
        private static readonly object _executionLock = new object();
        private RenameWorksetEventHandler _handler => (RenameWorksetEventHandler)Handler;

        public override string CommandName => "rename_workset";

        public RenameWorksetCommand(UIApplication uiApp)
            : base(new RenameWorksetEventHandler(), uiApp)
        {
        }

        public override object Execute(JObject parameters, string requestId)
        {
            lock (_executionLock)
            {
                try
                {
                    var currentName = parameters?["currentName"]?.Value<string>();
                    var newName = parameters?["newName"]?.Value<string>();

                    if (string.IsNullOrWhiteSpace(currentName))
                        throw new ArgumentException("currentName is required");
                    if (string.IsNullOrWhiteSpace(newName))
                        throw new ArgumentException("newName is required");

                    _handler.CurrentName = currentName;
                    _handler.NewName = newName;

                    if (RaiseAndWaitForCompletion(120000))
                    {
                        return _handler.Result;
                    }
                    else
                    {
                        throw new TimeoutException("Rename workset timed out");
                    }
                }
                catch (Exception ex)
                {
                    throw new Exception($"Rename workset failed: {ex.Message}");
                }
            }
        }
    }
}
