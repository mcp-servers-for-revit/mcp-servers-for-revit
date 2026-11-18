using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCPCommandSet.Models.Common;
using RevitMCPSDK.API.Interfaces;

namespace RevitMCPCommandSet.Services
{
    public class CreateRevisionEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        private readonly ManualResetEvent _resetEvent = new ManualResetEvent(false);

        public string Action { get; set; } = "list";
        public string RevisionDate { get; set; } = "";
        public string RevisionDescription { get; set; } = "";
        public string IssuedBy { get; set; } = "";
        public string IssuedTo { get; set; } = "";
        public List<long> SheetIds { get; set; } = new List<long>();
        public AIResult<object> Result { get; private set; }

        public void SetParameters(string action, string revisionDate, string revisionDescription, string issuedBy, string issuedTo, List<long> sheetIds)
        {
            Action = action ?? "list";
            RevisionDate = revisionDate ?? "";
            RevisionDescription = revisionDescription ?? "";
            IssuedBy = issuedBy ?? "";
            IssuedTo = issuedTo ?? "";
            SheetIds = sheetIds ?? new List<long>();
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

                var action = (Action ?? "list").Trim().ToLower();
                switch (action)
                {
                    case "create":
                        CreateRevision(doc);
                        break;
                    case "add_to_sheets":
                        AddRevisionToSheets(doc);
                        break;
                    case "list":
                        ListRevisions(doc);
                        break;
                    default:
                        throw new ArgumentException($"Unknown revision action: '{Action}'. Use 'list', 'create', or 'add_to_sheets'.");
                }
            }
            catch (Exception ex)
            {
                Result = new AIResult<object> { Success = false, Message = $"Revision operation failed: {ex.Message}" };
            }
            finally
            {
                _resetEvent.Set();
            }
        }

        private void ListRevisions(Document doc)
        {
            var revisions = Revision.GetAllRevisionIds(doc)
                .Select(id => doc.GetElement(id) as Revision)
                .Where(r => r != null)
                .Select(r => new
                {
#if REVIT2024_OR_GREATER
                    id = r.Id.Value,
#else
                    id = r.Id.IntegerValue,
#endif
                    sequenceNumber = r.SequenceNumber,
                    date = r.RevisionDate,
                    description = r.Description,
                    issuedBy = r.IssuedBy,
                    issuedTo = r.IssuedTo,
                    issued = r.Issued,
                    visibility = r.Visibility.ToString()
                })
                .ToList();

            Result = new AIResult<object>
            {
                Success = true,
                Message = $"Found {revisions.Count} revisions",
                Response = revisions
            };
        }

        private void CreateRevision(Document doc)
        {
            Revision revision;
            using (var transaction = new Transaction(doc, "Create Revision"))
            {
                transaction.Start();
                try
                {
                    revision = Revision.Create(doc);
                    if (!string.IsNullOrEmpty(RevisionDate))
                        revision.RevisionDate = RevisionDate;
                    if (!string.IsNullOrEmpty(RevisionDescription))
                        revision.Description = RevisionDescription;
                    if (!string.IsNullOrEmpty(IssuedBy))
                        revision.IssuedBy = IssuedBy;
                    if (!string.IsNullOrEmpty(IssuedTo))
                        revision.IssuedTo = IssuedTo;

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
                Success = true,
                Message = $"Created revision: {revision.Description}",
                Response = new
                {
#if REVIT2024_OR_GREATER
                    id = revision.Id.Value,
#else
                    id = revision.Id.IntegerValue,
#endif
                    sequenceNumber = revision.SequenceNumber,
                    date = revision.RevisionDate,
                    description = revision.Description
                }
            };
        }

        private void AddRevisionToSheets(Document doc)
        {
            if (SheetIds.Count == 0)
                throw new ArgumentException("sheetIds required for add_to_sheets action");

            // Get the latest revision
            var revisionIds = Revision.GetAllRevisionIds(doc);
            if (revisionIds.Count == 0)
                throw new Exception("No revisions exist in the project");

            var latestRevisionId = revisionIds.Last();
            int successCount = 0;

            using (var transaction = new Transaction(doc, "Add Revision to Sheets"))
            {
                transaction.Start();
                try
                {
                    foreach (var sheetId in SheetIds)
                    {
                        var sheet = doc.GetElement(ToElementId(sheetId)) as ViewSheet;
                        if (sheet == null) continue;

                        var currentRevisions = sheet.GetAdditionalRevisionIds().ToList();
                        if (!currentRevisions.Contains(latestRevisionId))
                        {
                            currentRevisions.Add(latestRevisionId);
                            sheet.SetAdditionalRevisionIds(currentRevisions);
                            successCount++;
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
                Message = $"Added revision to {successCount} sheets"
            };
        }

        private ElementId ToElementId(long id)
        {
#if REVIT2024_OR_GREATER
            return new ElementId(id);
#else
            return new ElementId((int)id);
#endif
        }

        public string GetName() => "Create Revision";
    }
}
