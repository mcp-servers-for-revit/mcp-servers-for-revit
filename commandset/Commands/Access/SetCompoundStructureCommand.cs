using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Models.Common;
using RevitMCPCommandSet.Services;
using RevitMCPSDK.API.Base;

namespace RevitMCPCommandSet.Commands.Access
{
    public class SetCompoundStructureCommand : ExternalEventCommandBase
    {
        private static readonly object _executionLock = new object();
        private SetCompoundStructureEventHandler _handler => (SetCompoundStructureEventHandler)Handler;
        public override string CommandName => "set_compound_structure";

        public SetCompoundStructureCommand(UIApplication uiApp)
            : base(new SetCompoundStructureEventHandler(), uiApp) { }

        public override object Execute(JObject parameters, string requestId)
        {
            lock (_executionLock)
            {
                try
                {
                    _handler.TypeId = parameters?["typeId"]?.Value<long>();
                    _handler.TypeName = parameters?["typeName"]?.Value<string>();
                    _handler.Category = parameters?["category"]?.Value<string>();
                    _handler.DuplicateAsName = parameters?["duplicateAsName"]?.Value<string>();
                    _handler.Layers = parameters?["layers"]?.ToObject<List<CompoundLayerInput>>();

                    if (_handler.Layers == null || _handler.Layers.Count == 0)
                        throw new ArgumentException("layers is required and cannot be empty");

                    if (RaiseAndWaitForCompletion(120000))
                    {
                        return _handler.Result;
                    }
                    else
                    {
                        throw new TimeoutException("Set compound structure timed out");
                    }
                }
                catch (Exception ex)
                {
                    throw new Exception($"Set compound structure failed: {ex.Message}");
                }
            }
        }
    }
}
