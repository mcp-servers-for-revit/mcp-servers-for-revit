using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Services;
using RevitMCPSDK.API.Base;

namespace RevitMCPCommandSet.Commands.Access
{
    public class CreateWorksetCommand : ExternalEventCommandBase
    {
        private static readonly object _executionLock = new object();
        private CreateWorksetEventHandler _handler => (CreateWorksetEventHandler)Handler;

        public override string CommandName => "create_workset";

        public CreateWorksetCommand(UIApplication uiApp)
            : base(new CreateWorksetEventHandler(), uiApp)
        {
        }

        public override object Execute(JObject parameters, string requestId)
        {
            lock (_executionLock)
            {
                try
                {
                    var name = parameters?["name"]?.Value<string>();
                    if (string.IsNullOrWhiteSpace(name))
                        throw new ArgumentException("name is required");

                    _handler.WorksetName = name;

                    if (RaiseAndWaitForCompletion(120000))
                    {
                        return _handler.Result;
                    }
                    else
                    {
                        throw new TimeoutException("Create workset timed out");
                    }
                }
                catch (Exception ex)
                {
                    throw new Exception($"Create workset failed: {ex.Message}");
                }
            }
        }
    }
}
