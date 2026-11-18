using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Models.Views;
using RevitMCPCommandSet.Services;
using RevitMCPSDK.API.Base;

namespace RevitMCPCommandSet.Commands
{
    public class CreateViewCommand : ExternalEventCommandBase
    {
        private static readonly object _executionLock = new object();
        private CreateViewEventHandler _handler => (CreateViewEventHandler)Handler;

        public override string CommandName => "create_view";

        public CreateViewCommand(UIApplication uiApp)
            : base(new CreateViewEventHandler(), uiApp)
        {
        }

        public override object Execute(JObject parameters, string requestId)
        {
            lock (_executionLock)
            {
                try
                {
                    var viewInfo = parameters?.ToObject<ViewCreationInfo>();
                    if (viewInfo == null)
                        throw new ArgumentException("View creation info is required");

                    _handler.SetParameters(viewInfo);

                    if (RaiseAndWaitForCompletion(15000))
                    {
                        return _handler.Result;
                    }
                    else
                    {
                        throw new TimeoutException("Create view timed out");
                    }
                }
                catch (Exception ex)
                {
                    throw new Exception($"Create view failed: {ex.Message}");
                }
            }
        }
    }
}
