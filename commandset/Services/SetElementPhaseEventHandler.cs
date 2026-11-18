using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCPCommandSet.Helpers;
using RevitMCPCommandSet.Models.Common;
using RevitMCPSDK.API.Interfaces;

namespace RevitMCPCommandSet.Services
{
    public class SetElementPhaseEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        private readonly ManualResetEvent _resetEvent = new ManualResetEvent(false);

        public List<SetElementPhaseRequest> Requests { get; set; }
        public AIResult<List<SetElementPhaseResult>> Result { get; private set; }

        public void SetParameters(List<SetElementPhaseRequest> requests)
        {
            Requests = requests;
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
                var results = new List<SetElementPhaseResult>();

                if (!ConfirmationHelper.Confirm("change phase for", Requests.Count))
                {
                    Result = new AIResult<List<SetElementPhaseResult>>
                    {
                        Success = false,
                        Message = "Operation cancelled by user",
                        Response = new List<SetElementPhaseResult>()
                    };
                    return;
                }

                using (var transaction = new Transaction(doc, "Set Element Phase"))
                {
                    transaction.Start();
                    try
                    {
                        foreach (var request in Requests)
                        {
                            var result = new SetElementPhaseResult
                            {
                                ElementId = request.ElementId
                            };

                            try
                            {
#if REVIT2024_OR_GREATER
                                var elementId = new ElementId(request.ElementId);
#else
                                var elementId = new ElementId((int)request.ElementId);
#endif
                                var element = doc.GetElement(elementId);
                                if (element == null)
                                {
                                    result.Success = false;
                                    result.Message = $"Element {request.ElementId} not found";
                                    results.Add(result);
                                    continue;
                                }

                                bool anySet = false;

                                // Set created phase
                                if (request.CreatedPhaseId.HasValue)
                                {
#if REVIT2024_OR_GREATER
                                    var createdPhaseElementId = new ElementId(request.CreatedPhaseId.Value);
#else
                                    var createdPhaseElementId = new ElementId((int)request.CreatedPhaseId.Value);
#endif
                                    if (!(doc.GetElement(createdPhaseElementId) is Phase))
                                    {
                                        result.Success = false;
                                        result.Message = $"CreatedPhaseId {request.CreatedPhaseId.Value} is not a valid Phase";
                                        results.Add(result);
                                        continue;
                                    }

                                    var createdParam = element.get_Parameter(BuiltInParameter.PHASE_CREATED);
                                    if (createdParam == null || createdParam.IsReadOnly)
                                    {
                                        result.Success = false;
                                        result.Message = "PHASE_CREATED parameter is not available or is read-only on this element";
                                        results.Add(result);
                                        continue;
                                    }

                                    createdParam.Set(createdPhaseElementId);
                                    anySet = true;
                                }

                                // Set demolished phase
                                if (request.DemolishedPhaseId.HasValue)
                                {
#if REVIT2024_OR_GREATER
                                    var demolishedPhaseElementId = new ElementId(request.DemolishedPhaseId.Value);
#else
                                    var demolishedPhaseElementId = new ElementId((int)request.DemolishedPhaseId.Value);
#endif
                                    if (!(doc.GetElement(demolishedPhaseElementId) is Phase))
                                    {
                                        result.Success = false;
                                        result.Message = $"DemolishedPhaseId {request.DemolishedPhaseId.Value} is not a valid Phase";
                                        results.Add(result);
                                        continue;
                                    }

                                    var demolishedParam = element.get_Parameter(BuiltInParameter.PHASE_DEMOLISHED);
                                    if (demolishedParam == null || demolishedParam.IsReadOnly)
                                    {
                                        result.Success = false;
                                        result.Message = "PHASE_DEMOLISHED parameter is not available or is read-only on this element";
                                        results.Add(result);
                                        continue;
                                    }

                                    demolishedParam.Set(demolishedPhaseElementId);
                                    anySet = true;
                                }

                                if (!anySet)
                                {
                                    result.Success = false;
                                    result.Message = "No phase was specified (provide createdPhaseId and/or demolishedPhaseId)";
                                }
                                else
                                {
                                    result.Success = true;
                                    result.Message = "Phase set successfully";
                                }
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
                    catch
                    {
                        if (transaction.GetStatus() == TransactionStatus.Started)
                            transaction.RollBack();
                        throw;
                    }
                }

                int successCount = results.Count(r => r.Success);
                Result = new AIResult<List<SetElementPhaseResult>>
                {
                    Success = successCount > 0,
                    Message = $"Set phase on {successCount}/{results.Count} elements successfully",
                    Response = results
                };
            }
            catch (Exception ex)
            {
                Result = new AIResult<List<SetElementPhaseResult>>
                {
                    Success = false,
                    Message = $"Failed to set element phase: {ex.Message}",
                    Response = new List<SetElementPhaseResult>()
                };
            }
            finally
            {
                _resetEvent.Set();
            }
        }

        public string GetName() => "Set Element Phase";
    }
}
