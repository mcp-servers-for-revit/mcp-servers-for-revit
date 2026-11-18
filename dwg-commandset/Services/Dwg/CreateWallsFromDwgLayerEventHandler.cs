using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCPCommandSet.Services.Dwg;
using RevitMCPCommandSet.Utils.Dwg;
using RevitMCPSDK.API.Interfaces;

namespace RevitMCPCommandSet.Services.Dwg
{
    /// <summary>
    /// Mutating handler that generates Revit walls from paired face lines on
    /// one or more DWG layers (comma-separated). CAD drafters split wall faces
    /// across pen layers and draw them as fragmented collinear pieces, so the
    /// detection pipeline is:
    ///   1. straighten to 2D plan segments (near-horizontal lines only)
    ///   2. deduplicate identical segments (c>ad pens redraw the same line)
    ///   3. cluster by angle (mod 180, 2 deg tolerance, wrap-aware)
    ///   4. merge collinear fragments into continuous rails inside a cluster
    ///   5. pair rails: parallel, thickness in range, directional overlap
    /// Centerline spans the overlap as the mean of both rails. Short pairs
    /// are rejected as door jamb linework, and pairs spanning detected
    /// door-swing arcs are optionally rejected. Thickness maps to the nearest
    /// Revit wall type by compound width.
    /// </summary>
    public class CreateWallsFromDwgLayerEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        private readonly ManualResetEvent _resetEvent = new ManualResetEvent(false);

        public string DwgNameOrId { get; set; }
        public string Layer { get; set; }
        public double HeightFt { get; set; } = 10.0;
        public double MaxWallThicknessFt { get; set; } = 3.0;
        public double MinWallLengthFt { get; set; } = 3.5;
        public string WallTypeName { get; set; }
        public bool ExcludeDoorArcs { get; set; } = true;
        public long LevelId { get; set; } = -1;
        public int MaxWalls { get; set; } = 200;

        // Tunables (kept internal; revisit per-DWG if needed).
        private const double MinWallThicknessFt = 2.0 / 12.0; // 2"
        private const double MaxParallelDevDeg = 2.0;   // angle clustering
        private const double PairAngleTolDeg = 1.0;     // rail pairing
        private const double MinOverlapFrac = 0.7;
        private const double JambStubMaxFt = 2.0;       // partner rails shorter than this are jamb linework
        private const double RailTolFt = 0.05;          // collinear offset tolerance
        private const double MergeGapFt = 0.5;          // collinear fragment gap tolerance
        private const double PlanarZTolFrac = 0.05;     // dz/len limit for plan-view lines
        private const double MinSegFt = 0.05;           // drop sub-inch debris
        private const double DedupTolFt = 0.01;         // 2mm dedup rounding

        public object Result { get; private set; }

        public bool WaitForCompletion(int timeoutMilliseconds = 180000)
        {
            _resetEvent.Reset();
            return _resetEvent.WaitOne(timeoutMilliseconds);
        }

        private class WallPair
        {
            public XYZ CenterStart;
            public XYZ CenterEnd;
            public double Thickness;

            public double Length => CenterStart.DistanceTo(CenterEnd);
        }

        // A merged collinear continuous face in cluster frame + world centerline.
        private class Rail
        {
            public double AngleDeg; // cluster mean angle, degrees
            public double V;        // perpendicular offset in cluster frame
            public double U0, U1;   // extent along cluster direction
            public XYZ WorldStart;
            public XYZ WorldEnd;
            public double Length => U1 - U0;
        }

