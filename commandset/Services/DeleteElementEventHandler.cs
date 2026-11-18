using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCPCommandSet.Helpers;
using RevitMCPCommandSet.Utils;
using RevitMCPSDK.API.Interfaces;

namespace RevitMCPCommandSet.Services
{
    public class DeleteElementEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        // Execution result
        public bool IsSuccess { get; private set; }

        // Number of successfully deleted elements
        public int DeletedCount { get; private set; }

        // Error or warning message if execution fails or has warnings
        public string ErrorMessage { get; private set; }

        // Indicates if operation was cancelled by user
        public bool Cancelled { get; private set; }

        // State synchronization object
        public bool TaskCompleted { get; private set; }
        private readonly ManualResetEvent _resetEvent = new ManualResetEvent(false);
        // Element ID array to delete
        public string[] ElementIds { get; set; }
        // If true, only preview what would be deleted without actually deleting
        public bool DryRun { get; set; } = true;
        // Implement IWaitableExternalEventHandler interface
        public void SetDeleteParameters(string[] elementIds)
        {
            ElementIds = elementIds;
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
                DeletedCount = 0;
                if (ElementIds == null || ElementIds.Length == 0)
                {
                    IsSuccess = false;
                    return;
                }
                // Create collection of element IDs to delete
                List<ElementId> elementIdsToDelete = new List<ElementId>();
                List<string> invalidIds = new List<string>();
                foreach (var idStr in ElementIds)
                {
                    if (long.TryParse(idStr, out long elementIdValue))
                    {
                        var elementId = RevitMCPCommandSet.Utils.ElementIdExtensions.FromLong(elementIdValue);
                        // Check if element exists
                        if (doc.GetElement(elementId) != null)
                        {
                            elementIdsToDelete.Add(elementId);
                        }
                    }
                    else
                    {
                        invalidIds.Add(idStr);
                    }
                }
                if (invalidIds.Count > 0)
                {
                    ErrorMessage = $"The following IDs are invalid or elements do not exist: {string.Join(", ", invalidIds)}";
                }
                // Dry run: only report what would be deleted
                if (DryRun)
                {
                    DeletedCount = elementIdsToDelete.Count;
                    IsSuccess = true;
                    return;
                }

                // Confirm deletion with user before proceeding
                if (elementIdsToDelete.Count > 0 && !ConfirmationHelper.Confirm("delete", elementIdsToDelete.Count))
                {
                    IsSuccess = false;
                    Cancelled = true;
                    ErrorMessage = "Operation cancelled by user";
                    return;
                }
                // If there are elements that can be deleted, execute deletion
                if (elementIdsToDelete.Count > 0)
                {
                    using (var transaction = new Transaction(doc, "Delete Elements"))
                    {
                        transaction.Start();
                        try
                        {
                            // Batch delete elements
                            ICollection<ElementId> deletedIds = doc.Delete(elementIdsToDelete);
                            DeletedCount = deletedIds.Count;

                            transaction.Commit();
                        }
                        catch
                        {
                            if (transaction.GetStatus() == TransactionStatus.Started)
                                transaction.RollBack();
                            throw;
                        }
                    }
                    IsSuccess = true;
                }
                else
                {
                    ErrorMessage = "No valid elements to delete";
                    IsSuccess = false;
                }
            }
            catch (Exception ex)
            {
                ErrorMessage = "Failed to delete element: " + ex.Message;
                IsSuccess = false;
            }
            finally
            {
                TaskCompleted = true;
                _resetEvent.Set();
            }
        }
        public string GetName()
        {
            return "Delete Element";
        }
    }
}
