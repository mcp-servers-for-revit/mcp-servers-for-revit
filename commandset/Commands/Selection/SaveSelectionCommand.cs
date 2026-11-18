using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Services;
using RevitMCPSDK.API.Base;
using System;
using System.Collections.Generic;

namespace RevitMCPCommandSet.Commands.Selection
{
    public class SaveSelectionCommand : ExternalEventCommandBase
    {
        private SaveSelectionEventHandler _handler => (SaveSelectionEventHandler)Handler;

        public override string CommandName => "save_selection";

        public SaveSelectionCommand(UIApplication uiApp)
            : base(new SaveSelectionEventHandler(), uiApp) { }

        public override object Execute(JObject parameters, string requestId)
        {
            try
            {
                _handler.SelectionName = parameters?["name"]?.Value<string>() ?? "";
                _handler.ElementIds = parameters?["elementIds"]?.ToObject<List<long>>() ?? new List<long>();
                _handler.Overwrite = parameters?["overwrite"]?.Value<bool>() ?? false;

                _handler.SetParameters();

                if (RaiseAndWaitForCompletion(15000))
                    return _handler.Result;

                throw new TimeoutException("Save selection timed out");
            }
            catch (Exception ex)
            {
                throw new Exception($"Save selection failed: {ex.Message}");
            }
        }
    }
}
