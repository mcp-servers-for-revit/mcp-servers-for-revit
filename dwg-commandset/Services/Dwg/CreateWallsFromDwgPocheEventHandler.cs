using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCPCommandSet.Geometry;
using RevitMCPCommandSet.Services.Dwg;
using RevitMCPCommandSet.Utils.Dwg;
using RevitMCPSDK.API.Interfaces;

namespace RevitMCPCommandSet.Services.Dwg
{
    /// <summary>
    /// Mutating handler that generates Revit walls from the HATCH fills of a
    /// DWG import (poche regions). Lines and polylines are NOT used here —
    /// that is create_walls_from_dwg_layer's job.DWG hatch entities are
    /// exposed by Revit's import as flat (volume-zero) solids with no solid
    /// level GraphicsStyle; the source DWG layer survives one level deeper,
    /// on Face.GraphicsStyleId of each face. This handler therefore:
    /// scans the import for flat solids, keeps only faces whose
    /// Face.GraphicsStyleId resolves to the requested DWG layer (or all faces
    /// when no layer is given), extracts each face's boundary curve loop
    /// (tessellated), merges consecutive collinear edges into straight runs,
    /// and pairs runs of the same face as the two faces of a wall.
    /// Centerline pieces from different loops that are collinear (same
    /// circular-mean angle within tolerance, same perpendicular offset, small
    /// gap) merge independently, and duplicate outlines collapse. Thickness
    /// imposes the 2in..max wall band. Piece thickness maps to the nearest
    /// Revit wall type by compound width. Short centerlines are rejected as
    /// jamb linework; door-swing arcs optionally reject short centerlines
    /// near detected jambs.
    /// </summary>
    public class CreateWallsFromDwgPocheEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        private readonly ManualResetEvent _resetEvent = new ManualResetEvent(false);

        public string DwgNameOrId { get; set; }
        public string PocheLayer { get; set; } = "";
        public double HeightFt { get; set; } = 10.0;
        public double MaxWallThicknessFt { get; set; } = 3.0;
        public double MinWallLengthFt { get; set; } = 2.0;
        public string WallTypeName { get; set; }
        public bool ExcludeDoorArcs { get; set; } = true;
        public long LevelId { get; set; } = -1;
        public int MaxWalls { get; set; } = 200;

        // Tunables (internal; revisit per-DWG if needed).
        private const double MinWallThicknessFt = 2.0 / 12.0; // 2"
        private const double RunAngleTolDeg = 2.0;             // consecutive-edge collinearity in a loop
        private const double RailTolFt = 0.05;                 // collinear offset tolerance
        private const double PairAngleTolDeg = 2.0;            // face pairing parallelism
        private const double MinOverlapFrac = 0.7;
        private const double ClusterAngleTolRad = 2.0 * Math.PI / 180.0; // piece clustering
        private const double MergeGapFt = 0.5;                 // silent merge gap (drafting slop)
        private const double LoopCloseTolFt = 0.01;
        private const double MinRunFt = 0.3;                   // debris threshold for a face run
        private const double JambSnapFt = 0.35;

        // Opening bridging: walls are placed continuous across door openings
        // (doors get inserted later and cut real openings). A collinear gap on
        // the same rail up to MaxOpeningGapFt (doors incl. jamb trim; large
        // double doors) is bridged when both flanking pieces agree on measured
        // thickness AND a short perpendicular jamb run (the opening face)
        // brackets the gap. Gaps without jamb evidence stay separate.
        private const double MaxOpeningGapFt = 8.0;   // max bridged door opening
        private const double BridgeThicknessTolFt = 0.05; // same-wall thickness agreement
        private const double JambRatio = 0.35;        // short run vs neighbor lengths
        private const double JambPerpMinDeg = 60.0;   // jamb-vs-neighbor perpendicularity floor
        private const double JambSiblingParallelTolDeg = 10.0; // sibling runs face each other
        private const double JambSiblingLenTol = 0.25;  // near-equal lengths (both span the band)
        private const double BridgeJambSnapFt = 0.6;  // jamb midpoint may sit near a gap edge
        private const double PocheProximityFt = 0.15; // midline may sit this far outside a hatch patch (multi-wythe seam)
        private const double PocketSlotDepthFt = 0.5; // pocket strips are thinner than this
        private const double SliverRailTolFt = 0.1;   // sliver rail may sit this far outside the host band edge

        public object Result { get; private set; }

        public bool WaitForCompletion(int timeoutMilliseconds = 180000)
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

                if (string.IsNullOrWhiteSpace(DwgNameOrId))
                {
                    Result = new Dictionary<string, object> { ["error"] = "dwgNameOrId is required" };
                    return;
                }

