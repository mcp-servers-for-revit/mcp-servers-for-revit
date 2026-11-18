using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace RevitMCPCommandSet.Services.Dwg
{
    /// <summary>
    /// Shared DWG interrogation primitives used by the DwgCommandSet handlers:
    /// resolves imported/linked DWGs by id or name, and collects the curves of a
    /// given layer in world coordinates by recursively walking instance geometry.
    /// </summary>
    public static class DwgCurveSource
    {
        public class DwgCandidate
        {
            public Element Element;
            public long Id;
            public string Name;
            public string Kind;
        }

        public static long IdValue(Element e)
        {
#if REVIT2024_OR_GREATER
            return e.Id.Value;
#else
            return e.Id.IntegerValue;
#endif
        }

        /// <summary>
        /// Safety stats for a depth-capped geometry walk.
        /// </summary>
        public sealed class GeometryWalkStats
        {
            public int MaxDepthReached;
            public bool DepthCapped;
        }

        /// <summary>
        /// Maximum GeometryInstance nesting levels a geometry walk descends into.
        /// Real DWG block nesting beyond ~16 levels is pathological/broken input;
        /// 50 leaves ample headroom while bounding the frame stack.
        /// </summary>
        public const int MaxGeometryWalkDepth = 50;

        /// <summary>
        /// Depth-capped recursive walk over an element's geometry, descending
        /// into GeometryInstance.GetInstanceGeometry() and invoking the visitor
        /// for every geometry object (including instances, which visitors
        /// typically ignore).
        ///
        /// STACK-OVERFLOW GUARD (0xc00000fd): each nesting level costs one
        /// managed frame plus a full native GetInstanceGeometry() conversion in
        /// Revit's own code (native Utility.dll). A pathological DWG crashed
        /// Revit 2024.3.6 hard (journal 0433 / Application Error 1000,
        /// faulting module Utility.dll): StackOverflowException is uncatchable
        /// in .NET, so the recursion is bounded here instead. Note a managed
        /// cap cannot prevent recursion *inside* one native call - residual
        /// risk documented in AGENTS.md (DWG geometry walk safety).
        /// </summary>
        /// <param name="ge">Geometry element to walk (null-safe).</param>
        /// <param name="visit">Visitor invoked for each geometry object.</param>
        /// <param name="stats">Optional stats sink (depth reached / capped).</param>
        /// <param name="cancelled">Optional early-exit probe checked per object.</param>
        public static void WalkGeometry(
            GeometryElement ge,
            Action<GeometryObject> visit,
            GeometryWalkStats stats = null,
            Func<bool> cancelled = null)
        {
            WalkCore(ge, 0, visit, stats, cancelled);
        }

        private static void WalkCore(
            GeometryElement ge,
            int depth,
            Action<GeometryObject> visit,
            GeometryWalkStats stats,
            Func<bool> cancelled)
        {
            if (ge == null) return;
            if (depth > MaxGeometryWalkDepth)
            {
                if (stats != null) stats.DepthCapped = true;
                return;
            }
            if (stats != null && depth > stats.MaxDepthReached) stats.MaxDepthReached = depth;

            foreach (var o in ge)
            {
                if (cancelled != null && cancelled()) return;
                visit(o);

                var gi = o as GeometryInstance;
                if (gi == null) continue;
                GeometryElement inst = null;
                try { inst = gi.GetInstanceGeometry(); } catch { }
                WalkCore(inst, depth + 1, visit, stats, cancelled);
            }
        }

        public static string LayerName(Document doc, GeometryObject o)
        {
            try
            {
                var gs = doc.GetElement(o.GraphicsStyleId) as GraphicsStyle;
                return gs != null && gs.GraphicsStyleCategory != null ? gs.GraphicsStyleCategory.Name : "";
            }
            catch { return ""; }
        }

        /// <summary>
        /// All imported (ImportInstance) and linked CAD (RevitLinkInstance over
        /// CADLinkType) DWGs in the document.
        /// </summary>
        public static List<DwgCandidate> CollectCandidates(Document doc)
        {
            var result = new List<DwgCandidate>();

            foreach (ImportInstance ii in new FilteredElementCollector(doc).OfClass(typeof(ImportInstance)))
            {
                string name = ii.Category?.Name ?? ii.Name;
                result.Add(new DwgCandidate { Element = ii, Id = IdValue(ii), Name = name, Kind = "imported" });
            }

            foreach (RevitLinkInstance rli in new FilteredElementCollector(doc).OfClass(typeof(RevitLinkInstance)))
            {
                var lt = doc.GetElement(rli.GetTypeId()) as CADLinkType;
                if (lt == null) continue;
                result.Add(new DwgCandidate { Element = rli, Id = IdValue(rli), Name = lt.Name, Kind = "linked" });
            }

            return result;
        }

        /// <summary>
        /// Resolve a DWG element by element id, exact name, or partial name.
        /// </summary>
        public static Element ResolveDwg(Document doc, string query, out string name, out string kind)
        {
            query = (query ?? "").Trim();
            name = null; kind = null;
            var candidates = CollectCandidates(doc);

            if (query.Length == 0) return null;

            if (long.TryParse(query, out long idVal))
            {
                var byId = candidates.FirstOrDefault(x => x.Id == idVal);
                if (byId != null) { name = byId.Name; kind = byId.Kind; return byId.Element; }
            }

            var byName = candidates.FirstOrDefault(x => string.Equals(x.Name, query, StringComparison.OrdinalIgnoreCase));
            if (byName == null)
                byName = candidates.FirstOrDefault(x => x.Name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0);

            if (byName != null) { name = byName.Name; kind = byName.Kind; return byName.Element; }

            return null;
        }

        /// <summary>
        /// Collect all curves on the given layer from a DWG element's world
        /// geometry (depth-capped walk; see WalkGeometry for the rationale).
        /// </summary>
        public static List<Curve> CollectLayerCurves(Document doc, Element target, string layerFilter)
        {
            var collected = new List<Curve>();

            WalkGeometry(
                target.get_Geometry(new Options()),
                o =>
                {
                    var cv = o as Curve;
                    if (cv == null) return;
                    if (string.Equals(LayerName(doc, cv), layerFilter, StringComparison.OrdinalIgnoreCase))
                        collected.Add(cv);
                });

            return collected;
        }

        /// <summary>
        /// Median Z of curve start points (planar DWG assumption); falls back to 0.
        /// </summary>
        public static double MedianZ(List<Curve> curves)
        {
            var zValues = new List<double>();
            foreach (var cv in curves)
            {
                try { zValues.Add(cv.GetEndPoint(0).Z); } catch { }
            }
            if (zValues.Count == 0) return 0;
            zValues.Sort();
            return zValues[zValues.Count / 2];
        }
    }
}
