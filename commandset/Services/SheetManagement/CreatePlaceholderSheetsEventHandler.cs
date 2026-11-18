using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCPCommandSet.Models.Common;
using RevitMCPCommandSet.Utils;
using RevitMCPSDK.API.Interfaces;

namespace RevitMCPCommandSet.Services.SheetManagement
{
    public class CreatePlaceholderSheetsEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        private string _action;
        private List<PlaceholderSheetDefinition> _sheets;
        private List<long> _sheetIds;
        private long? _titleBlockId;

        public AIResult<object> Result { get; private set; }
        public bool TaskCompleted { get; private set; }
        private readonly ManualResetEvent _resetEvent = new ManualResetEvent(false);

        public void SetParameters(string action, List<PlaceholderSheetDefinition> sheets, List<long> sheetIds, long? titleBlockId)
        {
            _action = action;
            _sheets = sheets;
            _sheetIds = sheetIds;
            _titleBlockId = titleBlockId;
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

                switch (_action)
                {
                    case "create":
                        ExecuteCreate(doc);
                        break;
                    case "list":
                        ExecuteList(doc);
                        break;
                    case "convert":
                        ExecuteConvert(doc);
                        break;
                    case "delete":
                        ExecuteDelete(doc);
                        break;
                    default:
                        Result = new AIResult<object> { Success = false, Message = $"Unknown action: {_action}" };
                        break;
                }
            }
            catch (Exception ex)
            {
                Result = new AIResult<object> { Success = false, Message = $"Create placeholder sheets failed: {ex.Message}" };
            }
            finally
            {
                TaskCompleted = true;
                _resetEvent.Set();
            }
        }

        private void ExecuteCreate(Document doc)
        {
            var results = new List<object>();
            int successCount = 0;

            using (var transaction = new Transaction(doc, "Create Placeholder Sheets"))
            {
                transaction.Start();
                try
                {
                    foreach (var sheetDef in _sheets)
                    {
                        try
                        {
                            var sheet = ViewSheet.CreatePlaceholder(doc);
                            sheet.SheetNumber = sheetDef.Number;
                            sheet.Name = sheetDef.Name;

                            successCount++;
                            results.Add(new
                            {
                                sheetId = sheet.Id.GetValue(),
                                number = sheet.SheetNumber,
                                name = sheet.Name,
                                isPlaceholder = true,
                                success = true
                            });
                        }
                        catch (Exception ex)
                        {
                            results.Add(new
                            {
                                sheetId = (long)0,
                                number = sheetDef.Number,
                                name = sheetDef.Name,
                                isPlaceholder = true,
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
                Message = $"Created {successCount}/{_sheets.Count} placeholder sheets",
                Response = new { totalCreated = successCount, sheets = results }
            };
        }

        private void ExecuteList(Document doc)
        {
            var placeholders = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSheet))
                .Cast<ViewSheet>()
                .Where(s => s.IsPlaceholder)
                .Select(s => new
                {
                    sheetId = s.Id.GetValue(),
                    number = s.SheetNumber,
                    name = s.Name,
                    isPlaceholder = true
                })
                .ToList();

            Result = new AIResult<object>
            {
                Success = true,
                Message = $"Found {placeholders.Count} placeholder sheets",
                Response = new { count = placeholders.Count, sheets = placeholders }
            };
        }

        private void ExecuteConvert(Document doc)
        {
            var results = new List<object>();
            int successCount = 0;

#if REVIT2024_OR_GREATER
            var titleBlockElemId = new ElementId(_titleBlockId.Value);
#else
            var titleBlockElemId = new ElementId((int)_titleBlockId.Value);
#endif

            using (var transaction = new Transaction(doc, "Convert Placeholder Sheets"))
            {
                transaction.Start();
                try
                {
                    foreach (var sheetIdValue in _sheetIds)
                    {
                        try
                        {
                            var sheetElemId = RevitMCPCommandSet.Utils.ElementIdExtensions.FromLong(sheetIdValue);
                            var existingSheet = doc.GetElement(sheetElemId) as ViewSheet;

                            if (existingSheet == null)
                            {
                                results.Add(new
                                {
                                    sheetId = sheetIdValue,
                                    success = false,
                                    message = "Sheet not found"
                                });
                                continue;
                            }

                            if (!existingSheet.IsPlaceholder)
                            {
                                results.Add(new
                                {
                                    sheetId = sheetIdValue,
                                    number = existingSheet.SheetNumber,
                                    name = existingSheet.Name,
                                    success = false,
                                    message = "Sheet is not a placeholder"
                                });
                                continue;
                            }

                            // Save number and name before deleting
                            string savedNumber = existingSheet.SheetNumber;
                            string savedName = existingSheet.Name;

                            // Delete the placeholder
                            doc.Delete(sheetElemId);

                            // Create real sheet with same number and name
                            var newSheet = ViewSheet.Create(doc, titleBlockElemId);
                            newSheet.SheetNumber = savedNumber;
                            newSheet.Name = savedName;

                            successCount++;
                            results.Add(new
                            {
                                sheetId = newSheet.Id.GetValue(),
                                number = newSheet.SheetNumber,
                                name = newSheet.Name,
                                isPlaceholder = false,
                                success = true
                            });
                        }
                        catch (Exception ex)
                        {
                            results.Add(new
                            {
                                sheetId = sheetIdValue,
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
                Message = $"Converted {successCount}/{_sheetIds.Count} placeholder sheets to real sheets",
                Response = new { totalConverted = successCount, sheets = results }
            };
        }

        private void ExecuteDelete(Document doc)
        {
            int successCount = 0;
            var results = new List<object>();

            using (var transaction = new Transaction(doc, "Delete Placeholder Sheets"))
            {
                transaction.Start();
                try
                {
                    foreach (var sheetIdValue in _sheetIds)
                    {
                        try
                        {
                            var sheetElemId = RevitMCPCommandSet.Utils.ElementIdExtensions.FromLong(sheetIdValue);
                            var sheet = doc.GetElement(sheetElemId) as ViewSheet;

                            if (sheet == null)
                            {
                                results.Add(new { sheetId = sheetIdValue, success = false, message = "Sheet not found" });
                                continue;
                            }

                            doc.Delete(sheetElemId);
                            successCount++;
                            results.Add(new { sheetId = sheetIdValue, success = true });
                        }
                        catch (Exception ex)
                        {
                            results.Add(new { sheetId = sheetIdValue, success = false, message = ex.Message });
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
                Message = $"Deleted {successCount}/{_sheetIds.Count} sheets",
                Response = new { totalDeleted = successCount, sheets = results }
            };
        }

        public string GetName() => "Create Placeholder Sheets";
    }

    public class PlaceholderSheetDefinition
    {
        public string Number { get; set; }
        public string Name { get; set; }
    }
}
