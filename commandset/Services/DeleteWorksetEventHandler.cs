using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCPCommandSet.Helpers;
using RevitMCPCommandSet.Models.Common;
using RevitMCPSDK.API.Interfaces;

namespace RevitMCPCommandSet.Services
{
    public class DeleteWorksetEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        private readonly ManualResetEvent _resetEvent = new ManualResetEvent(false);

        public string WorksetName { get; set; }
        public string MoveToWorksetName { get; set; }
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

                if (!doc.IsWorkshared)
                {
                    Result = new AIResult<object>
                    {
                        Success = false,
                        Message = "Project is not workshared"
                    };
                    return;
                }

                // Build workset lookup
                var worksetLookup = new Dictionary<string, Workset>(StringComparer.OrdinalIgnoreCase);
                foreach (var ws in new FilteredWorksetCollector(doc).OfKind(WorksetKind.UserWorkset))
                {
                    worksetLookup[ws.Name] = ws;
                }

                if (!worksetLookup.TryGetValue(WorksetName, out var targetWorkset))
                {
                    Result = new AIResult<object>
                    {
                        Success = false,
                        Message = $"Workset '{WorksetName}' not found"
                    };
                    return;
                }

                if (!worksetLookup.TryGetValue(MoveToWorksetName, out var destinationWorkset))
                {
                    Result = new AIResult<object>
                    {
                        Success = false,
                        Message = $"Destination workset '{MoveToWorksetName}' not found"
                    };
                    return;
                }

                if (targetWorkset.Id == destinationWorkset.Id)
                {
                    Result = new AIResult<object>
                    {
                        Success = false,
                        Message = "Cannot delete a workset and move its elements to itself"
                    };
                    return;
                }

#if REVIT2022_OR_GREATER
                // Count elements in workset for confirmation
                var elementCount = new FilteredElementCollector(doc)
                    .WherePasses(new ElementWorksetFilter(targetWorkset.Id, false))
                    .GetElementCount();

                // Check if deletion is possible
                var deleteSettings = new DeleteWorksetSettings(
                    DeleteWorksetOption.MoveElementsToWorkset, destinationWorkset.Id);

                if (!WorksetTable.CanDeleteWorkset(doc, targetWorkset.Id, deleteSettings))
                {
                    Result = new AIResult<object>
                    {
                        Success = false,
                        Message = $"Cannot delete workset '{WorksetName}'. It may be a system workset, owned by another user, or the destination workset is invalid."
                    };
                    return;
                }

                if (!ConfirmationHelper.Confirm($"delete workset '{WorksetName}' (moving {elementCount} elements to '{MoveToWorksetName}'). This action affects", 1))
                {
                    Result = new AIResult<object>
                    {
                        Success = false,
                        Message = "Operation cancelled by user"
                    };
                    return;
                }

                // Checkout the workset before deleting
                var worksetIds = new List<WorksetId> { targetWorkset.Id };
                WorksharingUtils.CheckoutWorksets(doc, worksetIds);

                using (var transaction = new Transaction(doc, "Delete Workset"))
                {
                    transaction.Start();
                    try
                    {
                        WorksetTable.DeleteWorkset(doc, targetWorkset.Id, deleteSettings);
                        transaction.Commit();

                        Result = new AIResult<object>
                        {
                            Success = true,
                            Message = $"Workset '{WorksetName}' deleted. {elementCount} element(s) moved to '{MoveToWorksetName}'.",
                            Response = new
                            {
                                deletedWorkset = WorksetName,
                                movedElementsTo = MoveToWorksetName,
                                elementsMoved = elementCount
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
#else
                // DeleteWorksetSettings, WorksetTable.CanDeleteWorkset and
                // WorksetTable.DeleteWorkset arrived in Revit 2022
                Result = new AIResult<object>
                {
                    Success = false,
                    Message = $"Deleting workset '{WorksetName}' requires Revit 2022 or newer"
                };
#endif
            }
            catch (Exception ex)
            {
                Result = new AIResult<object>
                {
                    Success = false,
                    Message = $"Failed to delete workset: {ex.Message}"
                };
            }
            finally
            {
                _resetEvent.Set();
            }
        }

        public string GetName() => "Delete Workset";
    }
}