        public void Execute(UIApplication app)
        {
            try
            {
                var doc = app.ActiveUIDocument?.Document;
                if (doc == null)
                {
                    Result = new Dictionary<string, object> { ["error"] = "No active document" };
                    return;
                }

                if (string.IsNullOrWhiteSpace(DwgNameOrId) || string.IsNullOrWhiteSpace(Layer))
                {
                    Result = new Dictionary<string, object> { ["error"] = "dwgNameOrId and layer are required" };
                    return;
                }

                Element target = DwgCurveSource.ResolveDwg(doc, DwgNameOrId, out string targetName, out string targetKind);
                if (target == null)
                {
                    Result = new Dictionary<string, object> { ["error"] = $"No imported or linked DWG matching '{DwgNameOrId}'" };
                    return;
                }

                var layers = Layer.Split(',')
                    .Select(s => s.Trim())
                    .Where(s => s.Length > 0)
                    .ToList();

                // ---- 1. Collect: 2D plan segments over all requested layers ----
                var layerCurves = new List<Curve>();
                var layerNames = new List<string>();
                foreach (var name in layers)
                {
                    var cs = DwgCurveSource.CollectLayerCurves(doc, target, name);
                    if (cs.Count > 0) layerNames.Add(name);
                    layerCurves.AddRange(cs);
                }

                if (layerCurves.Count == 0)
                {
                    Result = new Dictionary<string, object>
                    {
                        ["error"] = $"No curves found on layer(s) '{Layer}' of '{targetName}'"
                    };
                    return;
                }

                int nonLine = 0, nonPlanar = 0, tiny = 0;
                var segs = new List<double[]>(); // x0,y0,x1,y1,len
                foreach (var cv in layerCurves)
                {
                    var ln = cv as Line;
                    if (ln == null) { nonLine++; continue; }
                    var p0 = ln.GetEndPoint(0);
                    var p1 = ln.GetEndPoint(1);
                    double dx = p1.X - p0.X, dy = p1.Y - p0.Y, dz = p1.Z - p0.Z;
                    double len = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                    if (len < 1e-9) continue;
                    if (Math.Abs(dz) > PlanarZTolFrac * len) { nonPlanar++; continue; }
                    if (Math.Sqrt(dx * dx + dy * dy) < MinSegFt) { tiny++; continue; }
                    segs.Add(new[] { p0.X, p0.Y, p1.X, p1.Y, Math.Sqrt(dx * dx + dy * dy) });
                }

                if (segs.Count == 0)
                {
                    Result = new Dictionary<string, object>
                    {
                        ["error"] = "No usable plan-view lines on the requested layer(s)",
                        ["nonLine"] = nonLine, ["nonPlanar"] = nonPlanar, ["tiny"] = tiny
                    };
                    return;
                }

                // ---- 2. Dedup identical segments across pens ----
                var seenKeys = new HashSet<string>();
                var segs2 = new List<double[]>();
                foreach (var s in segs)
                {
                    double xa = Math.Round(Math.Min(s[0], s[2]) / DedupTolFt) * DedupTolFt;
                    double ya = Math.Round(Math.Min(s[1], s[3]) / DedupTolFt) * DedupTolFt;
                    double xb = Math.Round(Math.Max(s[0], s[2]) / DedupTolFt) * DedupTolFt;
                    double yb = Math.Round(Math.Max(s[1], s[3]) / DedupTolFt) * DedupTolFt;
                    string key = xa.ToString("R") + "," + ya.ToString("R") + "|" + xb.ToString("R") + "," + yb.ToString("R");
                    if (seenKeys.Add(key)) segs2.Add(s);
                }
                int duplicatesRemoved = segs.Count - segs2.Count;
                segs = segs2;

                // ---- 3. Angle clustering (mod 180, wrap-aware) ----
                double angTol = MaxParallelDevDeg * Math.PI / 180.0;
                var items = new List<double[]>(); // ang, segIdx
                for (int i = 0; i < segs.Count; i++)
                {
                    double ang = Math.Atan2(segs[i][3] - segs[i][1], segs[i][2] - segs[i][0]);
                    if (ang < 0) ang += Math.PI;
                    if (ang >= Math.PI) ang -= Math.PI;
                    items.Add(new[] { ang, (double)i });
                }
                items.Sort((a, b) => a[0].CompareTo(b[0]));

                var clusters = new List<List<int>>();
                var curCluster = new List<int>();
                double curAng = -1;
                foreach (var it in items)
                {
                    double a = it[0];
                    if (curCluster.Count == 0 || a - curAng <= angTol)
                    {
                        curCluster.Add((int)it[1]);
                        if (curCluster.Count == 1) curAng = a;
                    }
                    else
                    {
                        clusters.Add(curCluster);
                        curCluster = new List<int> { (int)it[1] };
                        curAng = a;
                    }
                }
                if (curCluster.Count > 0) clusters.Add(curCluster);
                if (clusters.Count > 1)
                {
                    var first = clusters[0];
                    var last = clusters[clusters.Count - 1];
                    double aF = AngleOf(segs[first[0]]);
                    double aL = AngleOf(segs[last[0]]);
                    if ((aF + Math.PI) - aL <= angTol) { last.AddRange(first); clusters.RemoveAt(0); }
                }

                // ---- 4. Merge collinear fragments into rails per cluster ----
                var allRails = new List<Rail>();
                foreach (var cl in clusters)
                {
                    // Circular mean of segment angles (unit-vector average). A
                    // plain atan average is wrong for a horizontal family that
                    // straddles the 0/180 wrap: atan2 normalizes dy<0 lines to
                    // ~180deg and dy>0 lines to ~0deg, and averaging those raw
                    // values lands anywhere (observed: 5.714deg off a truly
                    // horizontal family). Vector averaging is wrap-safe.
                    double cs0 = 0, sn0 = 0;
                    foreach (int i in cl)
                    {
                        double a = AngleOf(segs[i]);
                        cs0 += Math.Cos(a);
                        sn0 += Math.Sin(a);
                    }
                    double am = Math.Atan2(sn0, cs0);
                    if (am < 0) am += Math.PI;
                    if (am >= Math.PI) am -= Math.PI;
                    double cu = Math.Cos(am), su = Math.Sin(am);

                    var frags = new List<double[]>(); // v0, u0, u1
                    foreach (int i in cl)
                    {
                        double u0 = segs[i][0] * cu + segs[i][1] * su;
                        double v0 = -segs[i][0] * su + segs[i][1] * cu;
                        double u1 = segs[i][2] * cu + segs[i][3] * su;
                        if (u1 < u0) { double t = u0; u0 = u1; u1 = t; }
                        frags.Add(new[] { v0, u0, u1 });
                    }
                    frags.Sort((a, b) => a[0].CompareTo(b[0]));

                    var rails = new List<double[]>(); // v, u0, u1
                    var curRail = new List<double[]>();
                    double railV = 0;
                    foreach (var f in frags)
                    {
                        if (curRail.Count == 0) { railV = f[0]; curRail.Add(f); }
                        else if (Math.Abs(f[0] - railV) <= RailTolFt) { curRail.Add(f); }
                        else { FlushRail(curRail, railV, rails); railV = f[0]; curRail.Add(f); }
                    }
                    FlushRail(curRail, railV, rails);

                    foreach (var r in rails)
                    {
                        double v = r[0], u0 = r[1], u1 = r[2];
                        allRails.Add(new Rail
                        {
                            AngleDeg = am * 180.0 / Math.PI,
                            V = v, U0 = u0, U1 = u1,
                            WorldStart = new XYZ(u0 * cu - v * su, u0 * su + v * cu, 0),
                            WorldEnd = new XYZ(u1 * cu - v * su, u1 * su + v * cu, 0)
                        });
                    }
                }
                // ---- 5. Pair rails ----
                var longIdx = new List<int>();
                for (int i = 0; i < allRails.Count; i++)
                    if (allRails[i].Length >= JambStubMaxFt) longIdx.Add(i);

                var candidates = new List<Cand>();
                foreach (int i in longIdx)
                {
                    if (allRails[i].Length < MinWallLengthFt) continue;
                    foreach (int j in longIdx)
                    {
                        if (j == i) continue;
                        double dAng = Math.Abs(allRails[i].AngleDeg - allRails[j].AngleDeg);
                        if (Math.Min(dAng, 180 - dAng) > PairAngleTolDeg) continue;

                        // Express rail j in rail i's cluster frame (angle-tolerant,
                        // orientation-corrected) so thickness/overlap are true values.
                        double ar = allRails[i].AngleDeg * Math.PI / 180.0;
                        double cu = Math.Cos(ar), su = Math.Sin(ar);
                        double uj0 = allRails[j].WorldStart.X * cu + allRails[j].WorldStart.Y * su;
                        double vj0 = -allRails[j].WorldStart.X * su + allRails[j].WorldStart.Y * cu;
                        double uj1 = allRails[j].WorldEnd.X * cu + allRails[j].WorldEnd.Y * su;
                        double vj1 = -allRails[j].WorldEnd.X * su + allRails[j].WorldEnd.Y * cu;
                        if (uj1 < uj0) { double t = uj0; uj0 = uj1; uj1 = t; }
                        double vj = (vj0 + vj1) * 0.5;

                        double dv = Math.Abs(allRails[i].V - vj);
                        if (dv < MinWallThicknessFt || dv > MaxWallThicknessFt) continue;

                        double lo = Math.Max(allRails[i].U0, uj0);
                        double hi = Math.Min(allRails[i].U1, uj1);
                        double ov = hi - lo;
                        if (ov <= 0) continue;
                        double minRail = Math.Min(allRails[i].Length, allRails[j].Length);
                        double frac = ov / minRail;
                        if (frac < MinOverlapFrac) continue;
                        if (allRails[j].Length < JambStubMaxFt) continue;

                        candidates.Add(new Cand { I = i, J = j, Thick = dv, OverlapFrac = frac, Vj = vj, Uj0 = uj0, Uj1 = uj1 });
                    }
                }

                // Greedy: most overlap first, then thinnest.
                var consumed = new HashSet<int>();
                var accepted = new List<WallPair>();
                foreach (var cand in candidates
                    .OrderByDescending(c => c.OverlapFrac)
                    .ThenBy(c => c.Thick))
                {
                    if (consumed.Contains(cand.I) || consumed.Contains(cand.J)) continue;
                    var ra = allRails[cand.I];
                    var rb = allRails[cand.J];
                    double lo = Math.Max(ra.U0, cand.Uj0);
                    double hi = Math.Min(ra.U1, cand.Uj1);
                    double vm = (ra.V + cand.Vj) * 0.5;
                    double cu = Math.Cos(ra.AngleDeg * Math.PI / 180.0), su = Math.Sin(ra.AngleDeg * Math.PI / 180.0);
                    accepted.Add(new WallPair
                    {
                        CenterStart = new XYZ(lo * cu - vm * su, lo * su + vm * cu, 0),
                        CenterEnd = new XYZ(hi * cu - vm * su, hi * su + vm * cu, 0),
                        Thickness = cand.Thick
                    });
                    consumed.Add(cand.I);
                    consumed.Add(cand.J);
                }
                int unpairedRails = longIdx.Count(id => !consumed.Contains(id) && allRails[id].Length >= MinWallLengthFt);

                // ---- Level ----
                double z = DwgCurveSource.MedianZ(layerCurves);
                Level level = null;
                if (LevelId > 0) level = doc.GetElement(new ElementId((int)LevelId)) as Level;
                if (level == null)
                    level = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>()
                        .OrderBy(l => Math.Abs(l.Elevation - z)).FirstOrDefault();
                if (level == null)
                {
                    Result = new Dictionary<string, object> { ["error"] = "No level found in document" };
                    return;
                }

                // ---- Wall types ----
                var wallTypes = new FilteredElementCollector(doc)
                    .OfClass(typeof(WallType)).WhereElementIsElementType()
                    .Cast<WallType>().ToList();
                if (wallTypes.Count == 0)
                {
                    Result = new Dictionary<string, object> { ["error"] = "No wall types in document" };
                    return;
                }
                WallType forcedType = null;
                if (!string.IsNullOrWhiteSpace(WallTypeName))
                {
                    var q = WallTypeName.Trim();
                    forcedType = wallTypes.FirstOrDefault(wt => string.Equals(wt.Name, q, StringComparison.OrdinalIgnoreCase))
                              ?? wallTypes.FirstOrDefault(wt => wt.Name.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0);
                    if (forcedType == null)
                    {
                        Result = new Dictionary<string, object> { ["error"] = $"No wall type matching '{WallTypeName}'" };
                        return;
                    }
                }

                // ---- Build ----
                List<XYZ> jambPoints = null;
                if (ExcludeDoorArcs)
                {
                    jambPoints = CollectDoorJambPoints(target, doc);
                }

                var preprocessor = new SilentWarningsPreprocessor();
                var createdIds = new List<long>();
                var typeSummary = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                int created = 0, rejectedJamb = 0, doorArcRejected = 0, buildFailed = 0;

                using (var trans = new Transaction(doc, "Create Walls from DWG Layer"))
                {
                    var fo = trans.GetFailureHandlingOptions();
                    trans.SetFailureHandlingOptions(fo.SetFailuresPreprocessor(preprocessor));
                    trans.Start();

                    foreach (var pair in accepted)
                    {
                        if (created >= MaxWalls) break;

                        // Jamb rejection: fake walls across door openings are short.
                        if (pair.Length < MinWallLengthFt)
                        {
                            rejectedJamb++;
                            continue;
                        }

                        // Door-arc rejection: short segment spanning between two jamb points.
                        if (ExcludeDoorArcs && jambPoints != null &&
                            pair.Length < MinWallLengthFt * 2.0 &&
                            NearJamb(pair.CenterStart, jambPoints) && NearJamb(pair.CenterEnd, jambPoints))
                        {
                            doorArcRejected++;
                            continue;
                        }

                        // Nearest wall type by thickness (forced type overrides).
                        var wt = forcedType;
                        if (wt == null)
                            wt = wallTypes.OrderBy(w => Math.Abs(w.Width - pair.Thickness)).First();

                        try
                        {
                            var centerLine = Line.CreateBound(pair.CenterStart, pair.CenterEnd);
                            var wall = Wall.Create(doc, centerLine, wt.Id, level.Id, HeightFt, 0, false, false);
                            if (wall != null)
                            {
                                createdIds.Add(DwgCurveSource.IdValue(wall));
                                created++;
                                string tn = wt.Name;
                                if (typeSummary.ContainsKey(tn)) typeSummary[tn]++;
                                else typeSummary[tn] = 1;
                            }
                            else buildFailed++;
                        }
                        catch
                        {
                            buildFailed++;
                        }
                    }

                    trans.Commit();
                }

                Result = new Dictionary<string, object>
                {
                    ["source"] = new Dictionary<string, object>
                    {
                        ["id"] = DwgCurveSource.IdValue(target),
                        ["name"] = targetName,
                        ["kind"] = targetKind
                    },
                    ["layers"] = layerNames,
                    ["level"] = new Dictionary<string, object> { ["id"] = DwgCurveSource.IdValue(level), ["name"] = level.Name },
                    ["heightFt"] = HeightFt,
                    ["totalCurves"] = layerCurves.Count,
                    ["planLines"] = segs.Count,
                    ["duplicatesRemoved"] = duplicatesRemoved,
                    ["nonLine"] = nonLine,
                    ["nonPlanar"] = nonPlanar,
                    ["tiny"] = tiny,
                    ["rails"] = allRails.Count,
                    ["candidates"] = accepted.Count,
                    ["wallsCreated"] = created,
                    ["unpairedLongRails"] = unpairedRails,
                    ["rejectedJamb"] = rejectedJamb,
                    ["doorArcRejected"] = doorArcRejected,
                    ["buildFailed"] = buildFailed,
                    ["maxWallThicknessFt"] = MaxWallThicknessFt,
                    ["minWallLengthFt"] = MinWallLengthFt,
                    ["typeSummary"] = typeSummary,
                    ["createdIds"] = createdIds,
                    ["suppressedMessages"] = preprocessor.Log
                };
            }
            catch (Exception ex)
            {
                Result = new Dictionary<string, object> { ["error"] = ex.Message, ["stack"] = ex.StackTrace };
            }
            finally
            {
                _resetEvent.Set();
            }
        }

