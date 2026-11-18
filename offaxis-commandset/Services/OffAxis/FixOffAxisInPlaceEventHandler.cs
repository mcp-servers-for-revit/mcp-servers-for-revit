using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCPCommandSet.Utils.OffAxis;
using RevitMCPSDK.API.Interfaces;

namespace RevitMCPCommandSet.Services.OffAxis
{
    public class FixOffAxisInPlaceEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        private readonly ManualResetEvent _resetEvent = new ManualResetEvent(false);

        public HashSet<int> TargetHosts { get; set; } = new HashSet<int>();
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

        private struct InPlaceFixItem
        {
            public ModelCurve MC;
            public Line Snapped;
            public XYZ OrigP1;
        }

        private static XYZ PlaneProp(Plane pl, string name)
        {
            try
            {
                var prop = pl.GetType().GetProperties().FirstOrDefault(p => p.Name == name);
                return prop != null ? prop.GetValue(pl) as XYZ : null;
            }
            catch { return null; }
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

                bool isTargetedMode = TargetHosts != null && TargetHosts.Count > 0;
                double tol = 0.001;

                var excludedCategories = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Mass", "Toposolid" };
                var advisoryFormTypes = new HashSet<string>(StringComparer.Ordinal) { "Blend", "Sweep", "Revolve", "GenericForm", "SweptBlend" };

                var dimConstrained = new HashSet<int>();
                try
                {
                    foreach (var dim in new FilteredElementCollector(document).OfClass(typeof(Dimension)).Cast<Dimension>())
                    {
                        var refs = dim.References;
                        if (refs != null)
                        {
                            for (int i = 0; i < refs.Size; i++)
                            {
                                var el = document.GetElement(refs.get_Item(i));
                                if (el != null) dimConstrained.Add(el.Id.GetIntValue());
                            }
                        }
                    }
                }
                catch { }

                // Warning-driven enumeration
                var warnings = document.GetWarnings()?.ToList() ?? new List<FailureMessage>();
                var formFlaggedLines = new Dictionary<int, HashSet<int>>();
                var formMeta = new Dictionary<int, (int hostId, string category, string family)>();
                var advisoryTargets = new List<object>();

                foreach (var w in warnings)
                {
                    string desc = "";
                    try { desc = w.GetDescriptionText() ?? ""; } catch { }
                    if (!desc.ToLower().Contains("off axis")) continue;

                    var failing = w.GetFailingElements();
                    if (failing == null || failing.Count == 0) continue;

                    FamilyInstance inPlaceInst = null;
                    int? formId = null;
                    var warnLines = new List<int>();

                    foreach (var fid in failing)
                    {
                        var e = document.GetElement(fid);
                        if (e == null) continue;
                        if (inPlaceInst == null && e is FamilyInstance fi && (fi.Symbol?.Family?.IsInPlace ?? false))
                            inPlaceInst = fi;
                        else if (!formId.HasValue && (e.GetType().Name == "Extrusion" || advisoryFormTypes.Contains(e.GetType().Name)))
                            formId = e.Id.GetIntValue();
                        else if (e is ModelCurve mc)
                        {
                            string cn = mc.Category?.Name ?? "";
                            if (cn.Contains("Sketch") || mc.Category?.Id.GetIntValue() == -2000045)
                                warnLines.Add(mc.Id.GetIntValue());
                        }
                    }

                    if (inPlaceInst == null || !formId.HasValue) continue;

                    int hostId = inPlaceInst.Id.GetIntValue();
                    string hostCat = inPlaceInst.Category?.Name ?? "";
                    string hostFam = inPlaceInst.Symbol?.Family?.Name ?? "";

                    if (isTargetedMode)
                    {
                        bool hostMatch = TargetHosts.Contains(hostId);
                        bool formMatch = TargetHosts.Contains(formId.Value);
                        if (!hostMatch && !formMatch) continue;
                        if (TargetLines.Count > 0) warnLines = warnLines.Where(l => TargetLines.Contains(l)).ToList();
                    }

                    if (excludedCategories.Contains(hostCat))
                    {
                        advisoryTargets.Add(new { FormId = formId.Value, Family = hostFam, Category = hostCat, Status = "excluded category (advisory)" });
                        continue;
                    }

                    Element fEx = document.GetElement(new ElementId(formId.Value));
                    if (fEx != null && fEx.GetType().Name != "Extrusion")
                    {
                        advisoryTargets.Add(new { FormId = formId.Value, Family = hostFam, Category = hostCat, Status = "non-Extrusion primitive (fix manually)" });
                        continue;
                    }
                    if (fEx != null && fEx.Pinned)
                    {
                        advisoryTargets.Add(new { FormId = formId.Value, Family = hostFam, Category = hostCat, Status = "pinned (advisory)" });
                        continue;
                    }

                    if (!formMeta.ContainsKey(formId.Value)) formMeta[formId.Value] = (hostId, hostCat, hostFam);
                    if (!formFlaggedLines.ContainsKey(formId.Value)) formFlaggedLines[formId.Value] = new HashSet<int>();
                    foreach (var l in warnLines) formFlaggedLines[formId.Value].Add(l);
                }

                // Solvers
                List<InPlaceFixItem> SolveRotate(Extrusion exn, HashSet<int> flagged)
                {
                    var fixes = new List<InPlaceFixItem>();
                    var sk = exn.Sketch;
                    if (sk == null) return fixes;
                    var mcs = new List<ModelCurve>();
                    foreach (var cid in sk.GetAllElements())
                    {
                        if (document.GetElement(cid) is ModelCurve m) mcs.Add(m);
                    }
                    if (mcs.Count == 0) return fixes;

                    Plane pl = null;
                    foreach (var m in mcs)
                    {
                        if (m is ModelLine ml && ml.SketchPlane != null) { pl = ml.SketchPlane.GetPlane(); break; }
                    }
                    if (pl == null) return fixes;

                    XYZ pn = PlaneProp(pl, "Normal") ?? XYZ.BasisZ;
                    XYZ pvx = PlaneProp(pl, "XVec") ?? PlaneProp(pl, "BasisX") ?? XYZ.BasisX;
                    XYZ pvy = PlaneProp(pl, "YVec") ?? PlaneProp(pl, "BasisY") ?? XYZ.BasisY;
                    XYZ org = PlaneProp(pl, "Origin") ?? XYZ.Zero;

                    XYZ ToL(XYZ p) { var d = p - org; return new XYZ(d.DotProduct(pvx), d.DotProduct(pvy), d.DotProduct(pn)); }
                    XYZ ToW(XYZ l) => org + pvx * l.X + pvy * l.Y + pn * l.Z;

                    foreach (var m in mcs)
                    {
                        int lineId = m.Id.GetIntValue();
                        if (!(m.GeometryCurve is Line gl)) continue;
                        XYZ wP0 = gl.GetEndPoint(0), wP1 = gl.GetEndPoint(1);
                        XYZ wd = wP1 - wP0;
                        double wlen = wd.GetLength();
                        if (wlen < 1e-9) continue;
                        XYZ wu = wd / wlen;

                        double bd = OffAxisGeometryUtils.WorldDev(wu);
                        XYZ ba = OffAxisGeometryUtils.ClosestWorldCandidate(wu);

                        bool warned = flagged.Contains(lineId);
                        bool inBand = bd >= MinDeviationDeg && bd <= MaxDeviationDeg;
                        if (!(warned || inBand)) continue;

                        XYZ p0 = ToL(wP0), p1 = ToL(wP1);
                        XYZ mid = (p0 + p1) * 0.5;
                        XYZ dLoc = ToL(wP0 + ba * 0.1) - p0;
                        double dl = dLoc.GetLength();
                        XYZ dNew = dl > 1e-9 ? dLoc / dl : new XYZ(1, 0, 0);
                        XYZ n0 = mid - dNew * (wlen / 2.0);
                        XYZ n1 = mid + dNew * (wlen / 2.0);
                        Line snapped = Line.CreateBound(ToW(n0), ToW(n1));

                        if (wP0.DistanceTo(snapped.GetEndPoint(0)) > 1e-6 || wP1.DistanceTo(snapped.GetEndPoint(1)) > 1e-6)
                        {
                            fixes.Add(new InPlaceFixItem { MC = m, Snapped = snapped, OrigP1 = wP1 });
                        }
                    }
                    return fixes;
                }

                List<InPlaceFixItem> SolveForm(Extrusion ex, HashSet<int> flaggedLines)
                {
                    var fixes = new List<InPlaceFixItem>();
                    var sk = ex.Sketch;
                    if (sk == null) return fixes;
                    var mcs = new List<ModelCurve>();
                    foreach (var cid in sk.GetAllElements())
                    {
                        if (document.GetElement(cid) is ModelCurve m) mcs.Add(m);
                    }
                    if (mcs.Count == 0) return fixes;

                    Plane pl = null;
                    foreach (var m in mcs)
                    {
                        if (m is ModelLine ml && ml.SketchPlane != null) { pl = ml.SketchPlane.GetPlane(); break; }
                    }
                    if (pl == null) return fixes;

                    XYZ pn = PlaneProp(pl, "Normal") ?? XYZ.BasisZ;
                    XYZ pvx = PlaneProp(pl, "XVec") ?? PlaneProp(pl, "BasisX") ?? XYZ.BasisX;
                    XYZ pvy = PlaneProp(pl, "YVec") ?? PlaneProp(pl, "BasisY") ?? XYZ.BasisY;
                    XYZ org = PlaneProp(pl, "Origin") ?? XYZ.Zero;

                    XYZ ToLocal(XYZ p) { var d = p - org; return new XYZ(d.DotProduct(pvx), d.DotProduct(pvy), d.DotProduct(pn)); }
                    XYZ ToWorld(XYZ l) => org + pvx * l.X + pvy * l.Y + pn * l.Z;

                    var profile = sk.Profile;
                    for (int li = 0; li < profile.Size; li++)
                    {
                        var arr = profile.get_Item(li);
                        int n = arr.Size;
                        if (n < 2) continue;
                        var lines = new List<Line>();
                        var mcRef = new List<ModelCurve>();
                        bool nonLine = false;

                        for (int j = 0; j < n; j++)
                        {
                            var c = arr.get_Item(j);
                            if (!(c is Line ln)) { nonLine = true; break; }
                            lines.Add(ln);
                            ModelCurve match = null;
                            foreach (var m in mcs)
                            {
                                if (m.GeometryCurve is Line gl)
                                {
                                    if ((gl.GetEndPoint(0).DistanceTo(ln.GetEndPoint(0)) < tol && gl.GetEndPoint(1).DistanceTo(ln.GetEndPoint(1)) < tol) ||
                                        (gl.GetEndPoint(0).DistanceTo(ln.GetEndPoint(1)) < tol && gl.GetEndPoint(1).DistanceTo(ln.GetEndPoint(0)) < tol))
                                    { match = m; break; }
                                }
                            }
                            mcRef.Add(match);
                        }
                        if (nonLine) continue;

                        var Q = new XYZ[n];
                        var D = new XYZ[n];
                        bool anyOffAxis = false;

                        for (int j = 0; j < n; j++)
                        {
                            XYZ wP0 = lines[j].GetEndPoint(0);
                            XYZ wP1 = lines[j].GetEndPoint(1);
                            XYZ wd = wP1 - wP0;
                            double wlen = wd.GetLength();
                            XYZ wu = wlen > 1e-12 ? wd / wlen : XYZ.BasisX;

                            double bestDev = OffAxisGeometryUtils.WorldDev(wu);
                            XYZ bestAxis = OffAxisGeometryUtils.ClosestWorldCandidate(wu);

                            bool warned = mcRef[j] != null && flaggedLines.Contains(mcRef[j].Id.GetIntValue());
                            bool inBand = bestDev >= MinDeviationDeg && bestDev <= MaxDeviationDeg;
                            bool shouldSnap = isTargetedMode ? warned : (warned || inBand);

                            if (shouldSnap)
                            {
                                anyOffAxis = true;
                                XYZ p0 = ToLocal(wP0), p1 = ToLocal(wP1);
                                XYZ mid = (p0 + p1) * 0.5;
                                XYZ dLoc = ToLocal(wP0 + bestAxis * 0.1) - p0;
                                double dl = dLoc.GetLength();
                                D[j] = dl > 1e-9 ? dLoc / dl : new XYZ(1, 0, 0);
                                Q[j] = new XYZ(mid.X, mid.Y, p0.Z);
                            }
                            else
                            {
                                XYZ p0 = ToLocal(wP0), p1 = ToLocal(wP1);
                                XYZ d = new XYZ(p1.X - p0.X, p1.Y - p0.Y, 0);
                                double dl = d.GetLength();
                                D[j] = dl > 1e-12 ? d / dl : new XYZ(1, 0, 0);
                                Q[j] = p0;
                            }
                        }
                        if (!anyOffAxis) continue;

                        var verts = new List<XYZ>();
                        for (int j = 0; j < n; j++)
                        {
                            int prev = (j - 1 + n) % n;
                            XYZ pA = Q[prev], dA = D[prev];
                            XYZ pB = Q[j], dB = D[j];
                            double det = dA.X * dB.Y - dA.Y * dB.X;
                            XYZ v;
                            if (Math.Abs(det) > 1e-7)
                            {
                                double t = ((pB.X - pA.X) * dB.Y - (pB.Y - pA.Y) * dB.X) / det;
                                v = new XYZ(pA.X + t * dA.X, pA.Y + t * dA.Y, pA.Z);
                            }
                            else v = (ToLocal(lines[prev].GetEndPoint(1)) + ToLocal(lines[j].GetEndPoint(0))) * 0.5;
                            verts.Add(ToWorld(v));
                        }

                        for (int j = 0; j < n; j++)
                        {
                            Line snapped = Line.CreateBound(verts[j], verts[(j + 1) % n]);
                            XYZ oP0 = lines[j].GetEndPoint(0), oP1 = lines[j].GetEndPoint(1);
                            if ((oP0.DistanceTo(snapped.GetEndPoint(0)) > 1e-6 || oP1.DistanceTo(snapped.GetEndPoint(1)) > 1e-6) && mcRef[j] != null)
                            {
                                fixes.Add(new InPlaceFixItem { MC = mcRef[j], Snapped = snapped, OrigP1 = oP1 });
                            }
                        }
                    }
                    return fixes;
                }

                var preprocessor = new SilentFailuresPreprocessor();

                object FixFormById(int formId, string cat, string fam, HashSet<int> flagged)
                {
                    var ex = document.GetElement(new ElementId(formId)) as Extrusion;
                    if (ex == null) return new { FormId = formId, Status = "SKIP - not an Extrusion", LinesFixed = 0, LinesFailed = 0, Failed = false, LargeFix = false };
                    if (ex.Pinned) return new { FormId = formId, Status = "SKIP - pinned", LinesFixed = 0, LinesFailed = 0, Failed = false, LargeFix = false };
                    if (dimConstrained.Contains(formId)) return new { FormId = formId, Status = "SKIP - dimension constrained", LinesFixed = 0, LinesFailed = 0, Failed = false, LargeFix = false };

                    var rotateFixes = SolveRotate(ex, flagged);
                    var fixes = rotateFixes.Count > 0 ? rotateFixes : SolveForm(ex, flagged);
                    bool usedCorners = rotateFixes.Count == 0;
                    if (fixes.Count == 0) return new { FormId = formId, Status = "SKIP - on axis", LinesFixed = 0, LinesFailed = 0, Failed = false, LargeFix = false };

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
                        return new { FormId = formId, Category = cat, Family = fam, Status = "SKIP - movement exceeds maxMoveInches cap", Strategy = usedCorners ? "corner-vertex ray-ray closure" : "midpoint rotation", LinesFixed = 0, LinesFailed = fixes.Count, MovementIn = Math.Round(maxMove, 4), LargeFix = isLarge, Failed = true };
                    }

                    if (PreviewOnly)
                    {
                        return new { FormId = formId, Category = cat, Family = fam, Status = "PREVIEW", Strategy = usedCorners ? "corner-vertex ray-ray closure" : "midpoint rotation", LinesFixed = fixes.Count, LinesFailed = 0, MovementIn = Math.Round(maxMove, 4), LargeFix = isLarge, Failed = false };
                    }

                    var errors = new List<string>();
                    int ok = 0, fail = 0;
                    TransactionStatus st = TransactionStatus.Uninitialized;

                    try
                    {
                        using (Transaction t = new Transaction(document, "InPlaceFix " + formId))
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
                            st = t.Commit();
                            if (st != TransactionStatus.Committed) { ok = 0; fail = fixes.Count; }
                        }
                    }
                    catch (Exception exx)
                    {
                        errors.Add(exx.InnerException?.Message ?? exx.Message);
                        ok = 0;
                        fail = fixes.Count;
                    }

