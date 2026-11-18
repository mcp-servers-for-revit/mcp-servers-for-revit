using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCPSDK.API.Interfaces;

namespace RevitMCPCommandSet.Services.Workflow
{
    public class WorkflowSheetSetEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        private readonly ManualResetEvent _resetEvent = new ManualResetEvent(false);

        public List<SheetDefinition> Sheets { get; set; } = new();
        public string TitleBlockName { get; set; } = "";
        public object Result { get; private set; }

        public class SheetDefinition
        {
            public string Number { get; set; } = "";
            public string Name { get; set; } = "";
        }

        public void SetParameters(List<SheetDefinition> sheets, string titleBlockName)
        {
            Sheets = sheets;
            TitleBlockName = titleBlockName;
            _resetEvent.Reset();
        }

        public bool WaitForCompletion(int timeoutMilliseconds = 60000)
        {
            return _resetEvent.WaitOne(timeoutMilliseconds);
        }

        public void Execute(UIApplication app)
        {
            try
            {
                var doc = app.ActiveUIDocument.Document;

                // Find title block type
                ElementId titleBlockId = FindTitleBlock(doc, TitleBlockName);
                if (titleBlockId == null || titleBlockId == ElementId.InvalidElementId)
                {
                    Result = new { success = false, error = "No title block found in the project. Load a title block family first." };
                    return;
                }

                if (Sheets == null || Sheets.Count == 0)
                {
                    Result = new { success = false, error = "No sheet definitions provided." };
                    return;
                }

                var createdSheets = new List<object>();
                var errors = new List<string>();

                using (var tx = new Transaction(doc, "Workflow Sheet Set"))
                {
                    tx.Start();

                    foreach (var sheetDef in Sheets)
                    {
                        try
                        {
                            var sheet = ViewSheet.Create(doc, titleBlockId);

                            if (!string.IsNullOrEmpty(sheetDef.Number))
                                sheet.SheetNumber = sheetDef.Number;

                            if (!string.IsNullOrEmpty(sheetDef.Name))
                                sheet.Name = sheetDef.Name;

                            createdSheets.Add(new
                            {
#if REVIT2024_OR_GREATER
                                id = sheet.Id.Value,
#else
                                id = sheet.Id.IntegerValue,
#endif
                                number = sheet.SheetNumber,
                                name = sheet.Name
                            });
                        }
                        catch (Exception ex)
                        {
                            errors.Add($"Sheet '{sheetDef.Number} - {sheetDef.Name}' failed: {ex.Message}");
                        }
                    }

                    tx.Commit();
                }

                Result = new
                {
                    success = true,
                    sheetsCreated = createdSheets.Count,
                    sheets = createdSheets,
                    errors = errors.Count > 0 ? errors : null
                };
            }
            catch (Exception ex)
            {
                Result = new { success = false, error = ex.Message };
            }
            finally
            {
                _resetEvent.Set();
            }
        }

        private ElementId FindTitleBlock(Document doc, string titleBlockName)
        {
            var titleBlocks = new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .OfCategory(BuiltInCategory.OST_TitleBlocks)
                .Cast<FamilySymbol>()
                .ToList();

            if (titleBlocks.Count == 0) return null;

            // If name specified, try to match
            if (!string.IsNullOrEmpty(titleBlockName))
            {
                var match = titleBlocks.FirstOrDefault(tb =>
                    tb.Name.Equals(titleBlockName, StringComparison.OrdinalIgnoreCase) ||
                    tb.FamilyName.Equals(titleBlockName, StringComparison.OrdinalIgnoreCase) ||
                    $"{tb.FamilyName}: {tb.Name}".Equals(titleBlockName, StringComparison.OrdinalIgnoreCase));

                if (match != null)
                {
                    if (!match.IsActive) match.Activate();
                    return match.Id;
                }
            }

            // Use first available
            var first = titleBlocks.First();
            if (!first.IsActive) first.Activate();
            return first.Id;
        }

        public string GetName() => "Workflow Sheet Set";
    }
}
