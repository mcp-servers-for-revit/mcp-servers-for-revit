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
    public class DetectOffAxisLinesEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        private readonly ManualResetEvent _resetEvent = new ManualResetEvent(false);

        public double MinDeviationDeg { get; set; } = 0.001;
        public double MaxDeviationDeg { get; set; } = 0.1;

        public object Result { get; private set; }

        public bool WaitForCompletion(int timeoutMilliseconds = 30000)
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

                bool IsOffAxis(double angleDeg)
                {
                    double a = Math.Abs(angleDeg % 90.0);
                    if (a > 45.0) a = 90.0 - a;
                    return a > MinDeviationDeg && a < MaxDeviationDeg;
                }

                // Walls
                var walls = new FilteredElementCollector(document)
                    .OfClass(typeof(Wall)).WhereElementIsNotElementType().Cast<Wall>()
                    .Where(w => w.Location is LocationCurve lc && lc.Curve is Line)
                    .ToList();

                var wallResults = new List<object>();
                foreach (var w in walls)
                {
                    var line = (Line)((LocationCurve)w.Location).Curve;
                    double angle = OffAxisGeometryUtils.LineAngleDeg2D(line);
                    if (IsOffAxis(angle))
                    {
                        wallResults.Add(new
                        {
                            Id = w.Id.GetIntValue(),
                            TypeName = w.WallType?.Name ?? "?",
                            P0 = FormatPt(line.GetEndPoint(0)),
                            P1 = FormatPt(line.GetEndPoint(1)),
                            AngleDeg = Math.Round(angle, 4),
                            DeviationDeg = Math.Round(OffAxisGeometryUtils.DeviationFromAxis(angle), 4)
                        });
                    }
                }

                // Beams
                var beams = new FilteredElementCollector(document)
                    .OfClass(typeof(FamilyInstance)).WhereElementIsNotElementType().Cast<FamilyInstance>()
                    .Where(fi => fi.Category != null && fi.Category.Id.GetIntValue() == (int)BuiltInCategory.OST_StructuralFraming
                        && fi.Location is LocationCurve lc && lc.Curve is Line)
                    .ToList();

                var beamResults = new List<object>();
                foreach (var b in beams)
                {
                    var line = (Line)((LocationCurve)b.Location).Curve;
                    double angle = OffAxisGeometryUtils.LineAngleDeg2D(line);
                    if (IsOffAxis(angle))
                    {
                        beamResults.Add(new
                        {
                            Id = b.Id.GetIntValue(),
                            TypeName = b.Symbol?.Name ?? "?",
                            P0 = FormatPt(line.GetEndPoint(0)),
                            P1 = FormatPt(line.GetEndPoint(1)),
                            AngleDeg = Math.Round(angle, 4),
                            DeviationDeg = Math.Round(OffAxisGeometryUtils.DeviationFromAxis(angle), 4)
                        });
                    }
                }

                // Grids
                var grids = new FilteredElementCollector(document)
                    .OfClass(typeof(Grid)).WhereElementIsNotElementType().Cast<Grid>()
                    .Where(g => g.Curve is Line)
                    .ToList();

                var gridResults = new List<object>();
                foreach (var g in grids)
                {
                    var line = (Line)g.Curve;
                    double angle = OffAxisGeometryUtils.LineAngleDeg2D(line);
                    if (IsOffAxis(angle))
                    {
                        gridResults.Add(new
                        {
                            Id = g.Id.GetIntValue(),
                            TypeName = g.Name,
                            P0 = FormatPt(line.GetEndPoint(0)),
                            P1 = FormatPt(line.GetEndPoint(1)),
                            AngleDeg = Math.Round(angle, 4),
                            DeviationDeg = Math.Round(OffAxisGeometryUtils.DeviationFromAxis(angle), 4)
                        });
                    }
                }

                // Floors
                var floorResults = new List<object>();
                var floors = new FilteredElementCollector(document).OfClass(typeof(Floor)).WhereElementIsNotElementType().Cast<Floor>().ToList();
                foreach (var fl in floors)
                {
                    ElementId sketchId = fl.SketchId;
                    if (sketchId == ElementId.InvalidElementId) continue;
                    var sketch = document.GetElement(sketchId) as Sketch;
                    if (sketch == null) continue;

                    var offLines = new List<object>();
                    CurveArrArray profile = sketch.Profile;
                    for (int i = 0; i < profile.Size; i++)
                    {
                        CurveArray loop = profile.get_Item(i);
                        for (int j = 0; j < loop.Size; j++)
                        {
                            if (loop.get_Item(j) is Line line)
                            {
                                double angle = OffAxisGeometryUtils.LineAngleDeg2D(line);
                                if (IsOffAxis(angle))
                                {
                                    offLines.Add(new
                                    {
                                        P0 = FormatPt(line.GetEndPoint(0)),
                                        P1 = FormatPt(line.GetEndPoint(1)),
                                        AngleDeg = Math.Round(angle, 4),
                                        DeviationDeg = Math.Round(OffAxisGeometryUtils.DeviationFromAxis(angle), 4)
                                    });
                                }
                            }
                        }
                    }
                    if (offLines.Count > 0)
                    {
                        floorResults.Add(new
                        {
                            Id = fl.Id.GetIntValue(),
                            TypeName = fl.FloorType?.Name ?? "?",
                            OffAxisLines = offLines
                        });
                    }
                }

                // Ceilings
                var ceilingResults = new List<object>();
                var ceilings = new FilteredElementCollector(document).OfClass(typeof(Ceiling)).WhereElementIsNotElementType().Cast<Ceiling>().ToList();
                foreach (var ce in ceilings)
                {
                    ElementId sketchId = ce.SketchId;
                    if (sketchId == ElementId.InvalidElementId) continue;
                    var sketch = document.GetElement(sketchId) as Sketch;
                    if (sketch == null) continue;

                    var offLines = new List<object>();
                    CurveArrArray profile = sketch.Profile;
                    for (int i = 0; i < profile.Size; i++)
                    {
                        CurveArray loop = profile.get_Item(i);
                        for (int j = 0; j < loop.Size; j++)
                        {
                            if (loop.get_Item(j) is Line line)
                            {
                                double angle = OffAxisGeometryUtils.LineAngleDeg2D(line);
                                if (IsOffAxis(angle))
                                {
                                    offLines.Add(new
                                    {
                                        P0 = FormatPt(line.GetEndPoint(0)),
                                        P1 = FormatPt(line.GetEndPoint(1)),
                                        AngleDeg = Math.Round(angle, 4),
                                        DeviationDeg = Math.Round(OffAxisGeometryUtils.DeviationFromAxis(angle), 4)
                                    });
                                }
                            }
                        }
                    }
                    if (offLines.Count > 0)
                    {
                        ceilingResults.Add(new
                        {
                            Id = ce.Id.GetIntValue(),
                            TypeName = ce.Name ?? "?",
                            OffAxisLines = offLines
                        });
                    }
                }

                // Roofs
                var roofResults = new List<object>();
                var roofs = new FilteredElementCollector(document).OfClass(typeof(FootPrintRoof)).WhereElementIsNotElementType().Cast<FootPrintRoof>().ToList();
                foreach (var rf in roofs)
                {
                    var offLines = new List<object>();
                    try
                    {
                        ModelCurveArrArray profiles = rf.GetProfiles();
                        for (int i = 0; i < profiles.Size; i++)
                        {
                            ModelCurveArray loop = profiles.get_Item(i);
                            for (int j = 0; j < loop.Size; j++)
                            {
                                ModelCurve mc = loop.get_Item(j);
                                if (mc != null && mc.GeometryCurve is Line line)
                                {
                                    double angle = OffAxisGeometryUtils.LineAngleDeg2D(line);
                                    if (IsOffAxis(angle))
                                    {
                                        offLines.Add(new
                                        {
                                            Id = mc.Id.GetIntValue(),
                                            P0 = FormatPt(line.GetEndPoint(0)),
                                            P1 = FormatPt(line.GetEndPoint(1)),
                                            AngleDeg = Math.Round(angle, 4),
                                            DeviationDeg = Math.Round(OffAxisGeometryUtils.DeviationFromAxis(angle), 4)
                                        });
                                    }
                                }
                            }
                        }
                    }
                    catch { }

                    if (offLines.Count > 0)
                    {
                        roofResults.Add(new
                        {
                            Id = rf.Id.GetIntValue(),
                            TypeName = rf.RoofType?.Name ?? "?",
                            OffAxisLines = offLines
                        });
                    }
                }

                Result = new
                {
                    Tolerance = $"{MinDeviationDeg} to {MaxDeviationDeg} degrees",
                    Walls = wallResults,
                    Beams = beamResults,
                    Grids = gridResults,
                    Floors = floorResults,
                    Ceilings = ceilingResults,
                    Roofs = roofResults,
                    Summary = new
                    {
                        OffAxisWalls = wallResults.Count,
                        OffAxisBeams = beamResults.Count,
                        OffAxisGrids = gridResults.Count,
                        OffAxisFloors = floorResults.Count,
                        OffAxisCeilings = ceilingResults.Count,
                        OffAxisRoofs = roofResults.Count,
                        Total = wallResults.Count + beamResults.Count + gridResults.Count + floorResults.Count + ceilingResults.Count + roofResults.Count
                    }
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

        public string GetName() => "Detect Off-Axis Lines (Full Scan)";

        private static string FormatPt(XYZ p) => $"({p.X:F6},{p.Y:F6},{p.Z:F6})";
    }
}
