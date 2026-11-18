using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Services;
using RevitMCPSDK.API.Base;

namespace RevitMCPCommandSet.Commands.Access
{
    public class CreateMaterialCommand : ExternalEventCommandBase
    {
        private static readonly object _executionLock = new object();
        private CreateMaterialEventHandler _handler => (CreateMaterialEventHandler)Handler;
        public override string CommandName => "create_material";

        public CreateMaterialCommand(UIApplication uiApp)
            : base(new CreateMaterialEventHandler(), uiApp) { }

        public override object Execute(JObject parameters, string requestId)
        {
            lock (_executionLock)
            {
                try
                {
                    var name = parameters?["name"]?.Value<string>();
                    if (string.IsNullOrWhiteSpace(name))
                        throw new ArgumentException("name is required");

                    _handler.MaterialName = name;
                    _handler.DuplicateFrom = parameters?["duplicateFrom"]?.Value<string>();
                    _handler.ColorR = parameters?["colorR"]?.Value<int>();
                    _handler.ColorG = parameters?["colorG"]?.Value<int>();
                    _handler.ColorB = parameters?["colorB"]?.Value<int>();
                    _handler.Transparency = parameters?["transparency"]?.Value<int>();
                    _handler.Shininess = parameters?["shininess"]?.Value<int>();
                    _handler.Smoothness = parameters?["smoothness"]?.Value<int>();
                    _handler.MaterialClassName = parameters?["materialClass"]?.Value<string>();

                    if (RaiseAndWaitForCompletion(120000))
                    {
                        return _handler.Result;
                    }
                    else
                    {
                        throw new TimeoutException("Create material timed out");
                    }
                }
                catch (Exception ex)
                {
                    throw new Exception($"Create material failed: {ex.Message}");
                }
            }
        }
    }
}