                    // Retry with corner solver if rotation rolled back
                    if (st == TransactionStatus.RolledBack && !usedCorners)
                    {
                        var cornerFixes = SolveForm(ex, flagged);
                        if (cornerFixes.Count > 0)
                        {
                            try
                            {
                                using (Transaction t = new Transaction(document, "InPlaceFixCorners " + formId))
                                {
                                    t.Start();
                                    var fo = t.GetFailureHandlingOptions();
                                    fo.SetFailuresPreprocessor(preprocessor);
                                    fo.SetClearAfterRollback(true);
                                    fo.SetForcedModalHandling(false);
                                    t.SetFailureHandlingOptions(fo);

                                    int cok = 0, cfail = 0;
                                    foreach (var fix in cornerFixes)
                                    {
                                        try
                                        {
                                            ((LocationCurve)fix.MC.Location).Curve = fix.Snapped;
                                            cok++;
                                        }
                                        catch (Exception exx)
                                        {
                                            cfail++;
                                            errors.Add("Corner: " + (exx.InnerException?.Message ?? exx.Message));
                                        }
                                    }
                                    var cst = t.Commit();
                                    if (cst == TransactionStatus.Committed)
                                    {
                                        st = cst;
                                        ok = cok;
                                        fail = cfail;
                                        fixes = cornerFixes;
                                        usedCorners = true;
                                    }
                                }
                            }
                            catch { }
                        }
                    }