                var target = DwgCurveSource.ResolveDwg(doc, DwgNameOrId.Trim(), out string targetName, out string targetKind);
                if (target == null)
                {
                    Result = new Dictionary<string, object> { ["error"] = $"No imported or linked DWG matching '{DwgNameOrId}'" };
                    return;
                }

                string layerFilter = string.IsNullOrWhiteSpace(PocheLayer) ? null : PocheLayer.Trim();

                long harvestMs = 0, pipelineMs = 0, buildingMs = 0;

                int flatSolids = 0, facesInspected = 0, facesKept = 0, degenerateFaces = 0;
                // Grouped by source face: each face contributes its complete
                // ring set (outer ring + interior hole rings). Holes matter -
                // even-odd containment treats a chase/shaft cavity as empty
                // space, so faces across a cavity never pair into phantom
                // walls.
                var faceGroups = new List<List<List<XYZ>>>();
                var loopSeen = new HashSet<string>();
                var layerHistogram = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                var zValues = new List<double>();

                // Tessellation of one boundary curve loop into a deduped point
                // ring (no repeated closing vertex).
                void Tessellate(CurveLoop cl2, List<XYZ> pts)
                {
                    foreach (var cv in cl2)
                    {
                        Line ln = cv as Line;
                        if (ln != null)
                        {
                            pts.Add(ln.GetEndPoint(0));
                            continue;
                        }
                        foreach (var p in cv.Tessellate())
                            pts.Add(p);
                    }
                }

                // Depth-capped walk (see DwgCurveSource.WalkGeometry for the
                // stack-overflow rationale): flat solids are visited, faces
                // grouped per source solid.
                var walkStats = new DwgCurveSource.GeometryWalkStats();

                Action<GeometryObject> visit = o =>
                {
                    var solid = o as Solid;
                    if (solid == null || solid.Volume >= 1e-6) return;
                    flatSolids++;

                    foreach (Face f in solid.Faces)
                    {
                        var gs3 = doc.GetElement(f.GraphicsStyleId) as GraphicsStyle;
                        string layerName = gs3 != null && gs3.GraphicsStyleCategory != null
                            ? gs3.GraphicsStyleCategory.Name : "";
                        facesInspected++;
                        if (layerHistogram.ContainsKey(layerName)) layerHistogram[layerName]++; else layerHistogram[layerName] = 1;
                        if (layerFilter != null && !string.Equals(layerName, layerFilter, StringComparison.OrdinalIgnoreCase))
                            continue;

                        // Only planar faces carry a clean outline.
                        var pf = f as PlanarFace;
                        if (pf == null) { degenerateFaces++; continue; }

                        var loopsOfFace = pf.GetEdgesAsCurveLoops();
                        var faceRings = new List<List<XYZ>>();
                        foreach (var loopDirs in loopsOfFace)
                        {
                            var tess = new List<XYZ>();
                            Tessellate(loopDirs, tess);
                            if (tess.Count < 3) { degenerateFaces++; continue; }

                            // Dedupe consecutive, drop the repeated closing vertex.
                            var vs = new List<XYZ>();
                            foreach (var p in tess)
                            {
                                if (vs.Count > 0 && vs[vs.Count - 1].DistanceTo(p) < 0.01) continue;
                                vs.Add(p);
                            }
                            if (vs.Count >= 2 && vs[0].DistanceTo(vs[vs.Count - 1]) < 0.01) vs.RemoveAt(vs.Count - 1);
                            if (vs.Count < 3) { degenerateFaces++; continue; }

                            // Duplicate-outline collapse: top and bottom
                            // faces of one flat solid share the same outline
                            // signature.
                            string sig = string.Join(";", vs.Select(p =>
                                Math.Round(p.X * 64.0) + "," + Math.Round(p.Y * 64.0)));
                            if (loopSeen.Contains(sig)) continue;
                            loopSeen.Add(sig);

                            facesKept++;
                            foreach (var p in vs) zValues.Add(p.Z);
                            faceRings.Add(vs);
                        }
                        if (faceRings.Count > 0) faceGroups.Add(faceRings);
                    }
                };

                var geo = target.get_Geometry(new Options());
                var swHarvest = System.Diagnostics.Stopwatch.StartNew();
                DwgCurveSource.WalkGeometry(geo, visit, walkStats);
                harvestMs = swHarvest.ElapsedMilliseconds;

