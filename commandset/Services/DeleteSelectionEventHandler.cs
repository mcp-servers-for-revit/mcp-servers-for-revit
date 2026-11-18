using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCPCommandSet.Helpers;
using RevitMCPCommandSet.Models.Common;
using RevitMCPSDK.API.Interfaces;
using System;
using System.Linq;
using System.Threading;

namespace RevitMCPCommandSet.Services
{
    public class DeleteSelectionEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        public string SelectionName { get; set; } = "";

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

                if (string.IsNullOrEmpty(SelectionName))
                    throw new ArgumentException("Selection name is required");

                var selFilter = new FilteredElementCollector(doc)
                    .OfClass(typeof(SelectionFilterElement))
                    .Cast<SelectionFilterElement>()
                    .FirstOrDefault(s => s.Name == SelectionName);

                if (selFilter == null)
                {
                    Result = new AIResult<object>
                    {
                        Success = false,
                        Message = $"Selection '{SelectionName}' not found"
                    };
                    return;
                }

                if (!ConfirmationHelper.Confirm("delete", 1))
                {
                    Result = new AIResult<object>
                    {
                        Success = false,
                        Message = "Operation cancelled by user",
                        Response = new { cancelled = true }
                    };
                    return;
                }

                using (var transaction = new Transaction(doc, "Delete Selection"))
                {
                    transaction.Start();
                    try
                    {
                        doc.Delete(selFilter.Id);
                        transaction.Commit();

                        Result = new AIResult<object>
                        {
                            Success = true,
                            Message = $"Selection '{SelectionName}' deleted",
                            Response = new { name = SelectionName, deleted = true }
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
                Result = new AIResult<object> { Success = false, Message = $"Delete selection failed: {ex.Message}" };
            }
            finally
            {
                TaskCompleted = true;
                _resetEvent.Set();
            }
        }

        public string GetName() => "Delete Selection";
    }
}
