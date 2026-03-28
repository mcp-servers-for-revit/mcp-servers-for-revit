using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCPCommandSet.Models.Common;
using RevitMCPSDK.API.Interfaces;

namespace RevitMCPCommandSet.Services
{
    public class SetElementWorksetEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        private readonly ManualResetEvent _resetEvent = new ManualResetEvent(false);

        public List<SetWorksetRequest> Requests { get; set; }
        public AIResult<List<SetWorksetResult>> Result { get; private set; }

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
                    Result = new AIResult<List<SetWorksetResult>>
                    {
                        Success = false,
                        Message = "Project is not workshared. Worksets are not available.",
                        Response = new List<SetWorksetResult>()
                    };
                    return;
                }

                var results = new List<SetWorksetResult>();

                using (var transaction = new Transaction(doc, "Set Element Workset"))
                {
                    transaction.Start();

                    foreach (var request in Requests)
                    {
                        var result = new SetWorksetResult
                        {
                            ElementId = request.ElementId,
                            WorksetName = request.WorksetName
                        };

                        try
                        {
                            // Find the target workset by name
                            var wsCollector = new FilteredWorksetCollector(doc)
                                .OfKind(WorksetKind.UserWorkset);

                            Workset targetWorkset = null;
                            foreach (var ws in wsCollector)
                            {
                                if (ws.Name.Equals(request.WorksetName, StringComparison.OrdinalIgnoreCase))
                                {
                                    targetWorkset = ws;
                                    break;
                                }
                            }

                            if (targetWorkset == null)
                            {
                                result.Success = false;
                                result.Message = $"Workset '{request.WorksetName}' not found";
                                results.Add(result);
                                continue;
                            }

                            var elementId = new ElementId(request.ElementId);
                            var element = doc.GetElement(elementId);
                            if (element == null)
                            {
                                result.Success = false;
                                result.Message = $"Element {request.ElementId} not found";
                                results.Add(result);
                                continue;
                            }

                            var worksetParam = element.get_Parameter(BuiltInParameter.ELEM_PARTITION_PARAM);
                            if (worksetParam == null)
                            {
                                result.Success = false;
                                result.Message = $"Element {request.ElementId} does not support workset assignment";
                                results.Add(result);
                                continue;
                            }

                            if (worksetParam.IsReadOnly)
                            {
                                result.Success = false;
                                result.Message = $"Workset parameter on element {request.ElementId} is read-only";
                                results.Add(result);
                                continue;
                            }

                            worksetParam.Set(targetWorkset.Id.IntegerValue);
                            result.Success = true;
                            result.Message = $"Element moved to workset '{request.WorksetName}' successfully";
                        }
                        catch (Exception ex)
                        {
                            result.Success = false;
                            result.Message = ex.Message;
                        }

                        results.Add(result);
                    }

                    transaction.Commit();
                }

                int successCount = results.Count(r => r.Success);
                Result = new AIResult<List<SetWorksetResult>>
                {
                    Success = successCount > 0,
                    Message = $"Moved {successCount}/{results.Count} element(s) to the target workset successfully",
                    Response = results
                };
            }
            catch (Exception ex)
            {
                Result = new AIResult<List<SetWorksetResult>>
                {
                    Success = false,
                    Message = $"Failed to set element workset: {ex.Message}",
                    Response = new List<SetWorksetResult>()
                };
            }
            finally
            {
                _resetEvent.Set();
            }
        }

        public string GetName() => "Set Element Workset";
    }
}
