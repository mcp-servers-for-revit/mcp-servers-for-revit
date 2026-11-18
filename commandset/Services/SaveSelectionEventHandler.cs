using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCPCommandSet.Models.Common;
using RevitMCPSDK.API.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace RevitMCPCommandSet.Services
{
    public class SaveSelectionEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        public string SelectionName { get; set; } = "";
        public List<long> ElementIds { get; set; } = new List<long>();
        public bool Overwrite { get; set; } = false;

        public AIResult<object> Result { get; private set; }
        public bool TaskCompleted { get; private set; }
        private readonly ManualResetEvent _resetEvent = new ManualResetEvent(false);

        public void SetParameters() { TaskCompleted = false; _resetEvent.Reset(); }
        public bool WaitForCompletion(int timeoutMilliseconds = 15000) { return _resetEvent.WaitOne(timeoutMilliseconds); }

        public void Execute(UIApplication app)
        {
            try
            {
                var doc = app.ActiveUIDocument.Document;
                var uidoc = app.ActiveUIDocument;

                if (string.IsNullOrEmpty(SelectionName))
                    throw new ArgumentException("Selection name is required");

                // Get element IDs
                ICollection<ElementId> ids;
                if (ElementIds.Count > 0)
                {
                    ids = ElementIds.Select(id =>
                    {
#if REVIT2024_OR_GREATER
                        return new ElementId(id);
#else
                        return new ElementId((int)id);
#endif
                    }).ToList();
                }
                else
                {
                    ids = uidoc.Selection.GetElementIds();
                    if (ids.Count == 0)
                        throw new ArgumentException("No elements selected and no elementIds provided");
                }

                // Check for existing selection with same name
                var existing = new FilteredElementCollector(doc)
                    .OfClass(typeof(SelectionFilterElement))
                    .Cast<SelectionFilterElement>()
                    .FirstOrDefault(s => s.Name == SelectionName);

                using (var transaction = new Transaction(doc, "Save Selection"))
                {
                    transaction.Start();
                    try
                    {
                        if (existing != null)
                        {
                            if (!Overwrite)
                            {
                                transaction.RollBack();
                                Result = new AIResult<object>
                                {
                                    Success = false,
                                    Message = $"Selection '{SelectionName}' already exists. Use overwrite=true to replace it."
                                };
                                return;
                            }
                            doc.Delete(existing.Id);
                        }

                        var selFilter = SelectionFilterElement.Create(doc, SelectionName);
                        selFilter.SetElementIds(ids);
                        transaction.Commit();

                        Result = new AIResult<object>
                        {
                            Success = true,
                            Message = $"Selection '{SelectionName}' saved with {ids.Count} elements",
                            Response = new
                            {
                                name = SelectionName,
                                elementCount = ids.Count,
                                overwritten = existing != null
                            }
                        };
                    }
                    catch
                    {
                        if (transaction.GetStatus() == TransactionStatus.Started)
                            transaction.RollBack();
                        throw;
                    }
                }
            }
            catch (Exception ex)
            {
                Result = new AIResult<object> { Success = false, Message = $"Save selection failed: {ex.Message}" };
            }
            finally
            {
                TaskCompleted = true;
                _resetEvent.Set();
            }
        }

        public string GetName() => "Save Selection";
    }
}
