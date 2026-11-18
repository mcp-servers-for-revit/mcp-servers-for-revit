using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCPCommandSet.Models.Common;
using RevitMCPSDK.API.Interfaces;

namespace RevitMCPCommandSet.Services
{
    public class CreateWorksetEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        private readonly ManualResetEvent _resetEvent = new ManualResetEvent(false);

        public string WorksetName { get; set; }
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
                        Message = "Project is not workshared. Enable worksharing first."
                    };
                    return;
                }

                // Validate name doesn't contain forbidden characters
                var forbidden = new[] { '{', '}', '[', ']', '|', ';' };
                foreach (var c in forbidden)
                {
                    if (WorksetName.Contains(c.ToString()))
                    {
                        Result = new AIResult<object>
                        {
                            Success = false,
                            Message = $"Workset name cannot contain '{c}'"
                        };
                        return;
                    }
                }

                if (!WorksetTable.IsWorksetNameUnique(doc, WorksetName))
                {
                    Result = new AIResult<object>
                    {
                        Success = false,
                        Message = $"A workset named '{WorksetName}' already exists"
                    };
                    return;
                }

                using (var transaction = new Transaction(doc, "Create Workset"))
                {
                    transaction.Start();
                    try
                    {
                        var workset = Workset.Create(doc, WorksetName);
                        transaction.Commit();

                        Result = new AIResult<object>
                        {
                            Success = true,
                            Message = $"Workset '{WorksetName}' created successfully",
                            Response = new
                            {
                                id = workset.Id.IntegerValue,
                                name = workset.Name,
                                kind = workset.Kind.ToString(),
                                isOpen = workset.IsOpen,
                                isEditable = workset.IsEditable,
                                isDefaultWorkset = workset.IsDefaultWorkset
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
                    Message = $"Failed to create workset: {ex.Message}"
                };
            }
            finally
            {
                _resetEvent.Set();
            }
        }

        public string GetName() => "Create Workset";
    }
}