                if (faceGroups.Count == 0)
                {
                    Result = new Dictionary<string, object>
                    {
                        ["error"] = layerFilter == null
                            ? "No flat hatch-fill solids found in the DWG"
                            : $"No flat hatch-fill faces found on layer '{PocheLayer}' of '{targetName}'",
                        ["flatSolids"] = flatSolids,
                        ["facesInspected"] = facesInspected,
                        ["faceLayerHistogram"] = layerHistogram
                    };
                    return;
                }

                // ---- geometry pipeline (pure core, unit-tested) ----
                var geoLoops = faceGroups
                    .Select(g => g.Select(l => l.Select(p => new Pt(p.X, p.Y)).ToList()).ToList())
                    .ToList();
                var gopt = new PochePipelineOptions { MaxWallThicknessFt = MaxWallThicknessFt };
                var swPipeline = System.Diagnostics.Stopwatch.StartNew();
                var pipe = PocheGeometryCore.RunPipeline(geoLoops, gopt);
                pipelineMs = swPipeline.ElapsedMilliseconds;
                var rejects = pipe.RejectLog.Entries;
                var stageCounts = pipe.RejectLog.StageCounts;
                int straightRuns = pipe.StraightRuns;
                int pairsNoThickness = pipe.PairsNoThickness;
                int pairsNoOverlap = pipe.PairsNoOverlap;
                int pairsMade = pipe.FacePairs;
                int jambCandidates = pipe.JambCandidates;
                int pairsOutsidePoche = pipe.PairsOutsidePoche;
                int pairsInsidePoche = pipe.PairsInsidePoche;
                int sliversCulled = pipe.SliversCulled;
                var bridgeStats = pipe.Bridge;
                var merged = pipe.Merged;

                int wallsAfterMerge = merged.Count(p => p.Length >= MinWallLengthFt);

                // ---- level / wall types / build ----
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

                double z = 0;
                if (zValues.Count > 0)
                {
                    zValues.Sort();
                    z = zValues[zValues.Count / 2];
                }
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

                List<XYZ> jambPoints = null;
                List<Pt> jambPtPts = null;
                if (ExcludeDoorArcs)
                {
                    jambPoints = CollectDoorJambPoints(target, doc);
                    jambPtPts = jambPoints.Select(p => new Pt(p.X, p.Y)).ToList();
                }

                var preprocessor = new SilentWarningsPreprocessor();
                var createdIds = new List<long>();
                var typeSummary = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                int created = 0, rejectedJamb = 0, doorArcRejected = 0, buildFailed = 0, joinRetries = 0;
                var buildFailures = new List<string>();
                var swTotal = System.Diagnostics.Stopwatch.StartNew();

