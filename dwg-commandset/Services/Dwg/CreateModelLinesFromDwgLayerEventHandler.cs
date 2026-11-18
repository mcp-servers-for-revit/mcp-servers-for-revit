using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCPSDK.API.Interfaces;

namespace RevitMCPCommandSet.Services.Dwg
{
    /// <summary>
    /// Mutating handler that draws one DWG layer's curves as Revit model lines.
    /// Line -> bound line; bounded Arc -> 3-point arc; everything else
    /// (full circles, ellipses, splines) -> tessellated polyline segments.
    /// All curves are placed on a sketch plane at the layer's median Z.
    /// </summary>
    public class CreateModelLinesFromDwgLayerEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        private readonly ManualResetEvent _resetEvent = new ManualResetEvent(false);

        public string DwgNameOrId { get; set; }
        public string Layer { get; set; }
        public int MaxLines { get; set; } = 200;

        public object Result { get; private set; }

        public bool WaitForCompletion(int timeoutMilliseconds = 120000)
        {
            _resetEvent.Reset();
            return _resetEvent.WaitOne(timeoutMilliseconds);
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

                // Resolve the DWG element (same resolution rules as extract_dwg_curves).
                Element target = DwgCurveSource.ResolveDwg(doc, DwgNameOrId.Trim(), out string targetName, out string targetKind);
                if (target == null)
                {
                    Result = new Dictionary<string, object> { ["error"] = $"No imported or linked DWG matching '{DwgNameOrId}'" };
                    return;
                }

                // Pass 1: collect curves on the requested layer (world geometry,
                // depth-capped walk - see DwgCurveSource.WalkGeometry for the
                // stack-overflow rationale).
                var collected = new List<Curve>();
                var zValues = new List<double>();
                string layerF = Layer.Trim();
                var walkStats = new DwgCurveSource.GeometryWalkStats();

                DwgCurveSource.WalkGeometry(
                    target.get_Geometry(new Options()),
                    o =>
                    {
                        var cv = o as Curve;
                        if (cv == null) return;
                        string layer = DwgCurveSource.LayerName(doc, cv);
                        if (!string.Equals(layer, layerF, StringComparison.OrdinalIgnoreCase)) return;
                        collected.Add(cv);
                        try { zValues.Add(cv.GetEndPoint(0).Z); } catch { }
                    },
                    walkStats);

                if (collected.Count == 0)
                {
                    Result = new Dictionary<string, object>
                    {
                        ["error"] = $"No curves found on layer '{Layer}' of '{targetName}'",
                        ["hint"] = "Use extract_dwg_curves to list layer names for this DWG"
                    };
                    return;
                }

                // Sketch plane at the layer's median Z (planar DWG assumption).
                zValues.Sort();
                double z = zValues[zValues.Count / 2];
                var plane = Plane.CreateByNormalAndOrigin(XYZ.BasisZ, new XYZ(0, 0, z));
                var sketchPlane = SketchPlane.Create(doc, plane);

                // Pass 2: draw.
                int created = 0, skipped = 0, arcCount = 0, polyCount = 0, lineCount = 0;
                var createdIds = new List<long>();
                const double minSeg = 1e-6; // ft

                var preprocessor = new RevitMCPCommandSet.Utils.Dwg.SilentWarningsPreprocessor();
                using (var trans = new Transaction(doc, "Draw Model Lines from DWG Layer"))
                {
                    var failOptions = trans.GetFailureHandlingOptions();
                    trans.SetFailureHandlingOptions(failOptions.SetFailuresPreprocessor(preprocessor));

                    trans.Start();

                    foreach (var cv in collected)
                    {
                        if (created >= MaxLines) break;

                        try
                        {
                            bool hasEnds = false; XYZ s = null, e = null;
                            try { s = cv.GetEndPoint(0); e = cv.GetEndPoint(1); hasEnds = true; } catch { }

                            // Bound line for straight segments.
                            if (cv is Line && hasEnds)
                            {
                                if (AddCurve(doc, sketchPlane, cv, createdIds)) { created++; lineCount++; }
                                continue;
                            }

                            // 3-point arc for bounded arcs.
                            if (cv is Arc && hasEnds)
                            {
                                var pts = cv.Tessellate().ToList();
                                if (pts.Count >= 3)
                                {
                                    var mid = pts[pts.Count / 2];
                                    var arc3 = Arc.Create(s, e, mid);
                                    if (AddCurve(doc, sketchPlane, arc3, createdIds)) { created++; arcCount++; continue; }
                                }
                            }

                            // Fallback: tessellated polyline (full circles, ellipses, splines).
                            var tess = cv.Tessellate();
                            int segs = 0;
                            for (int i = 0; i < tess.Count - 1; i++)
                            {
                                if (tess[i].DistanceTo(tess[i + 1]) < minSeg) continue;
                                var ln = Line.CreateBound(tess[i], tess[i + 1]);
                                if (AddCurve(doc, sketchPlane, ln, createdIds)) { created++; segs++; }
                                if (created >= MaxLines) break;
                            }
                            if (segs > 0) polyCount++;
                        }
                        catch
                        {
                            skipped++;
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
                    ["layer"] = layerF,
                    ["sketchPlaneZ"] = z,
                    ["curvesOnLayer"] = collected.Count,
                    ["created"] = created,
                    ["skipped"] = skipped,
                    ["asLine"] = lineCount,
                    ["asArc"] = arcCount,
                    ["asPolyline"] = polyCount,
                    ["truncated"] = collected.Count > MaxLines,
                    ["depthCapped"] = walkStats.DepthCapped,
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

        private static bool AddCurve(Document doc, SketchPlane sp, Curve c, List<long> ids)
        {
            var mc = doc.Create.NewModelCurve(c, sp);
            if (mc == null) return false;
#if REVIT2024_OR_GREATER
            ids.Add(mc.Id.Value);
#else
            ids.Add(mc.Id.IntegerValue);
#endif
            return true;
        }

        public string GetName()
        {
            return "Create Model Lines from DWG Layer";
        }
    }
}

