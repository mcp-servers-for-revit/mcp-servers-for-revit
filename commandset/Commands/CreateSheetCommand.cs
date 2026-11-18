using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Models.Views;
using RevitMCPCommandSet.Services;
using RevitMCPSDK.API.Base;

namespace RevitMCPCommandSet.Commands
{
    public class CreateSheetCommand : ExternalEventCommandBase
    {
        private static readonly object _executionLock = new object();
        private CreateSheetEventHandler _handler => (CreateSheetEventHandler)Handler;

        public override string CommandName => "create_sheet";

        public CreateSheetCommand(UIApplication uiApp)
            : base(new CreateSheetEventHandler(), uiApp)
        {
        }

        public override object Execute(JObject parameters, string requestId)
        {
            lock (_executionLock)
            {
                try
                {
                    var sheetInfo = parameters?.ToObject<SheetCreationInfo>();
                    if (sheetInfo == null)
                        throw new ArgumentException("Sheet creation info is required");

                    _handler.SheetInfo = sheetInfo;

                    if (RaiseAndWaitForCompletion(15000))
                    {
                        return _handler.Result;
                    }
                    else
                    {
                        throw new TimeoutException("Create sheet timed out");
                    }
                }
                catch (Exception ex)
                {
                    throw new Exception($"Create sheet failed: {ex.Message}");
                }
            }
        }
    }
}
