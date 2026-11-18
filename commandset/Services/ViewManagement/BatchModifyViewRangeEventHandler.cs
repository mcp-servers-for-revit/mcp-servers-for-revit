using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCPCommandSet.Models.Common;
using RevitMCPSDK.API.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace RevitMCPCommandSet.Services.ViewManagement
{
    public class BatchModifyViewRangeEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        public List<long> ViewIds { get; set; } = new List<long>();
        public double? TopOffsetMm { get; set; }
        public double? CutPlaneOffsetMm { get; set; }
        public double? BottomOffsetMm { get; set; }
        public double? ViewDepthOffsetMm { get; set; }

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
                int modified = 0;
                int errors = 0;
                var results = new List<object>();

                using (var transaction = new Transaction(doc, "Batch Modify View Range"))
                {
                    transaction.Start();
                    try
                    {

                    foreach (var viewId in ViewIds)
                    {
#if REVIT2024_OR_GREATER
                        var view = doc.GetElement(new ElementId(viewId)) as ViewPlan;
#else
                        var view = doc.GetElement(new ElementId((int)viewId)) as ViewPlan;
#endif
                        if (view == null)
                        {
                            errors++;
                            results.Add(new { viewId, success = false, message = "View not found or not a plan view" });
                            continue;
                        }

                        try
                        {
                            var viewRange = view.GetViewRange();
                            var level = view.GenLevel;
                            if (level == null)
                            {
                                errors++;
                                results.Add(new { viewId, success = false, message = "View has no associated level" });
                                continue;
                            }

                            if (TopOffsetMm.HasValue)
                                viewRange.SetOffset(PlanViewPlane.TopClipPlane, TopOffsetMm.Value / 304.8);
                            if (CutPlaneOffsetMm.HasValue)
                                viewRange.SetOffset(PlanViewPlane.CutPlane, CutPlaneOffsetMm.Value / 304.8);
                            if (BottomOffsetMm.HasValue)
                                viewRange.SetOffset(PlanViewPlane.BottomClipPlane, BottomOffsetMm.Value / 304.8);
                            if (ViewDepthOffsetMm.HasValue)
                                viewRange.SetOffset(PlanViewPlane.ViewDepthPlane, ViewDepthOffsetMm.Value / 304.8);

                            view.SetViewRange(viewRange);
                            modified++;

                            // Read back values
                            var updatedRange = view.GetViewRange();
                            results.Add(new
                            {
                                viewId,
                                success = true,
                                viewName = view.Name,
                                viewRange = new
                                {
                                    topOffsetMm = updatedRange.GetOffset(PlanViewPlane.TopClipPlane) * 304.8,
                                    cutPlaneOffsetMm = updatedRange.GetOffset(PlanViewPlane.CutPlane) * 304.8,
                                    bottomOffsetMm = updatedRange.GetOffset(PlanViewPlane.BottomClipPlane) * 304.8,
                                    viewDepthOffsetMm = updatedRange.GetOffset(PlanViewPlane.ViewDepthPlane) * 304.8
                                }
                            });
                        }
                        catch (Exception ex)
                        {
                            errors++;
                            results.Add(new { viewId, success = false, message = ex.Message });
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
                    Success = modified > 0,
                    Message = $"Modified view range on {modified}/{ViewIds.Count} views, {errors} errors",
                    Response = new { modified, errors, views = results }
                };
            }
            catch (Exception ex)
            {
                Result = new AIResult<object> { Success = false, Message = $"Batch modify view range failed: {ex.Message}" };
            }
            finally
            {
                TaskCompleted = true;
                _resetEvent.Set();
            }
        }

        public string GetName() => "Batch Modify View Range";
    }
}
