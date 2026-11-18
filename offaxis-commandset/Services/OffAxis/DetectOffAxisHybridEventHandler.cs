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
    public class DetectOffAxisHybridEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        private readonly ManualResetEvent _resetEvent = new ManualResetEvent(false);

        private static readonly HashSet<string> ExcludedCategories = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Mass", "Toposolid"
        };

        private static readonly HashSet<string> AdvisoryFormTypes = new HashSet<string>(StringComparer.Ordinal)
        {
            "Blend", "Sweep", "Revolve", "GenericForm", "SweptBlend"
        };

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

                var allWarnings = document.GetWarnings()?.ToList() ?? new List<FailureMessage>();
                var offAxisWarnings = allWarnings.Where(w =>
                {
                    try
                    {
                        string t = w.GetDescriptionText();
                        return t != null && t.ToLower().Contains("off axis");
                    }
                    catch { return false; }
                }).ToList();

                var fixable = new List<object>();
                var unsupported = new List<object>();
                var processed = new HashSet<int>();
                var sketchHosts = new Dictionary<int, Element>();
                var sketchLineIds = new Dictionary<int, HashSet<int>>();
                var unresolvedSketchLines = new List<object>();
                var inPlaceMeta = new Dictionary<int, Dictionary<string, string>>();
                var inPlaceLines = new Dictionary<int, HashSet<int>>();
                var manualFixAdvisories = new List<object>();

                foreach (var w in offAxisWarnings)
                {
                    ICollection<ElementId> failing = null;
                    try { failing = w.GetFailingElements(); } catch { }
                    if (failing == null || failing.Count == 0) continue;

                    Element host = null;
                    FamilyInstance inPlaceInstance = null;
                    foreach (var fid in failing)
                    {
                        Element e = null;
                        try { e = document.GetElement(fid); } catch { }
                        if (e is Floor || e is Ceiling || e is FootPrintRoof) { host = e; break; }
                        if (inPlaceInstance == null && e is FamilyInstance fi && (fi.Symbol?.Family?.IsInPlace ?? false))
                            inPlaceInstance = fi;
                    }

                    if (inPlaceInstance != null)
                    {
                        string formType = "";
                        var ipLines = new HashSet<int>();
                        foreach (var fid in failing)
                        {
                            Element e = null;
                            try { e = document.GetElement(fid); } catch { }
                            if (e == null) continue;
                            string tp = e.GetType().Name;
                            if (tp == "Extrusion" || AdvisoryFormTypes.Contains(tp))
                            {
                                if (string.IsNullOrEmpty(formType)) formType = tp;
                            }
                            else if (IsSketchLine(e))
                            {
                                ipLines.Add(e.Id.GetIntValue());
                            }
                        }

                        int hid = inPlaceInstance.Id.GetIntValue();
                        if (inPlaceMeta.ContainsKey(hid))
                        {
                            if (!string.IsNullOrEmpty(formType) && string.IsNullOrEmpty(inPlaceMeta[hid]["FormType"]))
                                inPlaceMeta[hid]["FormType"] = formType;
                            foreach (var l in ipLines) inPlaceLines[hid].Add(l);
                        }
                        else
                        {
                            string cat = inPlaceInstance.Category?.Name ?? "";
                            string fam = "";
                            try { fam = inPlaceInstance.Symbol.Family.Name; } catch { }
                            inPlaceMeta[hid] = new Dictionary<string, string>
                            {
                                ["Category"] = cat,
                                ["Family"] = fam,
                                ["FormType"] = formType
                            };
                            inPlaceLines[hid] = new HashSet<int>(ipLines);
                        }
                        continue;
                    }

                    foreach (var fid in failing)
                    {
                        Element e = null;
                        try { e = document.GetElement(fid); } catch { }
                        if (e == null) continue;

                        if (host != null)
                        {
                            if (IsSketchLine(e))
                            {
                                int hid = host.Id.GetIntValue();
                                if (!sketchLineIds.ContainsKey(hid)) sketchLineIds[hid] = new HashSet<int>();
                                if (sketchLineIds[hid].Add(e.Id.GetIntValue())) sketchHosts[hid] = host;
                            }
                            continue;
                        }

                        string key = HandlerKey(e);
                        if (key == "Wall" || key == "Beam" || key == "Grid" || key == "ReferencePlane" || key == "ModelLine")
                        {
                            if (processed.Add(e.Id.GetIntValue()))
                            {
                                object entry = BuildSingular(document, e, key);
                                if (entry != null) fixable.Add(entry);
                            }
                        }
                        else if (IsSketchLine(e))
                        {
                            unresolvedSketchLines.Add(new
                            {
                                ElementId = e.Id.GetIntValue(),
                                ElementType = e.GetType().Name,
                                Category = e.Category?.Name,
                                Status = "UNRESOLVED sketch line (no host in warning)"
                            });
                        }
                        else
                        {
                            string desc;
                            try { desc = w.GetDescriptionText(); } catch { desc = "?"; }
                            unsupported.Add(new
                            {
                                ElementId = e.Id.GetIntValue(),
                                ElementType = e.GetType().Name,
                                Category = e.Category?.Name ?? "None",
                                WarningText = desc.Length > 80 ? desc.Substring(0, 80) : desc
                            });
                        }
                    }
                }

                // Materialize sketch-host fixable entries
                foreach (var kvp in sketchHosts)
                {
                    int hid = kvp.Key;
                    Element hostEl = kvp.Value;
                    var lineIds = sketchLineIds.ContainsKey(hid) ? sketchLineIds[hid] : new HashSet<int>();

                    var segments = new List<object>();
                    double primaryAng = double.NaN;
                    double hostSwing = 0.0;
                    foreach (var lid in lineIds)
                    {
                        Element le = null;
                        try { le = document.GetElement(new ElementId(lid)); } catch { }
                        Line ln = (le is ModelCurve m && m.GeometryCurve is Line l) ? l : null;
                        if (ln == null)
                        {
                            segments.Add(new { LineId = lid, Error = "geometry unavailable" });
                            continue;
                        }
                        double ang = OffAxisGeometryUtils.LineAngleDeg2D(ln);
                        if (double.IsNaN(primaryAng)) primaryAng = ang;
                        double devS = OffAxisGeometryUtils.DeviationFromAxis(ang);
                        double swingS = OffAxisGeometryUtils.OccupiedSwingInches(ln.Length, devS);
                        if (swingS > hostSwing) hostSwing = swingS;

                        segments.Add(new
                        {
                            LineId = lid,
                            P0 = PtArr(ln.GetEndPoint(0)),
                            P1 = PtArr(ln.GetEndPoint(1)),
                            AngleDeg = Math.Round(ang, 4),
                            DeviationDeg = Math.Round(devS, 4),
                            SnapTargetDeg = OffAxisGeometryUtils.SnapTargetDeg(ang),
                            PredictedMovementIn = Math.Round(swingS, 4),
                            LargeFix = swingS > OffAxisGeometryUtils.FlagMovementInches || devS > OffAxisGeometryUtils.FlagDeviationDegrees
                        });
                    }

                    string cat = hostEl is Floor ? "Floor" : hostEl is Ceiling ? "Ceiling" : "Roof";
                    double hostDev = double.IsNaN(primaryAng) ? 0.0 : OffAxisGeometryUtils.DeviationFromAxis(primaryAng);
                    fixable.Add(new
                    {
                        ElementId = hid,
                        Category = cat,
                        ElementType = hostEl.GetType().Name,
                        TypeName = SafeTypeName(document, hostEl),
                        Fixer = "FixSketches",
                        AngleDeg = double.IsNaN(primaryAng) ? (double?)null : Math.Round(primaryAng, 4),
                        DeviationDeg = double.IsNaN(primaryAng) ? (double?)null : Math.Round(OffAxisGeometryUtils.DeviationFromAxis(primaryAng), 4),
                        Pinned = hostEl.Pinned,
                        SegmentsCount = segments.Count,
                        PredictedMovementIn = Math.Round(hostSwing, 4),
                        LargeFix = hostSwing > OffAxisGeometryUtils.FlagMovementInches || hostDev > OffAxisGeometryUtils.FlagDeviationDegrees,
                        Geometry = new { Segments = segments }
                    });
                }

                // Materialize in-place fixables & advisories
                foreach (var kvp in inPlaceMeta)
                {
                    int hid = kvp.Key;
                    var md = kvp.Value;
                    string cat = md.ContainsKey("Category") ? md["Category"] : "";
                    string fam = md.ContainsKey("Family") ? md["Family"] : "";
                    string formType = md.ContainsKey("FormType") ? md["FormType"] : "";
                    var lineIds = inPlaceLines.ContainsKey(hid) ? inPlaceLines[hid] : new HashSet<int>();

                    bool excluded = ExcludedCategories.Contains(cat);
                    if (excluded)
                    {
                        manualFixAdvisories.Add(new
                        {
                            ElementId = hid,
                            Category = cat,
                            TypeName = fam,
                            FormType = string.IsNullOrEmpty(formType) ? "?" : formType,
                            Status = "EXCLUDED category (manual review)"
                        });
                        continue;
                    }
                    if (formType != "Extrusion")
                    {
                        manualFixAdvisories.Add(new
                        {
                            ElementId = hid,
                            Category = cat,
                            TypeName = fam,
                            FormType = string.IsNullOrEmpty(formType) ? "?" : formType,
                            Status = "NON-Extrusion in-place primitive (manual fix)"
                        });
                        continue;
                    }

                    Element hostEl = null;
                    try { hostEl = document.GetElement(new ElementId(hid)); } catch { }

                    var segments = new List<object>();
                    double primaryAng = double.NaN;
                    double ipSwing = 0.0;
                    foreach (var lid in lineIds)
                    {
                        Element le = null;
                        try { le = document.GetElement(new ElementId(lid)); } catch { }
                        Line ln = (le is ModelCurve m && m.GeometryCurve is Line l) ? l : null;
                        if (ln == null)
                        {
                            segments.Add(new { LineId = lid, Error = "geometry unavailable" });
                            continue;
                        }
                        double ang = OffAxisGeometryUtils.LineAngleDeg2D(ln);
                        if (double.IsNaN(primaryAng)) primaryAng = ang;
                        double devS = OffAxisGeometryUtils.DeviationFromAxis(ang);
                        double swingS = OffAxisGeometryUtils.OccupiedSwingInches(ln.Length, devS);
                        if (swingS > ipSwing) ipSwing = swingS;

                        segments.Add(new
                        {
                            LineId = lid,
                            P0 = PtArr(ln.GetEndPoint(0)),
                            P1 = PtArr(ln.GetEndPoint(1)),
                            AngleDeg = Math.Round(ang, 4),
                            DeviationDeg = Math.Round(devS, 4),
                            SnapTargetDeg = OffAxisGeometryUtils.SnapTargetDeg(ang),
                            PredictedMovementIn = Math.Round(swingS, 4),
                            LargeFix = swingS > OffAxisGeometryUtils.FlagMovementInches || devS > OffAxisGeometryUtils.FlagDeviationDegrees
                        });
                    }

                    double ipDev = double.IsNaN(primaryAng) ? 0.0 : OffAxisGeometryUtils.DeviationFromAxis(primaryAng);
                    string target = hid + ";" + string.Join(",", lineIds);

                    fixable.Add(new
                    {
                        ElementId = hid,
                        Category = cat,
                        ElementType = "FamilyInstance (In-Place Extrusion)",
                        TypeName = fam,
                        Fixer = "FixInPlaceSketches",
                        Target = target,
                        AngleDeg = double.IsNaN(primaryAng) ? (double?)null : Math.Round(primaryAng, 4),
                        DeviationDeg = double.IsNaN(primaryAng) ? (double?)null : Math.Round(ipDev, 4),
                        Pinned = hostEl?.Pinned ?? false,
                        SegmentsCount = segments.Count,
                        PredictedMovementIn = Math.Round(ipSwing, 4),
                        LargeFix = ipSwing > OffAxisGeometryUtils.FlagMovementInches || ipDev > OffAxisGeometryUtils.FlagDeviationDegrees,
                        Geometry = new { Segments = segments }
                    });
                }

                Result = new
                {
                    TotalWarnings = allWarnings.Count,
                    OffAxisWarnings = offAxisWarnings.Count,
                    FixableCount = fixable.Count,
                    UnsupportedCount = unsupported.Count,
                    ManualFixAdvisoryCount = manualFixAdvisories.Count,
                    UnresolvedSketchLinesCount = unresolvedSketchLines.Count,
                    FixableElements = fixable,
                    ManualFixAdvisories = manualFixAdvisories.Count > 0 ? manualFixAdvisories : null,
                    UnsupportedElements = unsupported.Count > 0 ? unsupported : null,
                    UnresolvedSketchLines = unresolvedSketchLines.Count > 0 ? unresolvedSketchLines : null
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

        public string GetName() => "Detect Off-Axis Elements (Hybrid)";

        private static bool IsSketchLine(Element e)
        {
            if (!(e is ModelCurve)) return false;
            int catId = e.Category?.Id.GetIntValue() ?? -1;
            string catName = e.Category?.Name ?? "";
            return catName.Contains("Sketch") || catId == -2000045;
        }

        private static string HandlerKey(Element e)
        {
            if (e is Grid) return "Grid";
            if (e is ReferencePlane) return "ReferencePlane";
            if (e is Wall) return "Wall";
            if (e is Floor) return "Floor";
            if (e is Ceiling) return "Ceiling";
            if (e is FootPrintRoof) return "Roof";
            if (e is FamilyInstance fi && fi.Category != null && fi.Category.Id.GetIntValue() == (int)BuiltInCategory.OST_StructuralFraming)
                return "Beam";
            if (e is ModelCurve mcur && mcur.Category != null && !(mcur.Category.Name ?? "").Contains("Sketch"))
                return "ModelLine";
            return null;
        }

        private static string SafeTypeName(Document doc, Element e)
        {
            try
            {
                ElementId tid = e.GetTypeId();
                if (tid != ElementId.InvalidElementId)
                {
                    Element t = doc.GetElement(tid);
                    if (t != null && !string.IsNullOrEmpty(t.Name)) return t.Name;
                }
            }
            catch { }
            if (e is Wall w) return w.WallType?.Name ?? "?";
            if (e is FamilyInstance fi) return fi.Symbol?.Name ?? "?";
            if (e is Grid g) return g.Name;
            if (e is ReferencePlane rp) return rp.Name;
            return "?";
        }

        private static double[] PtArr(XYZ p) => new[] { p.X, p.Y, p.Z };

        private static object BuildSingular(Document doc, Element e, string key)
        {
            if ((key == "Wall" || key == "Beam") && e.Location is LocationCurve lc && lc.Curve is Line ln)
            {
                double ang = OffAxisGeometryUtils.LineAngleDeg2D(ln);
                double dev = OffAxisGeometryUtils.DeviationFromAxis(ang);
                double swing = OffAxisGeometryUtils.OccupiedSwingInches(ln.Length, dev);
                return new
                {
                    ElementId = e.Id.GetIntValue(),
                    Category = key,
                    ElementType = e.GetType().Name,
                    TypeName = SafeTypeName(doc, e),
                    Fixer = "FixWallsAndBeams",
                    AngleDeg = Math.Round(ang, 4),
                    DeviationDeg = Math.Round(dev, 4),
                    SnapTargetDeg = OffAxisGeometryUtils.SnapTargetDeg(ang),
                    Pinned = e.Pinned,
                    PredictedMovementIn = Math.Round(swing, 4),
                    LargeFix = swing > OffAxisGeometryUtils.FlagMovementInches || dev > OffAxisGeometryUtils.FlagDeviationDegrees,
                    Geometry = new { P0 = PtArr(ln.GetEndPoint(0)), P1 = PtArr(ln.GetEndPoint(1)), Length = Math.Round(ln.Length, 6) }
                };
            }
            if (key == "Grid" && e is Grid g && g.Curve is Line gl)
            {
                double ang = OffAxisGeometryUtils.LineAngleDeg2D(gl);
                double dev = OffAxisGeometryUtils.DeviationFromAxis(ang);
                double swing = OffAxisGeometryUtils.OccupiedSwingInches(gl.Length, dev);
                return new
                {
                    ElementId = e.Id.GetIntValue(),
                    Category = "Grid",
                    ElementType = "Grid",
                    TypeName = g.Name,
                    Fixer = "FixGrids",
                    AngleDeg = Math.Round(ang, 4),
                    DeviationDeg = Math.Round(dev, 4),
                    SnapTargetDeg = OffAxisGeometryUtils.SnapTargetDeg(ang),
                    Pinned = e.Pinned,
                    PredictedMovementIn = Math.Round(swing, 4),
                    LargeFix = swing > OffAxisGeometryUtils.FlagMovementInches || dev > OffAxisGeometryUtils.FlagDeviationDegrees,
                    Geometry = new { P0 = PtArr(gl.GetEndPoint(0)), P1 = PtArr(gl.GetEndPoint(1)), Length = Math.Round(gl.Length, 6) }
                };
            }
            if (key == "ModelLine" && e is ModelCurve ml && ml.GeometryCurve is Line mll)
            {
                double ang = OffAxisGeometryUtils.LineAngleDeg2D(mll);
                double dev = OffAxisGeometryUtils.DeviationFromAxis(ang);
                double swing = OffAxisGeometryUtils.OccupiedSwingInches(mll.Length, dev);
                return new
                {
                    ElementId = e.Id.GetIntValue(),
                    Category = e.Category?.Name ?? "ModelLine",
                    ElementType = e.GetType().Name,
                    TypeName = e.Category?.Name ?? e.GetType().Name,
                    Fixer = "FixModelLines",
                    Target = e.Id.GetIntValue().ToString(),
                    AngleDeg = Math.Round(ang, 4),
                    DeviationDeg = Math.Round(dev, 4),
                    SnapTargetDeg = OffAxisGeometryUtils.SnapTargetDeg(ang),
                    Pinned = e.Pinned,
                    PredictedMovementIn = Math.Round(swing, 4),
                    LargeFix = swing > OffAxisGeometryUtils.FlagMovementInches || dev > OffAxisGeometryUtils.FlagDeviationDegrees,
                    Geometry = new { P0 = PtArr(mll.GetEndPoint(0)), P1 = PtArr(mll.GetEndPoint(1)), Length = Math.Round(mll.Length, 6) }
                };
            }
            if (key == "ReferencePlane" && e is ReferencePlane rp)
            {
                Plane pl;
                try { pl = rp.GetPlane(); }
                catch { return null; }
                XYZ n = pl.Normal;
                if (Math.Abs(n.Z) > 0.01) return null;
                double ang = Math.Atan2(Math.Abs(n.Y), Math.Abs(n.X)) * OffAxisGeometryUtils.RadToDeg;
                double dev = OffAxisGeometryUtils.DeviationFromAxis(ang);
                double len = rp.FreeEnd.DistanceTo(rp.BubbleEnd);
                double swing = OffAxisGeometryUtils.OccupiedSwingInches(len, dev);
                return new
                {
                    ElementId = e.Id.GetIntValue(),
                    Category = "ReferencePlane",
                    ElementType = "ReferencePlane",
                    TypeName = rp.Name,
                    Fixer = "FixReferencePlanes",
                    AngleDeg = Math.Round(ang, 4),
                    DeviationDeg = Math.Round(dev, 4),
                    SnapTargetDeg = OffAxisGeometryUtils.SnapTargetDeg(ang),
                    Pinned = e.Pinned,
                    PredictedMovementIn = Math.Round(swing, 4),
                    LargeFix = swing > OffAxisGeometryUtils.FlagMovementInches || dev > OffAxisGeometryUtils.FlagDeviationDegrees,
                    Geometry = new { Normal = PtArr(n), BubbleEnd = PtArr(rp.BubbleEnd), FreeEnd = PtArr(rp.FreeEnd) }
                };
            }
            return null;
        }
    }
}
