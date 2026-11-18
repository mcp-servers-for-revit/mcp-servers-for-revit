using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCPCommandSet.Utils.OffAxis;
using RevitMCPSDK.API.Interfaces;

namespace RevitMCPCommandSet.Services.OffAxis
{
    public class FixOffAxisReferencePlanesEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        private readonly ManualResetEvent _resetEvent = new ManualResetEvent(false);

        public HashSet<int> TargetIds { get; set; } = new HashSet<int>();
        public double MinDeviationDeg { get; set; } = 0.0000001;
        public double MaxDeviationDeg { get; set; } = 0.1;
        public double MaxMoveInches { get; set; } = OffAxisGeometryUtils.DefaultMaxMoveInches;
        public bool PreviewOnly { get; set; } = false;

        public object Result { get; private set; }

        public bool WaitForCompletion(int timeoutMilliseconds = 60000)
        {
            _resetEvent.Reset();
            return _resetEvent.WaitOne(timeoutMilliseconds);
        }

        private static double GetPlaneAngleDeg(ReferencePlane rp)
        {
            Plane plane = rp.GetPlane();
            XYZ normal = plane.Normal;
            return Math.Atan2(Math.Abs(normal.Y), Math.Abs(normal.X)) * OffAxisGeometryUtils.RadToDeg;
        }

        private static XYZ GetSnappedNormal(ReferencePlane rp)
        {
            Plane plane = rp.GetPlane();
            XYZ normal = plane.Normal;
            double angle = Math.Atan2(normal.Y, normal.X);
            double nearest = Math.Round(angle / (Math.PI / 4.0)) * (Math.PI / 4.0);
            if (nearest <= -Math.PI) nearest = Math.PI;
            return new XYZ(Math.Cos(nearest), Math.Sin(nearest), 0.0);
        }

