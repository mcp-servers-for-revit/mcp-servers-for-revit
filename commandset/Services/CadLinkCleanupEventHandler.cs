using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCPCommandSet.Models.Common;
using RevitMCPSDK.API.Interfaces;

namespace RevitMCPCommandSet.Services
{
    public class CadLinkCleanupEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        private readonly ManualResetEvent _resetEvent = new ManualResetEvent(false);

        public string Action { get; set; } = "list";
        public bool DeleteImports { get; set; } = false;
        public bool DeleteLinks { get; set; } = false;
        public List<long> ElementIds { get; set; } = new List<long>();
        public AIResult<object> Result { get; private set; }

        public bool WaitForCompletion(int timeoutMilliseconds = 10000)
        {
            _resetEvent.Reset();
            return _resetEvent.WaitOne(timeoutMilliseconds);
        }

        public void Execute(UIApplication app)
        {
            try
            {
                var doc = app.ActiveUIDocument.Document;

                switch (Action?.ToLower())
                {
                    case "delete":
                        DeleteCadElements(doc);
                        break;
                    default:
                        ListCadElements(doc);
                        break;
                }
            }
            catch (Exception ex)
            {
                Result = new AIResult<object> { Success = false, Message = $"CAD cleanup failed: {ex.Message}" };
            }
            finally
            {
                _resetEvent.Set();
            }
        }

        private void ListCadElements(Document doc)
        {
            var imports = new FilteredElementCollector(doc)
                .OfClass(typeof(ImportInstance))
                .Cast<ImportInstance>()
                .ToList();

            var cadItems = new List<object>();
            int importCount = 0;
            int linkCount = 0;

            foreach (var import in imports)
            {
                bool isLinked = import.IsLinked;
                if (isLinked) linkCount++; else importCount++;

                string viewName = "";
                if (import.OwnerViewId != ElementId.InvalidElementId)
                {
                    var view = doc.GetElement(import.OwnerViewId) as View;
                    viewName = view?.Name ?? "";
                }

                cadItems.Add(new
                {
                    id = import.Id.Value,
                    name = import.Name,
                    isLinked,
                    category = import.Category?.Name ?? "CAD",
                    ownerView = viewName,
                    pinned = import.Pinned
                });
            }

            // Also check CAD link types
            var cadLinkTypes = new FilteredElementCollector(doc)
                .OfClass(typeof(CADLinkType))
                .ToList();

            var linkTypes = cadLinkTypes.Select(lt => new
            {
                id = lt.Id.Value,
                name = lt.Name,
                type = "CADLinkType"
            }).ToList();

            Result = new AIResult<object>
            {
                Success = true,
                Message = $"Found {importCount} CAD imports and {linkCount} CAD links ({cadLinkTypes.Count} link types)",
                Response = new
                {
                    importCount,
                    linkCount,
                    linkTypeCount = cadLinkTypes.Count,
                    items = cadItems,
                    linkTypes
                }
            };
        }

        private void DeleteCadElements(Document doc)
        {
            var idsToDelete = new List<ElementId>();

            if (ElementIds.Count > 0)
            {
                idsToDelete = ElementIds.Select(id => ToElementId(id)).ToList();
            }
            else
            {
                var imports = new FilteredElementCollector(doc)
                    .OfClass(typeof(ImportInstance))
                    .Cast<ImportInstance>()
                    .ToList();

                foreach (var import in imports)
                {
                    if (DeleteImports && !import.IsLinked)
                        idsToDelete.Add(import.Id);
                    if (DeleteLinks && import.IsLinked)
                        idsToDelete.Add(import.Id);
                }
            }

            if (idsToDelete.Count == 0)
            {
                Result = new AIResult<object> { Success = true, Message = "No CAD elements to delete" };
                return;
            }

            int deletedCount;
            using (var transaction = new Transaction(doc, "Delete CAD Elements"))
            {
                transaction.Start();
                var deleted = doc.Delete(idsToDelete);
                deletedCount = deleted.Count;
                transaction.Commit();
            }

            Result = new AIResult<object>
            {
                Success = true,
                Message = $"Deleted {deletedCount} CAD elements",
                Response = new { deletedCount }
            };
        }

        private ElementId ToElementId(long id)
        {
            return new ElementId(id);
        }

        public string GetName() => "CAD Link Cleanup";
    }
}
