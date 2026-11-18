using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Models.Common;
using RevitMCPCommandSet.Services;
using RevitMCPSDK.API.Base;

namespace RevitMCPCommandSet.Commands
{
    public class ModifyElementCommand : ExternalEventCommandBase
    {
        private static readonly object _executionLock = new object();
        private ModifyElementEventHandler _handler => (ModifyElementEventHandler)Handler;

        public override string CommandName => "modify_element";

        public ModifyElementCommand(UIApplication uiApp)
            : base(new ModifyElementEventHandler(), uiApp)
        {
        }

        public override object Execute(JObject parameters, string requestId)
        {
            lock (_executionLock)
            {
                try
                {
                    var data = parameters?["data"]?.ToObject<ModifyElementSetting>();
                    if (data == null)
                        throw new ArgumentException("data is required");

                    _handler.Settings = data;

                    if (RaiseAndWaitForCompletion(15000))
                    {
                        return _handler.Result;
                    }
                    else
                    {
                        throw new TimeoutException("Modify element timed out");
                    }
                }
                catch (Exception ex)
                {
                    throw new Exception($"Modify element failed: {ex.Message}");
                }
            }
        }
    }
}