        private static double AngleOf(double[] s)
        {
            double a = Math.Atan2(s[3] - s[1], s[2] - s[0]);
            if (a < 0) a += Math.PI;
            if (a >= Math.PI) a -= Math.PI;
            return a;
        }

        private static void FlushRail(List<double[]> curRail, double railV, List<double[]> rails)
        {
            if (curRail.Count == 0) return;
            curRail.Sort((a, b) => a[1].CompareTo(b[1]));
            double u0 = curRail[0][1], u1 = curRail[0][2];
            for (int k = 1; k < curRail.Count; k++)
            {
                double f0 = curRail[k][1], f1 = curRail[k][2];
                if (f0 <= u1 + MergeGapFt)
                {
                    if (f1 > u1) u1 = f1;
                }
                else
                {
                    rails.Add(new[] { railV, u0, u1 });
                    u0 = f0; u1 = f1;
                }
            }
            rails.Add(new[] { railV, u0, u1 });
            curRail.Clear();
        }

        private static List<XYZ> CollectDoorJambPoints(Element target, Document doc)
        {
            var pts = new List<XYZ>();
            var all = DwgCurveSource.CollectLayerCurves(doc, target, null);
            foreach (var cv in all)
            {
                if (!(cv is Arc a)) continue;
                double spanRad;
                try { spanRad = a.GetEndParameter(1) - a.GetEndParameter(0); } catch { continue; }
                double spanDeg = spanRad * 180.0 / Math.PI;
                if (spanDeg < 75 || spanDeg > 105) continue;
                if (a.Radius < 0.75 || a.Radius > 5.0) continue;
                try
                {
                    pts.Add(a.GetEndPoint(0));
                    pts.Add(a.GetEndPoint(1));
                }
                catch { }
            }
            return pts;
        }

        private static bool NearJamb(XYZ p, List<XYZ> jambPoints)
        {
            foreach (var jp in jambPoints)
            {
                if (jp.DistanceTo(p) <= JambSnapFt) return true;
            }
            return false;
        }

        private const double JambSnapFt = 0.35;

        // Pairing candidate between two rails.
        private class Cand
        {
            public int I, J;
            public double Thick;
            public double OverlapFrac;
            public double Vj;    // j's perpendicular offset in i's frame
            public double Uj0, Uj1; // j's extent along i's frame
        }

        public string GetName()
        {
            return "Create Walls from DWG Layer";
        }
    }
}

