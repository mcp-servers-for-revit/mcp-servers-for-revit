using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCPCommandSet.Models.Common;
using RevitMCPSDK.API.Interfaces;

namespace RevitMCPCommandSet.Services
{
    public class GetWorksetsEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        private readonly ManualResetEvent _resetEvent = new ManualResetEvent(false);

        public bool IncludeSystemWorksets { get; set; } = false;
        public AIResult<object> Result { get; private set; }

        public void SetParameters(bool includeSystemWorksets = false)
        {
            IncludeSystemWorksets = includeSystemWorksets;
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

                if (!doc.IsWorkshared)
                {
                    Result = new AIResult<object>
                    {
                        Success = true,
                        Message = "Project is not workshared",
                        Response = new List<object>()
                    };
                    return;
                }

                var worksets = new List<object>();

                FilteredWorksetCollector wsCollector;
                if (IncludeSystemWorksets)
                {
                    wsCollector = new FilteredWorksetCollector(doc);
                }
                else
                {
                    wsCollector = new FilteredWorksetCollector(doc)
                        .OfKind(WorksetKind.UserWorkset);
                }

                foreach (var ws in wsCollector)
                {
                    worksets.Add(new
                    {
                        id = ws.Id.IntegerValue,
                        name = ws.Name,
                        kind = ws.Kind.ToString(),
                        isOpen = ws.IsOpen,
                        isEditable = ws.IsEditable,
                        owner = ws.Owner,
                        isDefaultWorkset = ws.IsDefaultWorkset,
                        isVisibleByDefault = ws.IsVisibleByDefault
                    });
                }

                Result = new AIResult<object>
                {
                    Success = true,
                    Message = $"Retrieved {worksets.Count} workset(s) successfully",
                    Response = worksets
                };
            }
            catch (Exception ex)
            {
                Result = new AIResult<object>
                {
                    Success = false,
                    Message = $"Failed to get worksets: {ex.Message}"
                };
            }
            finally
            {
                _resetEvent.Set();
            }
        }

        public string GetName() => "Get Worksets";
    }
}