                    maxMove = 0;
                    foreach (var fix in fixes)
                    {
                        double d = fix.OrigP1.DistanceTo(fix.Snapped.GetEndPoint(1)) * 12.0;
                        if (d > maxMove) maxMove = d;
                    }

                    string status = st == TransactionStatus.Committed && fail == 0 ? "FIXED" :
                        st == TransactionStatus.RolledBack ? "SKIP - constraints (rolled back)" :
                        st == TransactionStatus.Committed && ok > 0 ? "PARTIAL" : "FAIL";

                    isLarge = maxMove > OffAxisGeometryUtils.FlagMovementInches;

                    return new
                    {
                        FormId = formId,
                        Category = cat,
                        Family = fam,
                        Status = status,
                        Strategy = usedCorners ? "corner-vertex ray-ray closure" : "midpoint rotation",
                        LinesFixed = ok,
                        LinesFailed = fail,
                        MovementIn = Math.Round(maxMove, 4),
                        LargeFix = isLarge,
                        Failed = ok == 0 && status != "SKIP - on axis",
                        Errors = errors.Count > 0 ? errors : null
                    };
                }

                var fixLog = new List<object>();
                int totalFixed = 0, totalSkipped = 0, totalFailed = 0;
                int largeFixes = 0;

                foreach (var kv in formFlaggedLines)
                {
                    var meta = formMeta[kv.Key];
                    var r = FixFormById(kv.Key, meta.category, meta.family, kv.Value);
                    fixLog.Add(r);

                    // Extract status and LargeFix safely
                    var propStatus = r.GetType().GetProperty("Status")?.GetValue(r)?.ToString() ?? "";
                    var propLarge = (bool)(r.GetType().GetProperty("LargeFix")?.GetValue(r) ?? false);
                    if (propLarge) largeFixes++;

                    if (propStatus == "FIXED" || propStatus == "PARTIAL") totalFixed++;
                    else if (propStatus.StartsWith("SKIP")) totalSkipped++;
                    else totalFailed++;
                }

                Result = new
                {
                    TotalFixed = totalFixed,
                    TotalSkipped = totalSkipped,
                    TotalFailed = totalFailed,
                    LargeFixes = largeFixes,
                    PreviewOnly = PreviewOnly,
                    MaxMoveInches = MaxMoveInches,
                    AdvisoryManualFix = advisoryTargets.Count > 0 ? advisoryTargets : null,
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

        public string GetName() => "Fix Off-Axis In-Place Component Sketches";
    }
}
