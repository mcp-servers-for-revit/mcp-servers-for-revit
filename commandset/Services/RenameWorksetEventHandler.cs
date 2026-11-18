using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCPCommandSet.Models.Common;
using RevitMCPSDK.API.Interfaces;

namespace RevitMCPCommandSet.Services
{
    public class RenameWorksetEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        private readonly ManualResetEvent _resetEvent = new ManualResetEvent(false);

        public string CurrentName { get; set; }
        public string NewName { get; set; }
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

                // Validate new name characters
                var forbidden = new[] { '{', '}', '[', ']', '|', ';' };
                foreach (var c in forbidden)
                {
                    if (NewName.Contains(c.ToString()))
                    {
                        Result = new AIResult<object>
                        {
                            Success = false,
                            Message = $"Workset name cannot contain '{c}'"
                        };
                        return;
                    }
                }

                // Find workset by current name
                Workset targetWorkset = null;
                foreach (var ws in new FilteredWorksetCollector(doc).OfKind(WorksetKind.UserWorkset))
                {
                    if (ws.Name.Equals(CurrentName, StringComparison.OrdinalIgnoreCase))
                    {
                        targetWorkset = ws;
                        break;
                    }
                }

                if (targetWorkset == null)
                {
                    Result = new AIResult<object>
                    {
                        Success = false,
                        Message = $"Workset '{CurrentName}' not found"
                    };
                    return;
                }

                if (!WorksetTable.IsWorksetNameUnique(doc, NewName))
                {
                    Result = new AIResult<object>
                    {
                        Success = false,
                        Message = $"A workset named '{NewName}' already exists"
                    };
                    return;
                }

                using (var transaction = new Transaction(doc, "Rename Workset"))
                {
                    transaction.Start();
                    try
                    {
                        WorksetTable.RenameWorkset(doc, targetWorkset.Id, NewName);
                        transaction.Commit();

                        Result = new AIResult<object>
                        {
                            Success = true,
                            Message = $"Workset renamed from '{CurrentName}' to '{NewName}' successfully",
                            Response = new
                            {
                                id = targetWorkset.Id.IntegerValue,
                                previousName = CurrentName,
                                newName = NewName
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
                Result = new AIResult<object>
                {
                    Success = false,
                    Message = $"Failed to rename workset: {ex.Message}"
                };
            }
            finally
            {
                _resetEvent.Set();
            }
        }

        public string GetName() => "Rename Workset";
    }
}
