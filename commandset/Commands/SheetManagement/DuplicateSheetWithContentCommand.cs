using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Services.SheetManagement;
using RevitMCPSDK.API.Base;
using System;

namespace RevitMCPCommandSet.Commands.SheetManagement
{
    public class DuplicateSheetWithContentCommand : ExternalEventCommandBase
    {
        private static readonly object _executionLock = new object();
        private DuplicateSheetWithContentEventHandler _handler => (DuplicateSheetWithContentEventHandler)Handler;

        public override string CommandName => "duplicate_sheet_with_content";

        public DuplicateSheetWithContentCommand(UIApplication uiApp)
            : base(new DuplicateSheetWithContentEventHandler(), uiApp) { }

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
                    _handler.CopyRevisions = parameters?["copyRevisions"]?.Value<bool>() ?? false;
                    _handler.SheetNumberPrefix = parameters?["sheetNumberPrefix"]?.Value<string>() ?? "";
                    _handler.SheetNumberSuffix = parameters?["sheetNumberSuffix"]?.Value<string>() ?? "";

                    _handler.SetParameters();

                    if (RaiseAndWaitForCompletion(30000))
                        return _handler.Result;

                    throw new TimeoutException("Duplicate sheet timed out");
                }
                catch (Exception ex)
                {
                    throw new Exception($"Duplicate sheet failed: {ex.Message}");
                }
            }
        }
    }
}
