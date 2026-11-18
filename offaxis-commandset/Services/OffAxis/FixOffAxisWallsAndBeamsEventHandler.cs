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
    public class FixOffAxisWallsAndBeamsEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
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

        private static XYZ SnapP1ToNearestDirection(XYZ p0, XYZ p1, double length)
        {
            double ang = Math.Atan2(p1.Y - p0.Y, p1.X - p0.X) * OffAxisGeometryUtils.RadToDeg;
            double snapped = Math.Round(ang / 45.0) * 45.0;
            if (snapped <= -180) snapped = 180;
            double rad = snapped * OffAxisGeometryUtils.DegToRad;
            return new XYZ(p0.X + length * Math.Cos(rad), p0.Y + length * Math.Sin(rad), p1.Z);
        }

        private static Line SnapToAxis(Line line)
        {
            XYZ p0 = line.GetEndPoint(0);
            XYZ p1 = line.GetEndPoint(1);
            double len = p0.DistanceTo(p1);
            return Line.CreateBound(p0, SnapP1ToNearestDirection(p0, p1, len));
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
                double joinTolerance = 0.1;
                double minCurveLength = 0.001;

                // Safety: dimensions
                var dimConstrained = new HashSet<int>();
                foreach (var dim in new FilteredElementCollector(document).OfClass(typeof(Dimension)).WhereElementIsNotElementType().Cast<Dimension>())
                {
                    try
                    {
                        var refs = dim.References;
                        if (refs != null)
                        {
                            foreach (Reference r in refs)
                            {
                                if (r.ElementId != ElementId.InvalidElementId)
                                    dimConstrained.Add(r.ElementId.GetIntValue());
                            }
                        }
                    }
                    catch { }
                }

                // Safety: hosted inserts
                var hostedWallIds = new HashSet<int>(new FilteredElementCollector(document)
                    .OfClass(typeof(FamilyInstance)).WhereElementIsNotElementType().Cast<FamilyInstance>()
                    .Where(fi => fi.Host != null && (fi.Category?.Id.GetIntValue() == (int)BuiltInCategory.OST_Doors || fi.Category?.Id.GetIntValue() == (int)BuiltInCategory.OST_Windows))
                    .Select(fi => fi.Host.Id.GetIntValue()));

                // All walls & beams
                var allWalls = new FilteredElementCollector(document).OfClass(typeof(Wall)).WhereElementIsNotElementType().Cast<Wall>()
                    .Where(w => w.Location is LocationCurve lc && lc.Curve is Line).ToList();

                var allBeams = new FilteredElementCollector(document).OfClass(typeof(FamilyInstance)).WhereElementIsNotElementType().Cast<FamilyInstance>()
                    .Where(fi => fi.Category != null && fi.Category.Id.GetIntValue() == (int)BuiltInCategory.OST_StructuralFraming
                        && fi.Location is LocationCurve lc && lc.Curve is Line).ToList();

                // Wall endpoints for join detection
                var allEndpoints = new List<(int id, XYZ p0, XYZ p1)>();
                foreach (var w in allWalls)
                {
                    var ln = (Line)((LocationCurve)w.Location).Curve;
                    allEndpoints.Add((w.Id.GetIntValue(), ln.GetEndPoint(0), ln.GetEndPoint(1)));
                }

                bool SharesEndpoint(int wId, XYZ pt)
                {
                    foreach (var (id, p0, p1) in allEndpoints)
                    {
                        if (id == wId) continue;
                        if (pt.DistanceTo(p0) < joinTolerance || pt.DistanceTo(p1) < joinTolerance)
                            return true;
                    }
                    return false;
                }

                bool IsCandidate(double angleDeg, int elemId)
                {
                    double a = Math.Abs(angleDeg % 90.0);
                    if (a > 45.0) a = 90.0 - a;
                    if (a <= MinDeviationDeg) return false; // already on axis
                    if (a >= MaxDeviationDeg) return false; // excessive deviation
                    if (isTargetedMode) return TargetIds.Contains(elemId);
                    return true;
                }

                var fixLog = new List<object>();
                var skipLog = new List<object>();
                var failLog = new List<object>();
                var failureLog = new List<object>();

                var preprocessor = new SilentFailuresPreprocessor();

                int largeFixes = 0;
                // 1. Process Walls
                foreach (var wall in allWalls)
                {
                    int wid = wall.Id.GetIntValue();
                    var lc = (LocationCurve)wall.Location;
                    var origLine = (Line)lc.Curve;
                    double angle = OffAxisGeometryUtils.LineAngleDeg2D(origLine);

                    if (!IsCandidate(angle, wid))
                    {
                        if (isTargetedMode && TargetIds.Contains(wid))
                        {
                            double a = Math.Abs(angle % 90.0);
                            if (a > 45.0) a = 90.0 - a;
                            skipLog.Add(new { Id = wid, Category = "Wall", Reason = "Requested ID not in deviation band", DeviationDeg = Math.Round(a, 6) });
                        }
                        continue;
                    }

                    if (wall.Pinned)
                    {
                        skipLog.Add(new { Id = wid, Category = "Wall", Reason = "Wall is pinned" });
                        continue;
                    }
                    if (dimConstrained.Contains(wid))
                    {
                        skipLog.Add(new { Id = wid, Category = "Wall", Reason = "Referenced by dimension constraint" });
                        continue;
                    }
                    if (hostedWallIds.Contains(wid))
                    {
                        skipLog.Add(new { Id = wid, Category = "Wall", Reason = "Hosts door/window insert" });
                        continue;
                    }

                    XYZ origP0 = origLine.GetEndPoint(0);
                    XYZ origP1 = origLine.GetEndPoint(1);
                    double wallLen = origP0.DistanceTo(origP1);
                    if (wallLen < 0.5)
                    {
                        skipLog.Add(new { Id = wid, Category = "Wall", Reason = "Wall too short (< 0.5 ft)" });
                        continue;
                    }

                    if (SharesEndpoint(wid, origP1))
                    {
                        skipLog.Add(new { Id = wid, Category = "Wall", Reason = "Endpoint p1 is joined with another wall" });
                        continue;
                    }

                    Line newLine = SnapToAxis(origLine);
                    if (newLine.Length < minCurveLength)
                    {
                        skipLog.Add(new { Id = wid, Category = "Wall", Reason = "Resulting curve too short" });
                        continue;
                    }

                    double dev = OffAxisGeometryUtils.DeviationFromAxis(angle);
                    double moveIn = origP1.DistanceTo(newLine.GetEndPoint(1)) * 12.0;
                    bool isLarge = moveIn > OffAxisGeometryUtils.FlagMovementInches || dev > OffAxisGeometryUtils.FlagDeviationDegrees;
                    bool overCap = moveIn > MaxMoveInches;

                    if (overCap)
                    {
                        skipLog.Add(new { Id = wid, Category = "Wall", Reason = "Movement exceeds maxMoveInches cap", MovementIn = Math.Round(moveIn, 4), CapIn = MaxMoveInches });
                        continue;
                    }

                    if (PreviewOnly)
                    {
                        fixLog.Add(new
                        {
                            Id = wid,
                            Category = "Wall",
                            TypeName = wall.WallType?.Name ?? "?",
                            MovementIn = Math.Round(moveIn, 4),
                            DeviationDeg = Math.Round(dev, 4),
                            LargeFix = isLarge,
                            Preview = true
                        });
                        continue;
                    }

                    try
                    {
                        using (Transaction t = new Transaction(document, "Fix Off-Axis Wall " + wid))
                        {
                            t.Start();
                            var fo = t.GetFailureHandlingOptions();
                            fo.SetFailuresPreprocessor(preprocessor);
                            fo.SetClearAfterRollback(true);
                            fo.SetForcedModalHandling(false);
                            t.SetFailureHandlingOptions(fo);

                            lc.Curve = newLine;
                            var st = t.Commit();
                            if (st == TransactionStatus.Committed)
                            {
                                if (isLarge) largeFixes++;
                                fixLog.Add(new
                                {
                                    Id = wid,
                                    Category = "Wall",
                                    TypeName = wall.WallType?.Name ?? "?",
                                    MovementIn = Math.Round(moveIn, 4),
                                    DeviationDeg = Math.Round(dev, 4),
                                    LargeFix = isLarge
                                });
                            }
                            else
                            {
                                failLog.Add(new { Id = wid, Category = "Wall", Reason = "Transaction status: " + st });
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        failLog.Add(new { Id = wid, Category = "Wall", Reason = ex.Message });
                    }
                }

                // 2. Process Beams
                foreach (var beam in allBeams)
                {
                    int bid = beam.Id.GetIntValue();
                    var lc = (LocationCurve)beam.Location;
                    var origLine = (Line)lc.Curve;
                    double angle = OffAxisGeometryUtils.LineAngleDeg2D(origLine);

                    if (!IsCandidate(angle, bid))
                    {
                        if (isTargetedMode && TargetIds.Contains(bid))
                        {
                            double a = Math.Abs(angle % 90.0);
                            if (a > 45.0) a = 90.0 - a;
                            skipLog.Add(new { Id = bid, Category = "Beam", Reason = "Requested ID not in deviation band", DeviationDeg = Math.Round(a, 6) });
                        }
                        continue;
                    }

                    if (beam.Pinned)
                    {
                        skipLog.Add(new { Id = bid, Category = "Beam", Reason = "Beam is pinned" });
                        continue;
                    }
                    if (dimConstrained.Contains(bid))
                    {
                        skipLog.Add(new { Id = bid, Category = "Beam", Reason = "Referenced by dimension constraint" });
                        continue;
                    }

                    double beamLen = origLine.Length;
                    if (beamLen < 0.5)
                    {
                        skipLog.Add(new { Id = bid, Category = "Beam", Reason = "Beam too short (< 0.5 ft)" });
                        continue;
                    }

                    Line newLine = SnapToAxis(origLine);
                    if (newLine.Length < minCurveLength)
                    {
                        skipLog.Add(new { Id = bid, Category = "Beam", Reason = "Resulting curve too short" });
                        continue;
                    }

                    double dev = OffAxisGeometryUtils.DeviationFromAxis(angle);
                    double moveIn = origLine.GetEndPoint(1).DistanceTo(newLine.GetEndPoint(1)) * 12.0;
                    bool isLarge = moveIn > OffAxisGeometryUtils.FlagMovementInches || dev > OffAxisGeometryUtils.FlagDeviationDegrees;
                    bool overCap = moveIn > MaxMoveInches;

                    if (overCap)
                    {
                        skipLog.Add(new { Id = bid, Category = "Beam", Reason = "Movement exceeds maxMoveInches cap", MovementIn = Math.Round(moveIn, 4), CapIn = MaxMoveInches });
                        continue;
                    }

                    if (PreviewOnly)
                    {
                        fixLog.Add(new
                        {
                            Id = bid,
                            Category = "Beam",
                            TypeName = beam.Symbol?.Name ?? "?",
                            MovementIn = Math.Round(moveIn, 4),
                            DeviationDeg = Math.Round(dev, 4),
                            LargeFix = isLarge,
                            Preview = true
                        });
                        continue;
                    }

                    try
                    {
                        using (Transaction t = new Transaction(document, "Fix Off-Axis Beam " + bid))
                        {
                            t.Start();
                            var fo = t.GetFailureHandlingOptions();
                            fo.SetFailuresPreprocessor(preprocessor);
                            fo.SetClearAfterRollback(true);
                            fo.SetForcedModalHandling(false);
                            t.SetFailureHandlingOptions(fo);

                            lc.Curve = newLine;
                            var st = t.Commit();
                            if (st == TransactionStatus.Committed)
                            {
                                if (isLarge) largeFixes++;
                                fixLog.Add(new
                                {
                                    Id = bid,
                                    Category = "Beam",
                                    TypeName = beam.Symbol?.Name ?? "?",
                                    MovementIn = Math.Round(moveIn, 4),
                                    DeviationDeg = Math.Round(dev, 4),
                                    LargeFix = isLarge
                                });
                            }
                            else
                            {
                                failLog.Add(new { Id = bid, Category = "Beam", Reason = "Transaction status: " + st });
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        failLog.Add(new { Id = bid, Category = "Beam", Reason = ex.Message });
                    }
                }

                Result = new
                {
                    TotalFixed = fixLog.Count,
                    TotalSkipped = skipLog.Count,
                    TotalFailed = failLog.Count,
                    LargeFixes = largeFixes,
                    PreviewOnly = PreviewOnly,
                    MaxMoveInches = MaxMoveInches,
                    FixedElements = fixLog,
                    SkippedElements = skipLog.Count > 0 ? skipLog : null,
                    FailedElements = failLog.Count > 0 ? failLog : null,
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

        public string GetName() => "Fix Off-Axis Walls and Beams";
    }
}