        public void Execute(UIApplication app)
        {
            try
            {
                var document = app.ActiveUIDocument?.Document;
                if (document == null)
                {
                    Result = new { Error = "No active document" };
                    return;
                }

                if (document.IsReadOnlyFile || document.IsReadOnly || document.IsLinked)
                {
                    Result = new { Error = "Document is read-only or linked" };
                    return;
                }

                bool isTargetedMode = TargetIds != null && TargetIds.Count > 0;

                // Collect dimension-constrained element IDs
                var dims = new FilteredElementCollector(document)
                    .OfClass(typeof(Dimension))
                    .Cast<Dimension>()
                    .ToList();

                var constrainedIds = new HashSet<int>();
                foreach (var dim in dims)
                {
                    try
                    {
                        var refs = dim.References;
                        if (refs != null)
                        {
                            foreach (Reference r in refs)
                            {
                                if (r.ElementId != ElementId.InvalidElementId)
                                    constrainedIds.Add(r.ElementId.GetIntValue());
                            }
                        }
                    }
                    catch { }
                }

                var allRefPlanes = new FilteredElementCollector(document)
                    .OfClass(typeof(ReferencePlane)).WhereElementIsNotElementType()
                    .Cast<ReferencePlane>()
                    .ToList();

                var offAxisRefPlanes = new List<ReferencePlane>();
                foreach (var rp in allRefPlanes)
                {
                    Plane plane = rp.GetPlane();
                    XYZ normal = plane.Normal;
                    if (Math.Abs(normal.Z) > 0.01) continue;

                    double angle = GetPlaneAngleDeg(rp);
                    double dev = OffAxisGeometryUtils.DeviationFromAxis(angle);
                    if (dev <= MinDeviationDeg || dev >= MaxDeviationDeg) continue;

                    if (isTargetedMode && !TargetIds.Contains(rp.Id.GetIntValue())) continue;
                    offAxisRefPlanes.Add(rp);
                }

                var rpFixLog = new List<object>();
                var preprocessor = new SilentFailuresPreprocessor();
                int rpsFixed = 0;
                int largeFixes = 0;

                foreach (var rp in offAxisRefPlanes)
                {
                    int id = rp.Id.GetIntValue();

                    if (rp.Pinned)
                    {
                        rpFixLog.Add(new { ElementId = id, Name = rp.Name, Status = "SKIP - pinned" });
                        continue;
                    }

                    if (constrainedIds.Contains(id))
                    {
                        rpFixLog.Add(new { ElementId = id, Name = rp.Name, Status = "SKIP - dimension constrained" });
                        continue;
                    }

                    double oldAngle = GetPlaneAngleDeg(rp);
                    XYZ newNormal = GetSnappedNormal(rp);
                    double newAngle = Math.Atan2(Math.Abs(newNormal.Y), Math.Abs(newNormal.X)) * OffAxisGeometryUtils.RadToDeg;

                    XYZ freeEnd = rp.FreeEnd;
                    XYZ bubbleEnd = rp.BubbleEnd;
                    double length = freeEnd.DistanceTo(bubbleEnd);
                    XYZ newFreeEnd = bubbleEnd + newNormal * length;
                    double movement = freeEnd.DistanceTo(newFreeEnd);
                    double movementIn = movement * 12.0;
                    double deviationDeg = OffAxisGeometryUtils.DeviationFromAxis(oldAngle);
                    bool isLarge = movementIn > OffAxisGeometryUtils.FlagMovementInches || deviationDeg > OffAxisGeometryUtils.FlagDeviationDegrees;
                    bool overCap = movementIn > MaxMoveInches;

                    if (overCap)
                    {
                        rpFixLog.Add(new { ElementId = id, Name = rp.Name, Status = "SKIP - movement exceeds maxMoveInches cap", MovementIn = Math.Round(movementIn, 4), CapIn = MaxMoveInches });
                        continue;
                    }

                    if (PreviewOnly)
                    {
                        rpFixLog.Add(new
                        {
                            ElementId = id,
                            Name = rp.Name,
                            Status = "PREVIEW",
                            OldAngle = Math.Round(oldAngle, 4),
                            NewAngle = Math.Round(newAngle, 4),
                            DeviationDeg = Math.Round(deviationDeg, 6),
                            MovementIn = Math.Round(movementIn, 4),
                            LargeFix = isLarge,
                            Preview = true
                        });
                        continue;
                    }

                    try
                    {
                        using (Transaction t = new Transaction(document, "Fix reference plane " + rp.Name))
                        {
                            t.Start();
                            var fo = t.GetFailureHandlingOptions();
                            fo.SetFailuresPreprocessor(preprocessor);
                            fo.SetClearAfterRollback(true);
                            fo.SetForcedModalHandling(false);
                            t.SetFailureHandlingOptions(fo);

                            rp.FreeEnd = newFreeEnd;

                            var commitRes = t.Commit();
                            if (commitRes == TransactionStatus.Committed)
                            {
                                rpsFixed++;
                                if (isLarge) largeFixes++;

                                rpFixLog.Add(new
                                {
                                    ElementId = id,
                                    Name = rp.Name,
                                    Status = "FIXED",
                                    OldAngle = Math.Round(oldAngle, 4),
                                    NewAngle = Math.Round(newAngle, 4),
                                    DeviationDeg = Math.Round(deviationDeg, 6),
                                    MovementIn = Math.Round(movementIn, 4),
                                    LargeFix = isLarge
                                });
                            }
                            else
                            {
                                rpFixLog.Add(new
                                {
                                    ElementId = id,
                                    Name = rp.Name,
                                    Status = "SKIP - conflict (rolled back safely)"
                                });
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        var msg = ex.InnerException?.Message ?? ex.Message;
                        rpFixLog.Add(new
                        {
                            ElementId = id,
                            Name = rp.Name,
                            Status = "FAIL: " + (msg.Length > 100 ? msg.Substring(0, 100) : msg)
                        });
                    }
                }

                Result = new
                {
                    TotalProcessed = offAxisRefPlanes.Count,
                    TotalFixed = rpsFixed,
                    LargeFixes = largeFixes,
                    Targeted = isTargetedMode,
                    PreviewOnly = PreviewOnly,
                    MaxMoveInches = MaxMoveInches,
                    Log = rpFixLog,
                    FailuresHandled = preprocessor.Log.Count > 0 ? preprocessor.Log : null
                };
            }
            catch (Exception ex)
            {
                Result = new { Error = ex.Message, StackTrace = ex.StackTrace };
            }
            finally
            {
                _resetEvent.Set();
            }
        }

        public string GetName() => "Fix Off-Axis Reference Planes";
    }
}
