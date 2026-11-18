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
    public class DetectSpacingElementsEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        private readonly ManualResetEvent _resetEvent = new ManualResetEvent(false);

        public int Limit { get; set; } = 50;

        public object Result { get; private set; }

        public bool WaitForCompletion(int timeoutMilliseconds = 30000)
        {
            _resetEvent.Reset();
            return _resetEvent.WaitOne(timeoutMilliseconds);
        }

        private struct GeoData
        {
            public double P;
            public double Lo;
            public double Hi;
            public double Len;
            public double Ang;
            public XYZ E0;
            public XYZ E1;
        }

        private static GeoData ComputeGeo(Line ln)
        {
            var p0 = ln.GetEndPoint(0);
            var p1 = ln.GetEndPoint(1);
            double dx = p1.X - p0.X, dy = p1.Y - p0.Y;
            double len = Math.Sqrt(dx * dx + dy * dy);
            double ux = dx / len, uy = dy / len;
            double lo = Math.Min(p0.X * ux + p0.Y * uy, p1.X * ux + p1.Y * uy);
            double hi = Math.Max(p0.X * ux + p0.Y * uy, p1.X * ux + p1.Y * uy);
            double p = (p0.X + p1.X) / 2.0 * (-uy) + (p0.Y + p1.Y) / 2.0 * ux;
            double ang = Math.Atan2(dy, dx) * 180.0 / Math.PI;
            if (ang < 0) ang += 180.0;
            return new GeoData { P = p, Lo = lo, Hi = hi, Len = len, Ang = ang, E0 = p0, E1 = p1 };
        }

        private static double DirDiff(double a, double b)
        {
            double d = Math.Abs(a - b);
            if (d > 90.0) d = 180.0 - d;
            return d;
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

                double gridFt = 0.25 / 12.0;
                double dirTolDeg = 0.1;
                double maxMoveIn = 1.0;
                double flagMovementIn = 0.5;

                // Collect Grids
                var gridRecs = new List<(int id, double p, double ang)>();
                foreach (var g in new FilteredElementCollector(document).OfClass(typeof(Grid)).WhereElementIsNotElementType().Cast<Grid>())
                {
                    if (g.Curve is Line ln)
                    {
                        var geo = ComputeGeo(ln);
                        gridRecs.Add((g.Id.GetIntValue(), geo.P, geo.Ang));
                    }
                }

                var gBins = new List<(double ang, List<(int id, double p)> gs)>();
                foreach (var r in gridRecs)
                {
                    int binIdx = gBins.FindIndex(b => DirDiff(b.ang, r.ang) <= dirTolDeg);
                    if (binIdx < 0)
                    {
                        gBins.Add((r.ang, new List<(int id, double p)> { (r.id, r.p) }));
                    }
                    else
                    {
                        gBins[binIdx].gs.Add((r.id, r.p));
                    }
                }

                // Dimension constrained element IDs
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

                // Hosted doors/windows
                var hostedIds = new HashSet<int>(new FilteredElementCollector(document)
                    .OfClass(typeof(FamilyInstance)).WhereElementIsNotElementType().Cast<FamilyInstance>()
                    .Where(fi => fi.Host != null && (fi.Category?.Id.GetIntValue() == (int)BuiltInCategory.OST_Doors || fi.Category?.Id.GetIntValue() == (int)BuiltInCategory.OST_Windows))
                    .Select(fi => fi.Host.Id.GetIntValue()));

                var movable = new List<(int id, string cn, double p, double len, double ang, string typeName)>();

                // Walls
                foreach (var w in new FilteredElementCollector(document).OfClass(typeof(Wall)).WhereElementIsNotElementType().Cast<Wall>())
                {
                    if (!(w.Location is LocationCurve lc) || !(lc.Curve is Line ln) || w.Pinned) continue;
                    if (hostedIds.Contains(w.Id.GetIntValue()) || dimConstrained.Contains(w.Id.GetIntValue())) continue;
                    if (ln.Length < 0.5) continue;

                    // Exclude API joined
                    try
                    {
                        var joined = JoinGeometryUtils.GetJoinedElements(document, w);
                        if (joined != null && joined.Count > 0) continue;
                    }
                    catch { }

                    var geo = ComputeGeo(ln);
                    movable.Add((w.Id.GetIntValue(), "Wall", geo.P, geo.Len, geo.Ang, w.WallType?.Name ?? "?"));
                }

                // Beams
                foreach (var fi in new FilteredElementCollector(document).OfClass(typeof(FamilyInstance)).WhereElementIsNotElementType().Cast<FamilyInstance>())
                {
                    if (fi.Category?.Id.GetIntValue() != (int)BuiltInCategory.OST_StructuralFraming) continue;
                    if (!(fi.Location is LocationCurve lc) || !(lc.Curve is Line ln) || fi.Pinned) continue;
                    if (dimConstrained.Contains(fi.Id.GetIntValue()) || ln.Length < 0.5) continue;

                    var geo = ComputeGeo(ln);
                    movable.Add((fi.Id.GetIntValue(), "Beam", geo.P, geo.Len, geo.Ang, fi.Symbol?.Name ?? "?"));
                }

                var targets = new List<object>();
                foreach (var m in movable)
                {
                    // Find matching grid direction bin
                    var bin = gBins.FirstOrDefault(b => DirDiff(b.ang, m.ang) <= dirTolDeg);
                    if (bin.gs == null || bin.gs.Count == 0) continue;

                    // Nearest grid line
                    double nearestGp = bin.gs.OrderBy(g => Math.Abs(g.p - m.p)).First().p;
                    double signedDist = m.p - nearestGp;

                    double phi = signedDist - gridFt * Math.Floor(signedDist / gridFt);
                    double sn = phi + gridFt * Math.Round((signedDist - phi) / gridFt);
                    double deltaFt = signedDist - sn;
                    double deltaIn = deltaFt * 12.0;

                    if (Math.Abs(deltaIn) > 0.0012 && Math.Abs(deltaIn) <= maxMoveIn)
                    {
                        targets.Add(new
                        {
                            ElementId = m.id,
                            Category = m.cn,
                            TypeName = m.typeName,
                            LengthFt = Math.Round(m.len, 4),
                            AngleDeg = Math.Round(m.ang, 4),
                            OffsetIn = Math.Round(deltaIn, 4),
                            LargeFix = Math.Abs(deltaIn) > flagMovementIn
                        });
                    }
                }

                var limitedTargets = targets.Take(Limit).ToList();

                Result = new
                {
                    GridDirectionBins = gBins.Count,
                    TotalMovableAudited = movable.Count,
                    TotalOffLatticeFound = targets.Count,
                    ReturnedCount = limitedTargets.Count,
                    Targets = limitedTargets
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

        public string GetName() => "Detect Spacing Elements (Pass B)";
    }
}
