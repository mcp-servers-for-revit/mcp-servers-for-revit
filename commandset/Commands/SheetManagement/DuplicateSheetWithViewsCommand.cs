using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Services.SheetManagement;
using RevitMCPSDK.API.Base;
using System;

namespace RevitMCPCommandSet.Commands.SheetManagement
{
    public class DuplicateSheetWithViewsCommand : ExternalEventCommandBase
    {
        private static readonly object _executionLock = new object();
        private DuplicateSheetWithViewsEventHandler _handler => (DuplicateSheetWithViewsEventHandler)Handler;

        public override string CommandName => "duplicate_sheet_with_views";

        public DuplicateSheetWithViewsCommand(UIApplication uiApp)
            : base(new DuplicateSheetWithViewsEventHandler(), uiApp) { }

        public override object Execute(JObject parameters, string requestId)
        {
            lock (_executionLock)
            {
                try
                {
                    _handler.SheetId = parameters?["sheetId"]?.Value<long>() ?? throw new ArgumentException("sheetId is required");
                    _handler.Copies = parameters?["copies"]?.Value<int>() ?? 1;
                    _handler.DuplicateViews = parameters?["duplicateViews"]?.Value<bool>() ?? true;
                    _handler.KeepLegends = parameters?["keepLegends"]?.Value<bool>() ?? true;
                    _handler.KeepSchedules = parameters?["keepSchedules"]?.Value<bool>() ?? true;
                    _handler.NewSheetNumberPrefix = parameters?["newSheetNumberPrefix"]?.Value<string>() ?? "";
                    _handler.ViewDuplicateOptionName = parameters?["viewDuplicateOption"]?.Value<string>() ?? "DuplicateWithDetailing";

                    _handler.SetParameters();

                    if (RaiseAndWaitForCompletion(60000))
                        return _handler.Result;

                    throw new TimeoutException("Duplicate sheet with views timed out");
                }
                catch (Exception ex)
                {
                    throw new Exception($"Duplicate sheet with views failed: {ex.Message}");
                }
            }
        }
    }
}
