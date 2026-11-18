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
    public class FixOffAxisGridsEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
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

                var allGrids = new FilteredElementCollector(document)
                    .OfClass(typeof(Grid)).WhereElementIsNotElementType()
                    .Cast<Grid>()
                    .ToList();

                var offAxisGrids = new List<Grid>();
                foreach (var g in allGrids)
                {
                    if (!(g.Curve is Line ln)) continue;
                    double ang = OffAxisGeometryUtils.LineAngleDeg2D(ln);
                    double dev = OffAxisGeometryUtils.DeviationFromAxis(ang);
                    if (dev <= MinDeviationDeg || dev >= MaxDeviationDeg) continue;
                    if (isTargetedMode && !TargetIds.Contains(g.Id.GetIntValue())) continue;
                    offAxisGrids.Add(g);
                }

                var gridFixLog = new List<object>();
                var preprocessor = new SilentFailuresPreprocessor();
                int gridsFixed = 0;
                int largeFixes = 0;

                foreach (var grid in offAxisGrids)
                {
                    int id = grid.Id.GetIntValue();

                    if (grid.Pinned)
                    {
                        gridFixLog.Add(new { ElementId = id, Name = grid.Name, Status = "SKIP - pinned" });
                        continue;
                    }

                    var line = grid.Curve as Line;
                    if (line == null) continue;

                    XYZ p0 = line.GetEndPoint(0);
                    XYZ p1 = line.GetEndPoint(1);
                    XYZ mid = (p0 + p1) * 0.5;

                    double dx = p1.X - p0.X;
                    double dy = p1.Y - p0.Y;
                    double actualAngleRad = Math.Atan2(dy, dx);
                    double actualAngleDeg = actualAngleRad * OffAxisGeometryUtils.RadToDeg;

                    double nearestAxisDeg = Math.Round(actualAngleDeg / 45.0) * 45.0;
                    if (nearestAxisDeg <= -180) nearestAxisDeg = 180;
                    double deltaDeg = nearestAxisDeg - actualAngleDeg;
                    double deltaRad = deltaDeg * OffAxisGeometryUtils.DegToRad;

                    double len = p0.DistanceTo(p1);
                    double movement = len * Math.Abs(Math.Sin(deltaRad / 2.0));
                    double movementIn = movement * 12.0;
                    double deviationDeg = OffAxisGeometryUtils.DeviationFromAxis(actualAngleDeg);
                    bool isLarge = movementIn > OffAxisGeometryUtils.FlagMovementInches || deviationDeg > OffAxisGeometryUtils.FlagDeviationDegrees;
                    bool overCap = movementIn > MaxMoveInches;

                    if (overCap)
                    {
                        gridFixLog.Add(new { ElementId = id, Name = grid.Name, Status = "SKIP - movement exceeds maxMoveInches cap", MovementIn = Math.Round(movementIn, 4), CapIn = MaxMoveInches });
                        continue;
                    }

                    if (PreviewOnly)
                    {
                        gridFixLog.Add(new
                        {
                            ElementId = id,
                            Name = grid.Name,
                            Status = "PREVIEW",
                            OriginalAngle = Math.Round(actualAngleDeg, 4),
                            DeltaAngle = Math.Round(deltaDeg, 4),
                            NewAngle = Math.Round(nearestAxisDeg, 4),
                            DeviationDeg = Math.Round(deviationDeg, 6),
                            MovementIn = Math.Round(movementIn, 4),
                            LargeFix = isLarge,
                            Preview = true
                        });
                        continue;
                    }

                    Line axis = Line.CreateBound(mid, new XYZ(mid.X, mid.Y, mid.Z + 10.0));

                    try
                    {
                        using (Transaction t = new Transaction(document, "Rotate grid " + grid.Name))
                        {
                            t.Start();
                            var fo = t.GetFailureHandlingOptions();
                            fo.SetFailuresPreprocessor(preprocessor);
                            fo.SetClearAfterRollback(true);
                            fo.SetForcedModalHandling(false);
                            t.SetFailureHandlingOptions(fo);

                            ElementTransformUtils.RotateElement(document, grid.Id, axis, deltaRad);

                            var commitRes = t.Commit();
                            if (commitRes == TransactionStatus.Committed)
                            {
                                gridsFixed++;
                                if (isLarge) largeFixes++;

                                gridFixLog.Add(new
                                {
                                    ElementId = id,
                                    Name = grid.Name,
                                    Status = "FIXED",
                                    OriginalAngle = Math.Round(actualAngleDeg, 4),
                                    DeltaAngle = Math.Round(deltaDeg, 4),
                                    NewAngle = Math.Round(nearestAxisDeg, 4),
                                    DeviationDeg = Math.Round(deviationDeg, 6),
                                    MovementIn = Math.Round(movementIn, 4),
                                    LargeFix = isLarge
                                });
                            }
                            else
                            {
                                gridFixLog.Add(new { ElementId = id, Name = grid.Name, Status = "FAIL - " + commitRes });
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        gridFixLog.Add(new { ElementId = id, Name = grid.Name, Status = "FAIL - " + ex.Message });
                    }
                }

                Result = new
                {
                    TotalProcessed = offAxisGrids.Count,
                    TotalFixed = gridsFixed,
                    LargeFixes = largeFixes,
                    Targeted = isTargetedMode,
                    PreviewOnly = PreviewOnly,
                    MaxMoveInches = MaxMoveInches,
                    Log = gridFixLog,
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

        public string GetName() => "Fix Off-Axis Grids";
    }
}