                // Debug trace target (mirrors mcp_resolver_debug.log).
                // Fallback to %APPDATA% because writes to the Revit process's
                // temp have been observed to silently fail.
                string swoLogPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "mcp_poche_debug.log");
                string swoFallbackPath = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "mcp_poche_debug.log");
                Action<string> swo = msg =>
                {
                    try { System.IO.File.AppendAllText(swoLogPath, string.Format("[{0:HH:mm:ss.fff}] {1}\n", DateTime.Now, msg)); }
                    catch { try { System.IO.File.AppendAllText(swoFallbackPath, string.Format("[{0:HH:mm:ss.fff}] {1}\n", DateTime.Now, msg)); } catch { } }
                };

                swo(string.Format("stage=setup loops={0} merged={1} harvestMs={2} pipelineMs={3}",
                    faceGroups.Count, merged.Count, harvestMs, pipelineMs));

                // Batch failure pipeline: the app-level FailuresProcessing delegate
                // deletes warnings and, on error-level failures ("can't make wall"),
                // forces silent rollback so no modal dialog can block the socket;
                // the offending per-wall SubTransaction dies alone and the batch
                // continues. NEVER use ProceedWithCommit here — that triggers an
                // infinite failure-processing loop / UI lock.
                var fpApp = doc.Application;
                EventHandler<Autodesk.Revit.DB.Events.FailuresProcessingEventArgs> fpHandler = (sender, args) =>
                {
                    try
                    {
                        var fa = args.GetFailuresAccessor();
                        bool hasError = false;
                        foreach (var msg in fa.GetFailureMessages())
                        {
                            var severity = msg.GetSeverity();
                            string desc = msg.GetDescriptionText();
                            if (severity == FailureSeverity.Warning)
                            {
                                fa.DeleteWarning(msg);
                            }
                            else if (severity == FailureSeverity.Error)
                            {
                                hasError = true;
                                buildFailures.Add("failureEvent: " + desc);
                                swo("failureEvent " + desc);
                            }
                        }
                        if (hasError)
                        {
                            buildFailed++;
                            args.SetProcessingResult(FailureProcessingResult.ProceedWithRollBack);
                        }
                    }
                    catch { }
                };
                fpApp.FailuresProcessing += fpHandler;
                var swBuild = System.Diagnostics.Stopwatch.StartNew();
                try
                {

                    foreach (var pair in merged)
                    {
                        if (created + buildFailed >= MaxWalls) break;

                        if (pair.Length < MinWallLengthFt)
                        {
                            rejectedJamb++;
                            pipe.RejectLog.Log("shortCenterline", new Dictionary<string, object>
                            {
                                ["len"] = Math.Round(pair.Length, 2),
                                ["thick"] = Math.Round(pair.Thickness, 2),
                                ["start"] = new[] { Math.Round(pair.Sx, 2), Math.Round(pair.Sy, 2) },
                                ["end"] = new[] { Math.Round(pair.Ex, 2), Math.Round(pair.Ey, 2) }
                            });
                            continue;
                        }

                        if (ExcludeDoorArcs && jambPoints != null &&
                            pair.Length < MinWallLengthFt * 2.0 &&
                            NearJamb(pair.Start, jambPtPts) && NearJamb(pair.End, jambPtPts))
                        {
                            doorArcRejected++;
                            pipe.RejectLog.Log("doorArc", new Dictionary<string, object>
                            {
                                ["len"] = Math.Round(pair.Length, 2),
                                ["thick"] = Math.Round(pair.Thickness, 2),
                                ["start"] = new[] { Math.Round(pair.Sx, 2), Math.Round(pair.Sy, 2) },
                                ["end"] = new[] { Math.Round(pair.Ex, 2), Math.Round(pair.Ey, 2) }
                            });
                            continue;
                        }

                        var wt = forcedType;
                        if (wt == null)
                            wt = wallTypes.OrderBy(w => Math.Abs(w.Width - pair.Thickness)).First();

                        try
                        {
                            // Per-wall transaction (off-axis fixer pattern):
                            // SubTransactions cannot take failure-handling
                            // options, so error-level failures ("can't make
                            // wall") escape to a modal that wedges the socket.
                            // A full transaction with forced modal handling
                            // disabled processes failures headlessly and rolls
                            // back only this wall.
                            // Attempt 0: normal (joins allowed). If the commit
                            // fails ("Can't keep elements joined"), attempt 1
                            // retries the same wall with wall joins disallowed
                            // so junction stubs still get built.
                            bool committed = false;
                            for (int attempt = 0; attempt < 2 && !committed; attempt++)
                            {
                                using (var wallTrans = new Transaction(doc, attempt == 0 ? "Poche wall" : "Poche wall (no join)"))
                                {
                                    var wfo = wallTrans.GetFailureHandlingOptions();
                                    wfo.SetFailuresPreprocessor(preprocessor);
                                    wfo.SetClearAfterRollback(true);
                                    wfo.SetForcedModalHandling(false);
                                    wallTrans.SetFailureHandlingOptions(wfo);
                                    wallTrans.Start();
                                    try
                                    {
                                        var centerLine = Line.CreateBound(new XYZ(pair.Sx, pair.Sy, 0), new XYZ(pair.Ex, pair.Ey, 0));
                                        var wall = Wall.Create(doc, centerLine, wt.Id, level.Id, HeightFt, 0, false, false);
                                        if (attempt == 1 && wall != null)
                                        {
                                            WallUtils.DisallowWallJoinAtEnd(wall, 0);
                                            WallUtils.DisallowWallJoinAtEnd(wall, 1);
                                        }
                                        var status = wallTrans.Commit();
                                        if (status == TransactionStatus.Committed && wall != null)
                                        {
                                            createdIds.Add(DwgCurveSource.IdValue(wall));
                                            created++;
                                            if (attempt == 1) joinRetries++;
                                            string tn2 = wt.Name;
                                            if (typeSummary.ContainsKey(tn2)) typeSummary[tn2]++;
                                            else typeSummary[tn2] = 1;
                                            committed = true;
                                        }
                                        else if (attempt == 1)
                                        {
                                            buildFailed++;
                                            buildFailures.Add(string.Format("({0},{1})-({2},{3}) t={4}: status={5}",
                                                Math.Round(pair.Sx,2), Math.Round(pair.Sy,2),
                                                Math.Round(pair.Ex,2), Math.Round(pair.Ey,2),
                                                Math.Round(pair.Thickness,2), status));
                                            swo(string.Format("buildFail len={0} status={1}", Math.Round(pair.Length,2), status));
                                        }
                                    }
                                    catch (Exception bex)
                                    {
                                        if (attempt == 1)
                                        {
                                            buildFailed++;
                                            buildFailures.Add(string.Format("({0},{1})-({2},{3}) t={4}: {5}",
                                                Math.Round(pair.Sx,2), Math.Round(pair.Sy,2),
                                                Math.Round(pair.Ex,2), Math.Round(pair.Ey,2),
                                                Math.Round(pair.Thickness,2), bex.Message));
                                            swo(string.Format("buildFail len={0} {1}", Math.Round(pair.Length,2), buildFailures[buildFailures.Count-1]));
                                        }
                                    }
                                }
                            }
                        }
                        catch { buildFailed++; }
                    }

                    buildingMs = swBuild.ElapsedMilliseconds;
                }
                finally
                {
                    fpApp.FailuresProcessing -= fpHandler;
                }
                swo(string.Format("stage=done created={0} buildFailed={1} jambRej={2} arcRej={3} buildMs={4} totalMs={5}",
                    created, buildFailed, rejectedJamb, doorArcRejected, buildingMs, swTotal.ElapsedMilliseconds));

                Result = new Dictionary<string, object>
                {
                    ["source"] = new Dictionary<string, object>
                    {
                        ["id"] = DwgCurveSource.IdValue(target),
                        ["name"] = targetName,
                        ["kind"] = targetKind
                    },
                    ["pocheLayer"] = layerFilter ?? "(all layers)",
                    ["level"] = new Dictionary<string, object> { ["id"] = DwgCurveSource.IdValue(level), ["name"] = level.Name },
                    ["heightFt"] = HeightFt,
                    ["flatSolids"] = flatSolids,
                    ["facesInspected"] = facesInspected,
                    ["facesKept"] = facesKept,
                    ["degenerateFaces"] = degenerateFaces,
                    ["faceLayerHistogram"] = layerHistogram,
                    ["closedFaces"] = faceGroups.Count,
                    ["closedLoops"] = pipe.ClosedLoops,
                    ["straightRuns"] = straightRuns,
                    ["jambCandidates"] = jambCandidates,
                    ["facePairs"] = pairsMade,
                    ["rejectedPairsThickness"] = pairsNoThickness,
                    ["rejectedPairsOverlap"] = pairsNoOverlap,
                    ["mergedCenterlines"] = merged.Count,
                    ["wallsPotential"] = wallsAfterMerge,
                    ["dedupFragmentsCulled"] = pipe.DedupFragmentsCulled,
                    ["dedupBandsCulled"] = pipe.DedupBandsCulled,
                    ["dedupStubsCulled"] = pipe.DedupStubsCulled,
                    ["dedupStripsCulled"] = pipe.DedupStripsCulled,
                    ["dedupAbsorbed"] = pipe.DedupAbsorbed,
                    ["extendsDone"] = pipe.ExtendsDone,
                    ["bridgedOpenings"] = bridgeStats.Bridged,
                    ["bridgedJunctionEvidence"] = bridgeStats.BridgedEvidence,
                    ["unbridgedGaps"] = bridgeStats.Unbridged,
                    ["straightRuns"] = straightRuns,
                    ["facePairs"] = pairsMade,
                    ["rejectedPairsThickness"] = pairsNoThickness,
                    ["rejectedPairsOverlap"] = pairsNoOverlap,
                    ["pairsInsidePoche"] = pairsInsidePoche,
                    ["pairsOutsidePoche"] = pairsOutsidePoche,
                    ["sliversCulled"] = sliversCulled,
                    ["rejectStageCounts"] = stageCounts,
                    ["mergedCenterlines"] = merged.Count,
                    ["wallsCreated"] = created,
                    ["rejectedJamb"] = rejectedJamb,
                ["doorArcRejected"] = doorArcRejected,
                    ["buildFailed"] = buildFailed,
                    ["joinRetries"] = joinRetries,
                    ["buildFailures"] = buildFailures,
                    ["revitFailureLog"] = preprocessor.Log,
                    ["harvestMs"] = harvestMs,
                    ["depthCapped"] = walkStats.DepthCapped,
                    ["pipelineMs"] = pipelineMs,
                    ["buildingMs"] = buildingMs,
                    ["minWallLengthFt"] = MinWallLengthFt,
                ["typeSummary"] = typeSummary,
                ["createdIds"] = createdIds,
                ["rejects"] = rejects,
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

        private static bool NearJamb(Pt p, List<Pt> jambPoints)
        {
            foreach (var jp in jambPoints)
            {
                if (jp.Dist(p) <= JambSnapFt) return true;
            }
            return false;
        }

        public string GetName()
        {
            return "Create Walls from DWG Poche";
        }
    }
}


