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
    public class FixOffAxisModelLinesEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        private readonly ManualResetEvent _resetEvent = new ManualResetEvent(false);

        public HashSet<int> TargetLines { get; set; } = new HashSet<int>();
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

        private struct ModelLineFixItem
        {
            public ModelCurve MC;
            public Line Snapped;
            public XYZ OrigP1;
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

                bool isTargetedMode = TargetLines != null && TargetLines.Count > 0;
                double tol = 0.001;

                bool IsEligible(ModelCurve m)
                {
                    if (m == null || m.Category == null) return false;
                    string cn = m.Category.Name ?? "";
                    return !cn.Contains("Sketch");
                }

                var allCurves = new List<ModelCurve>();
                foreach (var ce in new FilteredElementCollector(document).OfClass(typeof(CurveElement)).Cast<Element>())
                {
                    if (ce is ModelCurve m && IsEligible(m)) allCurves.Add(m);
                }

                var constrainedIds = new HashSet<int>();
                try
                {
                    foreach (var dim in new FilteredElementCollector(document).OfClass(typeof(Dimension)).Cast<Dimension>())
                    {
                        var refs = dim.References;
                        if (refs != null)
                        {
                            for (int i = 0; i < refs.Size; i++)
                            {
                                var el2 = document.GetElement(refs.get_Item(i));
                                if (el2 != null) constrainedIds.Add(el2.Id.GetIntValue());
                            }
                        }
                    }
                }
                catch { }

                var requested = isTargetedMode
                    ? TargetLines.Select(id => document.GetElement(new ElementId(id)) as ModelCurve).Where(m => m != null && IsEligible(m)).ToList()
                    : allCurves.Where(m => m.GeometryCurve is Line).ToList();

                XYZ XY(XYZ p) => new XYZ(p.X, p.Y, 0);

                XYZ Intersect2(XYZ qa, XYZ da, XYZ qb, XYZ db)
                {
                    double det = da.X * db.Y - da.Y * db.X;
                    if (Math.Abs(det) < 1e-9) return null;
                    double t = ((qb.X - qa.X) * db.Y - (qb.Y - qa.Y) * db.X) / det;
                    return new XYZ(qa.X + t * da.X, qa.Y + t * da.Y, 0);
                }

                List<ModelLineFixItem> SolveChain(List<ModelCurve> chain, HashSet<int> flagged)
                {
                    int n = chain.Count;
                    if (n == 0) return null;
                    var eChain = new XYZ[n][];
                    for (int j = 0; j < n; j++)
                    {
                        if (!(chain[j].GeometryCurve is Line ln)) return null;
                        XYZ a = ln.GetEndPoint(0), b = ln.GetEndPoint(1);
                        if (n == 1) { eChain[j] = new[] { a, b }; continue; }
                        bool aPrev = false, bPrev = false, aNext = false, bNext = false;
                        if (j > 0)
                        {
                            if (!(chain[j - 1].GeometryCurve is Line pp)) return null;
                            XYZ c = pp.GetEndPoint(0), d = pp.GetEndPoint(1);
                            aPrev = a.DistanceTo(c) < tol || a.DistanceTo(d) < tol;
                            bPrev = b.DistanceTo(c) < tol || b.DistanceTo(d) < tol;
                            if (aPrev && bPrev) return null;
                        }
                        if (j < n - 1)
                        {
                            if (!(chain[j + 1].GeometryCurve is Line nn)) return null;
                            XYZ c = nn.GetEndPoint(0), d = nn.GetEndPoint(1);
                            aNext = a.DistanceTo(c) < tol || a.DistanceTo(d) < tol;
                            bNext = b.DistanceTo(c) < tol || b.DistanceTo(d) < tol;
                            if (aNext && bNext) return null;
                        }
                        XYZ lo, hi;
                        if (j == 0) lo = aNext ? b : a;
                        else lo = aPrev ? a : b;
                        if (j == n - 1) hi = bNext ? a : b;
                        else hi = aNext ? a : b;
                        eChain[j] = new[] { lo, hi };
                    }

                    var Q = new XYZ[n];
                    var D = new XYZ[n];
                    for (int j = 0; j < n; j++)
                    {
                        XYZ lo = eChain[j][0], hi = eChain[j][1];
                        XYZ d = hi - lo;
                        double len = d.GetLength();
                        if (len < 1e-9) return null;
                        D[j] = new XYZ(d.X / len, d.Y / len, 0);
                        Q[j] = new XYZ((lo.X + hi.X) / 2.0, (lo.Y + hi.Y) / 2.0, 0);
                        if (flagged.Contains(chain[j].Id.GetIntValue()))
                        {
                            XYZ axis = OffAxisGeometryUtils.ClosestWorldCandidate(d / len);
                            if (Math.Abs(axis.Z) > 0.5) return null;
                            double sx = axis.X, sy = axis.Y;
                            if (D[j].X * sx + D[j].Y * sy < 0) { sx = -sx; sy = -sy; }
                            XYZ dd = new XYZ(sx, sy, 0);
                            if (dd.GetLength() < 1e-9) return null;
                            D[j] = dd / dd.GetLength();
                            Q[j] = new XYZ((lo.X + hi.X) / 2.0, (lo.Y + hi.Y) / 2.0, 0);
                        }
                    }

                    var verts = new XYZ[n + 1];
                    verts[0] = XY(eChain[0][0]);
                    verts[n] = XY(eChain[n - 1][1]);
                    for (int j = 1; j < n; j++)
                    {
                        XYZ v = Intersect2(Q[j - 1], D[j - 1], Q[j], D[j]);
                        if (v == null) return null;
                        verts[j] = v;
                    }
                    var fixes = new List<ModelLineFixItem>();
                    for (int j = 0; j < n; j++)
                    {
                        XYZ lo = eChain[j][0], hi = eChain[j][1];
                        XYZ n0 = new XYZ(verts[j].X, verts[j].Y, lo.Z);
                        XYZ n1 = new XYZ(verts[j + 1].X, verts[j + 1].Y, hi.Z);
                        Line snapped = Line.CreateBound(n0, n1);
                        if (lo.DistanceTo(n0) > 1e-6 || hi.DistanceTo(n1) > 1e-6)
                            fixes.Add(new ModelLineFixItem { MC = chain[j], Snapped = snapped, OrigP1 = hi });
                    }
                    return fixes;
                }

                Line SolveStandalone(ModelCurve m)
                {
                    if (!(m.GeometryCurve is Line ln)) return null;
                    XYZ p0 = ln.GetEndPoint(0), p1 = ln.GetEndPoint(1);
                    XYZ d = p1 - p0;
                    double len = d.GetLength();
                    if (len < 1e-9) return null;
                    XYZ u = d / len;
                    XYZ axis = OffAxisGeometryUtils.ClosestWorldCandidate(u);
                    if (Math.Abs(axis.Z) > 0.5) return null;
                    double sx = axis.X, sy = axis.Y;
                    if (u.X * sx + u.Y * sy < 0) { sx = -sx; sy = -sy; }
                    XYZ dd = new XYZ(sx, sy, 0);
                    if (dd.GetLength() < 1e-9) return null;
                    XYZ du = dd / dd.GetLength() * (len / 2.0);
                    XYZ mid = new XYZ((p0.X + p1.X) / 2.0, (p0.Y + p1.Y) / 2.0, p0.Z);
                    return Line.CreateBound(mid - du, mid + du);
                }

                List<ModelCurve> BuildChain(ModelCurve start)
                {
                    var chain = new List<ModelCurve> { start };
                    var inChain = new HashSet<int> { start.Id.GetIntValue() };
                    bool grown;
                    do
                    {
                        grown = false;
                        var first = chain[0];
                        var last = chain[chain.Count - 1];
                        var f0 = (first.GeometryCurve as Line).GetEndPoint(0);
                        var frontCand = new List<ModelCurve>();
                        foreach (var c in allCurves)
                        {
                            if (inChain.Contains(c.Id.GetIntValue())) continue;
                            if (c.Pinned || constrainedIds.Contains(c.Id.GetIntValue())) continue;
                            if (!(c.GeometryCurve is Line ln)) continue;
                            if (ln.GetEndPoint(0).DistanceTo(f0) < tol || ln.GetEndPoint(1).DistanceTo(f0) < tol)
                                frontCand.Add(c);
                        }
                        if (frontCand.Count == 1)
                        {
                            chain.Insert(0, frontCand[0]);
                            inChain.Add(frontCand[0].Id.GetIntValue());
                            grown = true;
                        }
                        var l1 = (last.GeometryCurve as Line).GetEndPoint(1);
                        var backCand = new List<ModelCurve>();
                        foreach (var c in allCurves)
                        {
                            if (inChain.Contains(c.Id.GetIntValue())) continue;
                            if (c.Pinned || constrainedIds.Contains(c.Id.GetIntValue())) continue;
                            if (!(c.GeometryCurve is Line ln)) continue;
                            if (ln.GetEndPoint(0).DistanceTo(l1) < tol || ln.GetEndPoint(1).DistanceTo(l1) < tol)
                                backCand.Add(c);
                        }
                        if (backCand.Count == 1)
                        {
                            chain.Add(backCand[0]);
                            inChain.Add(backCand[0].Id.GetIntValue());
                            grown = true;
                        }
                    } while (grown && chain.Count < 2000);
                    return chain;
                }

                var fixLog = new List<object>();
                var preprocessor = new SilentFailuresPreprocessor();
                int totalFixed = 0, totalSkipped = 0, totalFailed = 0;
                int largeFixes = 0;

                foreach (var mc in requested)
                {
                    int id = mc.Id.GetIntValue();
                    Line gl = mc.GeometryCurve as Line;
                    if (gl == null)
                    {
                        totalSkipped++;
                        fixLog.Add(new { ElementId = id, Category = mc.Category?.Name ?? "?", Status = "SKIP - non-Line curve" });
                        continue;
                    }
                    if (mc.Pinned)
                    {
                        totalSkipped++;
                        fixLog.Add(new { ElementId = id, Category = mc.Category?.Name ?? "?", Status = "SKIP - pinned" });
                        continue;
                    }
                    if (constrainedIds.Contains(id))
                    {
                        totalSkipped++;
                        fixLog.Add(new { ElementId = id, Category = mc.Category?.Name ?? "?", Status = "SKIP - dimension constrained" });
                        continue;
                    }
                    XYZ u = gl.GetEndPoint(1) - gl.GetEndPoint(0);
                    double len = u.GetLength();
                    if (len > 1e-12) u = u / len;
                    double dev = OffAxisGeometryUtils.WorldDev(u);
                    if (dev < MinDeviationDeg || dev > MaxDeviationDeg)
                    {
                        totalSkipped++;
                        fixLog.Add(new { ElementId = id, Category = mc.Category?.Name ?? "?", Status = "SKIP - on axis", DeviationDeg = Math.Round(dev, 6) });
                        continue;
                    }

                    var flagged = new HashSet<int> { id };
                    var chain = BuildChain(mc);
                    bool chained = chain.Count >= 2;

                    List<ModelLineFixItem> fixes = chained ? SolveChain(chain, flagged) : null;
                    if (fixes == null || fixes.Count == 0)
                    {
                        Line st = SolveStandalone(mc);
                        if (st == null)
                        {
                            totalSkipped++;
                            fixLog.Add(new { ElementId = id, Category = mc.Category?.Name ?? "?", Status = "SKIP - unsolvable" });
                            continue;
                        }
                        fixes = new List<ModelLineFixItem> { new ModelLineFixItem { MC = mc, Snapped = st, OrigP1 = gl.GetEndPoint(1) } };
                    }

                    double maxMove = 0;
                    foreach (var fix in fixes)
                    {
                        double d = fix.OrigP1.DistanceTo(fix.Snapped.GetEndPoint(1)) * 12.0;
                        if (d > maxMove) maxMove = d;
                    }
                    bool isLarge = maxMove > OffAxisGeometryUtils.FlagMovementInches;
                    bool overCap = maxMove > MaxMoveInches;

                    if (overCap)
                    {
                        totalSkipped++;
                        fixLog.Add(new { ElementId = id, Category = mc.Category?.Name ?? "?", Status = "SKIP - movement exceeds maxMoveInches cap", Neighbors = chain.Count - 1, MovementIn = Math.Round(maxMove, 4), CapIn = MaxMoveInches, LargeFix = isLarge });
                        continue;
                    }

                    if (PreviewOnly)
                    {
                        fixLog.Add(new
                        {
                            ElementId = id,
                            Category = mc.Category?.Name ?? "?",
                            Status = "PREVIEW",
                            Neighbors = chain.Count - 1,
                            LinesFixed = fixes.Count,
                            MovementIn = Math.Round(maxMove, 4),
                            LargeFix = isLarge,
                            Preview = true
                        });
                        continue;
                    }

                    var errors = new List<string>();
                    int ok = 0, fail = 0;
                    TransactionStatus stt = TransactionStatus.Uninitialized;

                    try
                    {
                        using (Transaction t = new Transaction(document, "ModelLineFix " + id))
                        {
                            t.Start();
                            var fo = t.GetFailureHandlingOptions();
                            fo.SetFailuresPreprocessor(preprocessor);
                            fo.SetClearAfterRollback(true);
                            fo.SetForcedModalHandling(false);
                            t.SetFailureHandlingOptions(fo);

                            foreach (var fix in fixes)
                            {
                                try
                                {
                                    ((LocationCurve)fix.MC.Location).Curve = fix.Snapped;
                                    ok++;
                                }
                                catch (Exception exx)
                                {
                                    fail++;
                                    errors.Add(exx.InnerException?.Message ?? exx.Message);
                                }
                            }
                            stt = t.Commit();
                            if (stt != TransactionStatus.Committed) { ok = 0; fail = fixes.Count; }
                        }
                    }
                    catch (Exception exx)
                    {
                        errors.Add(exx.InnerException?.Message ?? exx.Message);
                        ok = 0;
                        fail = fixes.Count;
                    }

                    string status = stt == TransactionStatus.Committed && fail == 0 ? "FIXED" :
                        stt == TransactionStatus.RolledBack ? "SKIP - constraints (rolled back)" :
                        stt == TransactionStatus.Committed && ok > 0 ? "PARTIAL" : "FAIL";

                    if (isLarge) largeFixes++;

                    if (status == "FIXED") totalFixed++;
                    else if (status.StartsWith("SKIP")) totalSkipped++;
                    else totalFailed++;

                    fixLog.Add(new
                    {
                        ElementId = id,
                        Category = mc.Category?.Name ?? "?",
                        Status = status,
                        Neighbors = chain.Count - 1,
                        LinesFixed = ok,
                        LinesFailed = fail,
                        MovementIn = Math.Round(maxMove, 4),
                        LargeFix = isLarge,
                        Errors = errors.Count > 0 ? errors : null
                    });
                }

                Result = new
                {
                    TotalFixed = totalFixed,
                    TotalSkipped = totalSkipped,
                    TotalFailed = totalFailed,
                    LargeFixes = largeFixes,
                    Targeted = isTargetedMode,
                    PreviewOnly = PreviewOnly,
                    MaxMoveInches = MaxMoveInches,
                    Log = fixLog,
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

        public string GetName() => "Fix Off-Axis Model Lines";
    }
}
