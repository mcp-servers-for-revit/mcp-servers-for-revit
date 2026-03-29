using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCPSDK.API.Interfaces;

namespace RevitMCPCommandSet.Services
{
    public class DeleteElementEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        // Execution result
        public bool IsSuccess { get; private set; }

        // Number of successfully deleted elements
        public int DeletedCount { get; private set; }
        // State synchronization object
        public bool TaskCompleted { get; private set; }
        private readonly ManualResetEvent _resetEvent = new ManualResetEvent(false);
        // Element ID array to delete
        public string[] ElementIds { get; set; }
        // Implement IWaitableExternalEventHandler interface
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
                    if (int.TryParse(idStr, out int elementIdValue))
                    {
                        var elementId = new ElementId(elementIdValue);
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
                    TaskDialog.Show("Warning", $"The following IDs are invalid or elements do not exist: {string.Join(", ", invalidIds)}");
                }
                // If there are elements that can be deleted, execute deletion
                if (elementIdsToDelete.Count > 0)
                {
                    using (var transaction = new Transaction(doc, "Delete Elements"))
                    {
                        transaction.Start();

                        // Batch delete elements
                        ICollection<ElementId> deletedIds = doc.Delete(elementIdsToDelete);
                        DeletedCount = deletedIds.Count;

                        transaction.Commit();
                    }
                    IsSuccess = true;
                }
                else
                {
                    TaskDialog.Show("Error", "No valid elements to delete");
                    IsSuccess = false;
                }
            }
            catch (Exception ex)
            {
                TaskDialog.Show("Error", "Failed to delete element: " + ex.Message);
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
