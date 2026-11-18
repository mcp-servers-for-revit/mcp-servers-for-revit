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
    public class FixOffAxisSketchesEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        private readonly ManualResetEvent _resetEvent = new ManualResetEvent(false);

        public HashSet<int> TargetHosts { get; set; } = new HashSet<int>();
        public HashSet<int> TargetLines { get; set; } = new HashSet<int>();
        public double MinDeviationDeg { get; set; } = 0.0000001;
        public double MaxDeviationDeg { get; set; } = 0.1;
        public int MaxElementsPerRun { get; set; } = 50;
        public double MaxMoveInches { get; set; } = OffAxisGeometryUtils.DefaultMaxMoveInches;
        public bool PreviewOnly { get; set; } = false;

        public object Result { get; private set; }

        public bool WaitForCompletion(int timeoutMilliseconds = 60000)
        {
            _resetEvent.Reset();
            return _resetEvent.WaitOne(timeoutMilliseconds);
        }

        public class SketchFixResult
        {
            public int ElementId { get; set; }
            public string Category { get; set; }
            public string TypeName { get; set; }
            public string Status { get; set; }
            public int LinesFixed { get; set; }
            public int LinesFailed { get; set; }
            public double TotalMovement { get; set; }
            public double MovementIn { get; set; }
            public bool LargeFix { get; set; }
            public List<string> Errors { get; set; }
            public bool Failed { get; set; }
            public string Validation { get; set; }
            public List<int> HostedIds { get; set; }
        }

        private struct SketchFixItem
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

                bool isTargetedMode = TargetHosts != null && TargetHosts.Count > 0;
                bool lineFilterActive = TargetLines != null && TargetLines.Count > 0;
                double tol = 0.001;
                double profileGapTol = 0.01;

                bool LineAllowed(ModelCurve mc)
                {
                    return !lineFilterActive || (mc != null && TargetLines.Contains(mc.Id.GetIntValue()));
                }

                List<int> GetHostedElementIds(Element elem)
                {
                    var ids = new List<int>();
                    if (elem is HostObject ho)
                    {
                        try
                        {
                            var inserts = ho.FindInserts(false, false, false, true);
                            if (inserts != null && inserts.Count > 0)
                            {
                                foreach (var id in inserts) ids.Add(id.GetIntValue());
                            }
                        }
                        catch { }
                    }
                    return ids;
                }

                var preprocessor = new SilentFailuresPreprocessor();

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

                SketchFixResult ApplyFixes(int id, string category, Element elem, List<SketchFixItem> fixes)
                {
                    int linesFixed = 0, linesFailed = 0;
                    var lineErrors = new List<string>();
                    bool wasRolledBack = false;

                    double totalMovement = 0;
                    double maxMovementIn = 0;
                    foreach (var fix in fixes)
                    {
                        double dist = fix.OrigP1.DistanceTo(fix.Snapped.GetEndPoint(1));
                        totalMovement += dist;
                        if (dist * 12.0 > maxMovementIn) maxMovementIn = dist * 12.0;
                    }
                    bool isLarge = maxMovementIn > OffAxisGeometryUtils.FlagMovementInches;
                    bool overCap = maxMovementIn > MaxMoveInches;

                    if (overCap)
                    {
                        return new SketchFixResult
                        {
                            ElementId = id,
                            Category = category,
                            Status = "SKIP - movement exceeds maxMoveInches cap",
                            LinesFailed = fixes.Count,
                            MovementIn = Math.Round(maxMovementIn, 4),
                            LargeFix = isLarge,
                            Failed = true
                        };
                    }

                    if (PreviewOnly)
                    {
                        return new SketchFixResult
                        {
                            ElementId = id,
                            Category = category,
                            Status = "PREVIEW",
                            LinesFixed = fixes.Count,
                            TotalMovement = Math.Round(totalMovement, 8),
                            MovementIn = Math.Round(maxMovementIn, 4),
                            LargeFix = isLarge
                        };
                    }

                    try
                    {
                        using (Transaction t = new Transaction(document, "Fix sketch " + id))
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
                                    linesFixed++;
                                }
                                catch (Exception ex)
                                {
                                    lineErrors.Add(ex.InnerException?.Message ?? ex.Message);
                                    linesFailed++;
                                }
                            }

                            var commitRes = t.Commit();
                            if (commitRes != TransactionStatus.Committed)
                            {
                                wasRolledBack = true;
                                linesFailed = fixes.Count;
                                linesFixed = 0;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        lineErrors.Add("Tx: " + (ex.InnerException?.Message ?? ex.Message));
                        linesFailed = fixes.Count;
                        linesFixed = 0;
                    }

                    if (wasRolledBack)
                    {
                        return new SketchFixResult
                        {
                            ElementId = id,
                            Category = category,
                            Status = "SKIP - constraints conflict (rolled back safely)",
                            LinesFixed = 0,
                            LinesFailed = linesFailed,
                            Failed = true,
                            LargeFix = false
                        };
                    }

                    string validationStatus = null;
                    if (linesFixed > 0)
                    {
                        try
                        {
                            ElementId sketchId = ElementId.InvalidElementId;
                            if (elem is Floor fl2) sketchId = fl2.SketchId;
                            else if (elem is Ceiling cl2) sketchId = cl2.SketchId;
                            if (sketchId != null && sketchId != ElementId.InvalidElementId)
                            {
                                if (document.GetElement(sketchId) is Sketch sk2)
                                {
                                    for (int li = 0; li < sk2.Profile.Size; li++)
                                    {
                                        CurveArray lp = sk2.Profile.get_Item(li);
                                        double maxGap = 0;
                                        for (int j = 0; j < lp.Size; j++)
                                        {
                                            double g = lp.get_Item(j).GetEndPoint(1).DistanceTo(lp.get_Item((j + 1) % lp.Size).GetEndPoint(0));
                                            if (g > maxGap) maxGap = g;
                                        }
                                        if (maxGap > 0.001) validationStatus = $"CLOSURE GAP: {Math.Round(maxGap, 6)}";
                                    }
                                }
                            }
                        }
                        catch { validationStatus = "validation skipped"; }
                    }

                    Element typeElem = null;
                    try { typeElem = document.GetElement(elem.GetTypeId()); } catch { }

                    return new SketchFixResult
                    {
                        ElementId = id,
                        Category = category,
                        TypeName = typeElem?.Name ?? "?",
                        Status = linesFailed == 0 ? "FIXED" : (linesFixed > 0 ? "PARTIAL" : "FAIL"),
                        LinesFixed = linesFixed,
                        LinesFailed = linesFailed,
                        TotalMovement = Math.Round(totalMovement, 8),
                        MovementIn = Math.Round(maxMovementIn, 4),
                        LargeFix = isLarge,
                        Errors = lineErrors.Count > 0 ? lineErrors : null,
                        Failed = linesFixed == 0,
                        Validation = validationStatus
                    };
                }

                SketchFixResult ProcessSketchBased(Element elem, string category)
                {
                    int id = elem.Id.GetIntValue();
                    if (elem.Pinned) return new SketchFixResult { ElementId = id, Category = category, Status = "SKIP - pinned", LargeFix = false };
                    if (dimConstrained.Contains(id)) return new SketchFixResult { ElementId = id, Category = category, Status = "SKIP - dimension constrained", LargeFix = false };

                    var hostedIds = GetHostedElementIds(elem);
                    if (hostedIds.Count > 0)
                        return new SketchFixResult { ElementId = id, Category = category, Status = "SKIP - hosted (" + hostedIds.Count + ")", HostedIds = hostedIds, LargeFix = false };

                    ElementId sketchId = ElementId.InvalidElementId;
                    if (elem is Floor fl) sketchId = fl.SketchId;
                    else if (elem is Ceiling cl) sketchId = cl.SketchId;
                    if (sketchId == null || sketchId == ElementId.InvalidElementId) return new SketchFixResult { ElementId = id, Category = category, Status = "SKIP - no sketch", LargeFix = false };

                    Sketch sketch = document.GetElement(sketchId) as Sketch;
                    if (sketch == null) return new SketchFixResult { ElementId = id, Category = category, Status = "SKIP - sketch not found", LargeFix = false };

                    CurveArrArray profile = sketch.Profile;
                    var allSketchElems = sketch.GetAllElements();
                    var modelCurves = new List<ModelCurve>();
                    for (int ei = 0; ei < allSketchElems.Count; ei++)
                    {
                        Element e = document.GetElement(allSketchElems[ei]);
                        if (e is ModelCurve mc) modelCurves.Add(mc);
                    }

                    // Pre-validate
                    for (int li = 0; li < profile.Size; li++)
                    {
                        CurveArray loop = profile.get_Item(li);
                        for (int j = 0; j < loop.Size; j++)
                        {
                            if (!(loop.get_Item(j) is Line)) return new SketchFixResult { ElementId = id, Category = category, Status = "SKIP - non-Line curves", LargeFix = false };
                            double gap = loop.get_Item(j).GetEndPoint(1).DistanceTo(loop.get_Item((j + 1) % loop.Size).GetEndPoint(0));
                            if (gap > profileGapTol) return new SketchFixResult { ElementId = id, Category = category, Status = "SKIP - broken profile", LargeFix = false };
                        }
                    }

                    var allFixes = new List<SketchFixItem>();

                    for (int li = 0; li < profile.Size; li++)
                    {
                        CurveArray loop = profile.get_Item(li);
                        int n = loop.Size;

                        var lines = new List<Line>();
                        var mcRefs = new List<ModelCurve>();
                        for (int j = 0; j < n; j++)
                        {
                            Line ln = loop.get_Item(j) as Line;
                            lines.Add(ln);
                            ModelCurve match = null;
                            for (int m = 0; m < modelCurves.Count; m++)
                            {
                                if (modelCurves[m].GeometryCurve is Line mcLn)
                                {
                                    if ((mcLn.GetEndPoint(0).DistanceTo(ln.GetEndPoint(0)) < tol && mcLn.GetEndPoint(1).DistanceTo(ln.GetEndPoint(1)) < tol) ||
                                        (mcLn.GetEndPoint(0).DistanceTo(ln.GetEndPoint(1)) < tol && mcLn.GetEndPoint(1).DistanceTo(ln.GetEndPoint(0)) < tol))
                                    {
                                        match = modelCurves[m];
                                        break;
                                    }
                                }
                            }
                            mcRefs.Add(match);
                        }

                        var Q = new XYZ[n];
                        var D = new XYZ[n];
                        bool anyOffAxis = false;

                        for (int j = 0; j < n; j++)
                        {
                            XYZ p0 = lines[j].GetEndPoint(0);
                            XYZ p1 = lines[j].GetEndPoint(1);
                            XYZ mid = (p0 + p1) * 0.5;
                            double a = OffAxisGeometryUtils.LineAngleDeg2D(lines[j]);
                            double dev = OffAxisGeometryUtils.DeviationFromAxis(a);

                            bool shouldSnap = lineFilterActive
                                ? LineAllowed(mcRefs[j])
                                : (dev >= MinDeviationDeg && dev <= MaxDeviationDeg);

                            if (shouldSnap)
                            {
                                anyOffAxis = true;
                                double signedAng = Math.Atan2(p1.Y - p0.Y, p1.X - p0.X) * OffAxisGeometryUtils.RadToDeg;
                                double snappedAng = Math.Round(signedAng / 45.0) * 45.0;
                                if (snappedAng <= -180) snappedAng = 180;
                                double rad = snappedAng * OffAxisGeometryUtils.DegToRad;
                                D[j] = new XYZ(Math.Cos(rad), Math.Sin(rad), 0);
                                Q[j] = new XYZ(mid.X, mid.Y, p0.Z);
                            }
                            else
                            {
                                D[j] = (p1 - p0).Normalize();
                                Q[j] = p0;
                            }
                        }

                        if (anyOffAxis)
                        {
                            var newVerts = new List<XYZ>();
                            for (int j = 0; j < n; j++)
                            {
                                int prev = (j - 1 + n) % n;
                                XYZ pA = Q[prev]; XYZ dA = D[prev];
                                XYZ pB = Q[j]; XYZ dB = D[j];
                                double det = dA.X * dB.Y - dA.Y * dB.X;
                                if (Math.Abs(det) > 1e-7)
                                {
                                    double t = ((pB.X - pA.X) * dB.Y - (pB.Y - pA.Y) * dB.X) / det;
                                    newVerts.Add(new XYZ(pA.X + t * dA.X, pA.Y + t * dA.Y, pA.Z));
                                }
                                else
                                {
                                    newVerts.Add((lines[prev].GetEndPoint(1) + lines[j].GetEndPoint(0)) * 0.5);
                                }
                            }

                            for (int j = 0; j < n; j++)
                            {
                                Line snapped = Line.CreateBound(newVerts[j], newVerts[(j + 1) % n]);
                                XYZ oP0 = lines[j].GetEndPoint(0), oP1 = lines[j].GetEndPoint(1);
                                if (oP0.DistanceTo(snapped.GetEndPoint(0)) > 1e-6 || oP1.DistanceTo(snapped.GetEndPoint(1)) > 1e-6)
                                {
                                    if (mcRefs[j] != null && LineAllowed(mcRefs[j]))
                                        allFixes.Add(new SketchFixItem { MC = mcRefs[j], Snapped = snapped, OrigP1 = oP1 });
                                }
                            }
                        }
                    }

                    if (allFixes.Count == 0) return new SketchFixResult { ElementId = id, Category = category, Status = "SKIP - on axis", LargeFix = false };
                    return ApplyFixes(id, category, elem, allFixes);
                }

                SketchFixResult ProcessRoof(FootPrintRoof roof)
                {
                    int id = roof.Id.GetIntValue();
                    string category = "Roof";
                    if (roof.Pinned) return new SketchFixResult { ElementId = id, Category = category, Status = "SKIP - pinned", LargeFix = false };
                    if (dimConstrained.Contains(id)) return new SketchFixResult { ElementId = id, Category = category, Status = "SKIP - dimension constrained", LargeFix = false };

                    var hostedIds = GetHostedElementIds(roof);
                    if (hostedIds.Count > 0)
                        return new SketchFixResult { ElementId = id, Category = category, Status = "SKIP - hosted (" + hostedIds.Count + ")", HostedIds = hostedIds, LargeFix = false };

                    ModelCurveArrArray profiles;
                    try { profiles = roof.GetProfiles(); } catch { return new SketchFixResult { ElementId = id, Category = category, Status = "SKIP - GetProfiles failed", LargeFix = false }; }
                    if (profiles == null || profiles.Size == 0) return new SketchFixResult { ElementId = id, Category = category, Status = "SKIP - no profiles", LargeFix = false };

                    for (int pi = 0; pi < profiles.Size; pi++)
                    {
                        ModelCurveArray loop = profiles.get_Item(pi);
                        for (int ci = 0; ci < loop.Size; ci++)
                            if (!(loop.get_Item(ci).GeometryCurve is Line))
                                return new SketchFixResult { ElementId = id, Category = category, Status = "SKIP - non-Line curves", LargeFix = false };
                    }

                    var allFixes = new List<SketchFixItem>();
                    for (int pi = 0; pi < profiles.Size; pi++)
                    {
                        ModelCurveArray loop = profiles.get_Item(pi);
                        int n = loop.Size;
                        var lines = new List<Line>();
                        var mcRefs = new List<ModelCurve>();
                        for (int ci = 0; ci < n; ci++)
                        {
                            ModelCurve mc = loop.get_Item(ci);
                            if (mc.GeometryCurve is Line ln)
                            {
                                lines.Add(ln);
                                mcRefs.Add(mc);
                            }
                            else
                            {
                                lines.Add(null);
                                mcRefs.Add(null);
                            }
                        }
                        if (lines.Count < 2) continue;

                        var Q = new XYZ[n];
                        var D = new XYZ[n];
                        bool anyOffAxis = false;

                        for (int j = 0; j < n; j++)
                        {
                            if (lines[j] == null) continue;
                            XYZ p0 = lines[j].GetEndPoint(0);
                            XYZ p1 = lines[j].GetEndPoint(1);
                            XYZ mid = (p0 + p1) * 0.5;
                            double a = OffAxisGeometryUtils.LineAngleDeg2D(lines[j]);
                            double dev = OffAxisGeometryUtils.DeviationFromAxis(a);

                            bool shouldSnap = lineFilterActive
                                ? LineAllowed(mcRefs[j])
                                : (dev >= MinDeviationDeg && dev <= MaxDeviationDeg);

                            if (shouldSnap)
                            {
                                anyOffAxis = true;
                                double signedAng = Math.Atan2(p1.Y - p0.Y, p1.X - p0.X) * OffAxisGeometryUtils.RadToDeg;
                                double snappedAng = Math.Round(signedAng / 45.0) * 45.0;
                                if (snappedAng <= -180) snappedAng = 180;
                                double rad = snappedAng * OffAxisGeometryUtils.DegToRad;
                                D[j] = new XYZ(Math.Cos(rad), Math.Sin(rad), 0);
                                Q[j] = new XYZ(mid.X, mid.Y, p0.Z);
                            }
                            else
                            {
                                D[j] = (p1 - p0).Normalize();
                                Q[j] = p0;
                            }
                        }

                        if (anyOffAxis)
                        {
                            var newVerts = new List<XYZ>();
                            for (int j = 0; j < n; j++)
                            {
                                int prev = (j - 1 + n) % n;
                                XYZ pA = Q[prev]; XYZ dA = D[prev];
                                XYZ pB = Q[j]; XYZ dB = D[j];
                                double det = dA.X * dB.Y - dA.Y * dB.X;
                                if (Math.Abs(det) > 1e-7)
                                {
                                    double t = ((pB.X - pA.X) * dB.Y - (pB.Y - pA.Y) * dB.X) / det;
                                    newVerts.Add(new XYZ(pA.X + t * dA.X, pA.Y + t * dA.Y, pA.Z));
                                }
                                else
                                {
                                    newVerts.Add((lines[prev].GetEndPoint(1) + lines[j].GetEndPoint(0)) * 0.5);
                                }
                            }

                            for (int j = 0; j < n; j++)
                            {
                                Line snapped = Line.CreateBound(newVerts[j], newVerts[(j + 1) % n]);
                                XYZ oP0 = lines[j].GetEndPoint(0), oP1 = lines[j].GetEndPoint(1);
                                if (oP0.DistanceTo(snapped.GetEndPoint(0)) > 1e-6 || oP1.DistanceTo(snapped.GetEndPoint(1)) > 1e-6)
                                {
                                    if (mcRefs[j] != null && LineAllowed(mcRefs[j]))
                                        allFixes.Add(new SketchFixItem { MC = mcRefs[j], Snapped = snapped, OrigP1 = oP1 });
                                }
                            }
                        }
                    }

                    if (allFixes.Count == 0) return new SketchFixResult { ElementId = id, Category = category, Status = "SKIP - on axis", LargeFix = false };
                    return ApplyFixes(id, category, roof, allFixes);
                }

                var allFloors = new FilteredElementCollector(document).OfClass(typeof(Floor)).WhereElementIsNotElementType().Cast<Floor>().ToList();
                var allCeilings = new FilteredElementCollector(document).OfClass(typeof(Ceiling)).WhereElementIsNotElementType().Cast<Ceiling>().ToList();
                var allRoofs = new FilteredElementCollector(document).OfClass(typeof(FootPrintRoof)).WhereElementIsNotElementType().Cast<FootPrintRoof>().ToList();
                var allCandidates = new List<(int, string, Element)>();

                var fixLog = new List<SketchFixResult>();
                int totalFixed = 0, totalSkipped = 0, totalFailed = 0;
                int largeFixes = 0;

                void Record(SketchFixResult res)
                {
                    if (res == null) return;
                    fixLog.Add(res);
                    if (res.LargeFix) largeFixes++;
                    string s = res.Status ?? "";
                    if (s == "FIXED" || s == "PARTIAL") totalFixed++;
                    else if (s.StartsWith("SKIP")) totalSkipped++;
                    else totalFailed++;
                }

                foreach (var fl in allFloors)
                {
                    if (!isTargetedMode || TargetHosts.Contains(fl.Id.GetIntValue()))
                        allCandidates.Add((fl.Id.GetIntValue(), "Floor", fl));
                }
                foreach (var cl in allCeilings)
                {
                    if (!isTargetedMode || TargetHosts.Contains(cl.Id.GetIntValue()))
                        allCandidates.Add((cl.Id.GetIntValue(), "Ceiling", cl));
                }
                foreach (var rf in allRoofs)
                {
                    if (!isTargetedMode || TargetHosts.Contains(rf.Id.GetIntValue()))
                        allCandidates.Add((rf.Id.GetIntValue(), "Roof", rf));
                }

                int truncated = 0;
                int processed = 0;
                foreach (var (_, cat, elem) in allCandidates)
                {
                    if (processed >= MaxElementsPerRun) { truncated++; continue; }
                    processed++;
                    if (cat == "Floor") Record(ProcessSketchBased(elem, "Floor"));
                    else if (cat == "Ceiling") Record(ProcessSketchBased(elem, "Ceiling"));
                    else Record(ProcessRoof((FootPrintRoof)elem));
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
                    MaxElementsPerRun = MaxElementsPerRun,
                    Truncated = truncated,
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

        public string GetName() => "Fix Off-Axis Sketch Elements";
    }
}
