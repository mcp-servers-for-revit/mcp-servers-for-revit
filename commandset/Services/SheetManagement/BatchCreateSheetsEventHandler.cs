using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCPCommandSet.Models.Common;
using RevitMCPCommandSet.Utils;
using RevitMCPSDK.API.Interfaces;

namespace RevitMCPCommandSet.Services.SheetManagement
{
    public class BatchCreateSheetsEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        private List<SheetDefinition> _sheets;
        private string _defaultTitleBlockName;

        public AIResult<object> Result { get; private set; }
        public bool TaskCompleted { get; private set; }
        private readonly ManualResetEvent _resetEvent = new ManualResetEvent(false);

        public void SetParameters(List<SheetDefinition> sheets, string defaultTitleBlockName)
        {
            _sheets = sheets;
            _defaultTitleBlockName = defaultTitleBlockName;
            TaskCompleted = false;
            _resetEvent.Reset();
        }

        public bool WaitForCompletion(int timeoutMilliseconds = 10000)
        {
            return _resetEvent.WaitOne(timeoutMilliseconds);
        }

        public void Execute(UIApplication app)
        {
            try
            {
                var doc = app.ActiveUIDocument.Document;
                var results = new List<object>();
                int successCount = 0;

                ElementId defaultTitleBlockId = FindTitleBlock(doc, _defaultTitleBlockName);
                if (defaultTitleBlockId == null || defaultTitleBlockId == ElementId.InvalidElementId)
                {
                    defaultTitleBlockId = new FilteredElementCollector(doc)
                        .OfCategory(BuiltInCategory.OST_TitleBlocks)
                        .WhereElementIsElementType()
                        .FirstElementId();
                }

                using (var transaction = new Transaction(doc, "Batch Create Sheets"))
                {
                    transaction.Start();
                    try
                    {

                    foreach (var sheetDef in _sheets)
                    {
                        try
                        {
                            ElementId titleBlockId = !string.IsNullOrEmpty(sheetDef.TitleBlockName)
                                ? FindTitleBlock(doc, sheetDef.TitleBlockName) ?? defaultTitleBlockId
                                : defaultTitleBlockId;

                            var sheet = ViewSheet.Create(doc, titleBlockId ?? ElementId.InvalidElementId);
                            sheet.SheetNumber = sheetDef.Number;
                            sheet.Name = sheetDef.Name;

                            var placedViews = new List<object>();
                            if (sheetDef.ViewIds != null)
                            {
                                foreach (var viewId in sheetDef.ViewIds)
                                {
                                    try
                                    {
                                        var vid = Utils.ElementIdExtensions.FromLong(viewId);
                                        if (Viewport.CanAddViewToSheet(doc, sheet.Id, vid))
                                        {
                                            var vp = Viewport.Create(doc, sheet.Id, vid, new XYZ(0.5, 0.5, 0));
                                            placedViews.Add(new { viewId, success = true });
                                        }
                                        else
                                        {
                                            placedViews.Add(new { viewId, success = false, message = "View cannot be added to sheet (already placed or is a template)" });
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        placedViews.Add(new { viewId, success = false, message = ex.Message });
                                    }
                                }
                            }

                            successCount++;
                            results.Add(new
                            {
                                sheetId = sheet.Id.GetValue(),
                                number = sheet.SheetNumber,
                                name = sheet.Name,
                                success = true,
                                viewsPlaced = placedViews
                            });
                        }
                        catch (Exception ex)
                        {
                            results.Add(new
                            {
                                sheetId = (long)0,
                                number = sheetDef.Number,
                                name = sheetDef.Name,
                                success = false,
                                message = ex.Message
                            });
                        }
                    }

                    transaction.Commit();
                    }
                    catch
                    {
                        if (transaction.GetStatus() == TransactionStatus.Started)
                            transaction.RollBack();
                        throw;
                    }
                }

                Result = new AIResult<object>
                {
                    Success = successCount > 0,
                    Message = $"Created {successCount}/{_sheets.Count} sheets",
                    Response = new { totalCreated = successCount, sheets = results }
                };
            }
            catch (Exception ex)
            {
                Result = new AIResult<object> { Success = false, Message = $"Batch create sheets failed: {ex.Message}" };
            }
            finally
            {
                TaskCompleted = true;
                _resetEvent.Set();
            }
        }

        private ElementId FindTitleBlock(Document doc, string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            return new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_TitleBlocks)
                .WhereElementIsElementType()
                .FirstOrDefault(e => e.Name.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0)?.Id;
        }

        public string GetName() => "Batch Create Sheets";
    }

    public class SheetDefinition
    {
        public string Number { get; set; }
        public string Name { get; set; }
        public string TitleBlockName { get; set; }
        public List<long> ViewIds { get; set; }
    }
}
