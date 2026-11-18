using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCPCommandSet.Models.Common;
using RevitMCPSDK.API.Interfaces;

namespace RevitMCPCommandSet.Services
{
    public class SetActiveWorksetEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
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
                        Message = "Project is not workshared"
                    };
                    return;
                }

                // Find workset by name
                Workset targetWorkset = null;
                foreach (var ws in new FilteredWorksetCollector(doc).OfKind(WorksetKind.UserWorkset))
                {
                    if (ws.Name.Equals(WorksetName, StringComparison.OrdinalIgnoreCase))
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
                        Message = $"Workset '{WorksetName}' not found"
                    };
                    return;
                }

                var worksetTable = doc.GetWorksetTable();
                var previousId = worksetTable.GetActiveWorksetId();
                var previousName = worksetTable.GetWorkset(previousId)?.Name ?? "Unknown";

                // SetActiveWorksetId does NOT require a transaction
                worksetTable.SetActiveWorksetId(targetWorkset.Id);

                Result = new AIResult<object>
                {
                    Success = true,
                    Message = $"Active workset changed from '{previousName}' to '{WorksetName}'",
                    Response = new
                    {
                        previousWorkset = previousName,
                        activeWorkset = WorksetName,
                        worksetId = targetWorkset.Id.IntegerValue
                    }
                };
            }
            catch (Exception ex)
            {
                Result = new AIResult<object>
                {
                    Success = false,
                    Message = $"Failed to set active workset: {ex.Message}"
                };
            }
            finally
            {
                _resetEvent.Set();
            }
        }

        public string GetName() => "Set Active Workset";
    }
}
