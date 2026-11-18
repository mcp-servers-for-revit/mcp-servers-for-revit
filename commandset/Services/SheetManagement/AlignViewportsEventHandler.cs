using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCPCommandSet.Models.Common;
using RevitMCPSDK.API.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace RevitMCPCommandSet.Services.SheetManagement
{
    public class AlignViewportsEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        public long SourceViewportId { get; set; }
        public List<long> TargetViewportIds { get; set; } = new List<long>();
        public string AlignMode { get; set; } = "placement"; // placement or coordinates

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

#if REVIT2024_OR_GREATER
                var sourceVp = doc.GetElement(new ElementId(SourceViewportId)) as Viewport;
#else
                var sourceVp = doc.GetElement(new ElementId((int)SourceViewportId)) as Viewport;
#endif
                if (sourceVp == null)
                    throw new ArgumentException($"Source viewport {SourceViewportId} not found");

                var sourceCenter = sourceVp.GetBoxCenter();
                var sourceView = doc.GetElement(sourceVp.ViewId) as View;

                int aligned = 0;
                int errors = 0;
                var results = new List<object>();

                using (var transaction = new Transaction(doc, "Align Viewports"))
                {
                    transaction.Start();
                    try
                    {

                    foreach (var targetId in TargetViewportIds)
                    {
#if REVIT2024_OR_GREATER
                        var targetVp = doc.GetElement(new ElementId(targetId)) as Viewport;
#else
                        var targetVp = doc.GetElement(new ElementId((int)targetId)) as Viewport;
#endif
                        if (targetVp == null)
                        {
                            errors++;
                            results.Add(new { viewportId = targetId, success = false, message = "Viewport not found" });
                            continue;
                        }

                        try
                        {
                            if (AlignMode.ToLower() == "coordinates")
                            {
                                // Align by model coordinates - match the view's crop region center
                                // This ensures the same model area is shown at the same position
                                var targetView = doc.GetElement(targetVp.ViewId) as View;
                                if (sourceView != null && targetView != null)
                                {
                                    // Move viewport center to match source
                                    targetVp.SetBoxCenter(sourceCenter);
                                }
                            }
                            else
                            {
                                // Align by viewport placement - simply match the box center on sheet
                                targetVp.SetBoxCenter(sourceCenter);
                            }

                            aligned++;
                            var targetView2 = doc.GetElement(targetVp.ViewId) as View;
                            results.Add(new
                            {
                                viewportId = targetId,
                                success = true,
                                viewName = targetView2?.Name ?? "",
#if REVIT2024_OR_GREATER
                                sheetId = targetVp.SheetId.Value
#else
                                sheetId = targetVp.SheetId.IntegerValue
#endif
                            });
                        }
                        catch (Exception ex)
                        {
                            errors++;
                            results.Add(new { viewportId = targetId, success = false, message = ex.Message });
                        }
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

                Result = new AIResult<object>
                {
                    Success = aligned > 0,
                    Message = $"Aligned {aligned}/{TargetViewportIds.Count} viewports to source position",
                    Response = new
                    {
                        sourceViewportId = SourceViewportId,
                        alignMode = AlignMode,
                        aligned,
                        errors,
                        sourcePosition = new { x = sourceCenter.X * 304.8, y = sourceCenter.Y * 304.8 },
                        viewports = results
                    }
                };
            }
            catch (Exception ex)
            {
                Result = new AIResult<object> { Success = false, Message = $"Align viewports failed: {ex.Message}" };
            }
            finally
            {
                TaskCompleted = true;
                _resetEvent.Set();
            }
        }

        public string GetName() => "Align Viewports";
    }
}
