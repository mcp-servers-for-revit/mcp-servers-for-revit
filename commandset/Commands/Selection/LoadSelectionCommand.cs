using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Services;
using RevitMCPSDK.API.Base;
using System;

namespace RevitMCPCommandSet.Commands.Selection
{
    public class LoadSelectionCommand : ExternalEventCommandBase
    {
        private LoadSelectionEventHandler _handler => (LoadSelectionEventHandler)Handler;

        public override string CommandName => "load_selection";

        public LoadSelectionCommand(UIApplication uiApp)
            : base(new LoadSelectionEventHandler(), uiApp) { }

        public override object Execute(JObject parameters, string requestId)
        {
            try
            {
                _handler.SelectionName = parameters?["name"]?.Value<string>() ?? "";
                _handler.SelectInView = parameters?["selectInView"]?.Value<bool>() ?? true;

                _handler.SetParameters();

                if (RaiseAndWaitForCompletion(10000))
                    return _handler.Result;

                throw new TimeoutException("Load selection timed out");
            }
            catch (Exception ex)
            {
                throw new Exception($"Load selection failed: {ex.Message}");
            }
        }
    }
}
