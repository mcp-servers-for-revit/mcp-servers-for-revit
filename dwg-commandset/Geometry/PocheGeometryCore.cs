using System;
using System.Collections.Generic;
using System.Linq;

namespace RevitMCPCommandSet.Geometry
{
    /// <summary>
    /// Pure 2D geometry core of create_walls_from_dwg_poche. No Revit API
    /// types: rings and midlines are plain point pairs and arrays, so the
    /// pipeline is unit-testable outside Revit (TUnit / CI). The Revit
    /// handler harvests hatch-face boundary rings (List&lt;List&lt;XYZ&gt;&gt;), feeds
    /// them here, then maps the resulting wall pairs onto Revit walls.
    /// Behaviour is a 1:1 port from the handler at snapshot
    /// dwg-snapshot-pre-geometry-refactor. All units in feet.
    /// </summary>
    public struct Pt
    {
        public double X, Y;
        public Pt(double x, double y) { X = x; Y = y; }
        public static Pt operator +(Pt a, Pt b) { return new Pt(a.X + b.X, a.Y + b.Y); }
        public static Pt operator -(Pt a, Pt b) { return new Pt(a.X - b.X, a.Y - b.Y); }
        public static Pt operator *(Pt a, double s) { return new Pt(a.X * s, a.Y * s); }
        public double Dot(Pt b) { return X * b.X + Y * b.Y; }
        public double Dist(Pt b) { double dx = X - b.X, dy = Y - b.Y; return Math.Sqrt(dx * dx + dy * dy); }
        public double Len() { return Math.Sqrt(X * X + Y * Y); }
    }

    public class BridgeStatsCore
    {
        public int Silent;
        public int Bridged;
        public int BridgedEvidence;
        public int Unbridged;
    }

    /// <summary>
    /// Culled-candidate diagnostics: first 600 entries with per-stage cap of
    /// 150, each tagged by pipeline stage with rounded geometry.
    /// </summary>
    public class RejectLog
    {
        public const int TotalCap = 600;
        public const int PerStageCap = 150;
        private int _total;
        public List<Dictionary<string, object>> Entries = new List<Dictionary<string, object>>();
        public Dictionary<string, int> StageCounts = new Dictionary<string, int>();

        public void Log(string stage, Dictionary<string, object> fields)
        {
            if (!StageCounts.ContainsKey(stage)) StageCounts[stage] = 0;
            StageCounts[stage]++;
            if (StageCounts[stage] > PerStageCap || _total >= TotalCap) return;
            fields["stage"] = stage;
            Entries.Add(fields);
            _total++;
        }
    }

    public class WallPairCore
    {
        public double Sx, Sy, Ex, Ey, Thickness;
        public double Length
        {
            get { double dx = Ex - Sx, dy = Ey - Sy; return Math.Sqrt(dx * dx + dy * dy); }
        }
        public Pt Start { get { return new Pt(Sx, Sy); } }
        public Pt End { get { return new Pt(Ex, Ey); } }
    }

    public class PochePipelineOptions
    {
        public double MinWallThicknessFt = 2.0 / 12.0; // 2"
        public double MaxWallThicknessFt = 5.0;
        public double MinRunFt = 0.3;
        public double RunAngleTolDeg = 2.0;
        public double RailTolFt = 0.05;
        public double PairAngleTolDeg = 2.0;
        public double MinOverlapFrac = 0.7;
        public double ClusterAngleTolRad = 2.0 * Math.PI / 180.0;
        public double MergeGapFt = 3.0;                 // silent same-rail chain gap (drafting slop); larger gaps need bridge evidence
        public double MaxOpeningGapFt = 8.0;
        public double BridgeThicknessTolFt = 0.05;
        public double JambPerpMinDeg = 60.0;
        public double JambSiblingParallelTolDeg = 10.0;
        public double JambSiblingLenTol = 0.25;
        public double BridgeJambSnapFt = 0.6;
        public double PocheProximityFt = 0.15;
        public double PocketSlotDepthFt = 0.5;
        public double SliverRailTolFt = 0.1;

        // ---- post-merge cleanup pass (junction duplicates & cap stubs) ----
        public double DedupAngleDot = 0.9962;          // ~5 deg near-parallel gate
        public double DedupFragPerpFt = 0.2;            // same-rail fragment cull rail offset
        public double DedupFragThickFt = 0.15;         // same-rail fragment cull thickness delta
        public double DedupBandMinThickDelta = 0.4;    // thick junction band: min thickness excess over keeper
        public double DedupRailCullTolFt = 1.25;       // thick junction band: max rail offset from keeper
        public double DedupCullOverlapFrac = 0.5;       // along-overlap fraction of the shorter wall
        public double DedupMergePerpFt = 0.35;         // merge: rail offset bound
        public double DedupMergeGapFt = 0.05;          // merge: max along gap (overlap/abut only - real walls can sit 3+ ft apart on one rail)

        // ---- pocket-door strip rule ----
        // Domain fact: the only intentional sub-6in gaps between parallel
        // rails are pocket-door channels (~4in x door-width cut into the
        // wall band). Pairing across the channel edges yields thin "slot
        // strip" walls hugging the real band wall; their area is already
        // inside the band wall, so they are culled as redundant.
        public double DedupStripRailTolFt = 0.05;      // strip rail may sit this far outside the mate's band edge
        public double DedupStripOverlapFrac = 0.7;     // strip span must overlap the mate by this fraction of the shorter
        public double DedupStripMinThickDelta = 0.02;   // mate must be thicker than the strip by this much
        public double DedupStripMinLenRatio = 1.05;     // mate must be clearly longer - protects equal-length real wall pairs
        public double StubMaxLenFt = 3.5;              // crossing stub: max length
        public double StubMinThickFt = 2.5;            // crossing stub: min thickness
        public double StubCrossMarginFt = 0.2;         // crossing must be this far inside both spans
        public double StubCrossMaxDot = 0.35;          // crossing stub is near-perpendicular

        // ---- end extension (snap ends to crossing walls' rails) ----
        public double ExtMaxFt = 4.0;                  // max extension distance per end
        public double ExtCrossMaxDot = 0.5;            // crossing wall is meaningfully non-parallel
        public double ExtCrossSpanMarginFt = 0.5;      // crossing wall must span the junction by this margin
        public double ExtCrossMinLenFt = 1.5;         // ignore stub walls as extension targets
        public double ExtMaxThickFt = 2.0;             // thick walls (junction bands) don't extend
        public double ExtPocheTolFt = 0.35;            // extension segment must stay inside hatch (sampled)

        // ---- bridge evidence beyond jamb runs ----
        public double BridgeCrossMaxDot = 0.35;       // crossing piece is near-perpendicular to the rail
        public double BridgeCrossSnapFt = 0.75;       // crossing point may sit past the gap edges by this much
        public bool EnableJunctionEvidenceBridge = false;    // A/B verdict: fixes T-junction continuity (run 11: recall +0.011) but chains noise-band fragments (precision -0.027, F1 -0.009); run 9 preferred on test.dwg - available for drawings that value continuity
        public bool EnableEndExtension = false;        // A/B: end extension was recall/precision neutral on test.dwg (run 6c/6d vs 6a); kept for drawings with rail-terminated conventions
    }

    public class PochePipelineResult
    {
        public int ClosedLoops;
        public int StraightRuns;
        public int JambCandidates;
        public int FacePairs;
        public int PairsNoThickness;
        public int PairsNoOverlap;
        public int PairsInsidePoche;
        public int PairsOutsidePoche;
        public int SliversCulled;
        public int MergedCenterlines;
        public int DedupFragmentsCulled;
        public int DedupBandsCulled;
        public int DedupStubsCulled;
        public int DedupStripsCulled;
        public int DedupAbsorbed;
        public int ExtendsDone;
        public List<WallPairCore> Merged = new List<WallPairCore>();
        public List<WallPairCore> MergedAll = new List<WallPairCore>();
        public List<Pt> JambMidpoints = new List<Pt>();
        public BridgeStatsCore Bridge = new BridgeStatsCore();
        public RejectLog RejectLog = new RejectLog();
        internal List<double[]> CenterlinePieces = new List<double[]>();
    }

    public static class PocheGeometryCore
    {
        internal static double R2(double v) { return Math.Round(v, 2); }

        internal static double[] P2(Pt p) { return new[] { Math.Round(p.X, 2), Math.Round(p.Y, 2) }; }

        /// <summary>
        /// Full geometry pipeline over hatch faces. Each face is the complete
        /// ring set of one hatch patch (outer ring plus interior hole rings);
        /// holes matter: a point inside a hole is NOT inside the pochte
        /// (chase/shaft cavities), which the pairing containment relies on.
        /// </summary>
        public static PochePipelineResult RunPipeline(List<List<List<Pt>>> faces, PochePipelineOptions opt)
        {
            if (faces == null) throw new ArgumentNullException("faces");
            if (opt == null) throw new ArgumentNullException("opt");
            var res = new PochePipelineResult { ClosedLoops = faces.Sum(f => f.Count) };

            foreach (var face in faces)
            {
                // ---- straight runs: consecutive collinear edges, collected
                // across ALL rings of the face (outer ring + interior hole
                // rings). A wall around a cavity typically pairs the outer
                // ring's edge with the hole ring's edge - both belong to one
                // face, so pairing operates on the face-combined run set.
                var runs = new List<(Pt s, Pt e, double len)>();
                foreach (var vs in face)
                {
                    int n = vs.Count;
                    if (n < 3) continue;

                    int i = 0;
                    while (i < n)
                    {
                        Pt d0 = vs[(i + 1) % n] - vs[i];
                        double L0 = d0.Len();
                        if (L0 < 1e-9) { i++; continue; }
                        Pt dirCur = d0 * (1.0 / L0);
                        double lenCur = L0;
                        int k = i;
                        while (k + 1 < n)
                        {
                            Pt n0 = vs[k + 1], n1 = vs[(k + 2) % n];
                            Pt dN = n1 - n0;
                            double LN = dN.Len();
                            if (LN < 1e-9) break;
                            Pt uN = dN * (1.0 / LN);
                            double dotAbs = Math.Abs(dirCur.Dot(uN));
                            double angDev = Math.Acos(Math.Min(1.0, dotAbs)) * 180.0 / Math.PI;
                            bool sameDir = angDev <= opt.RunAngleTolDeg;
                            if (sameDir)
                            {
                                var vv = n0 - vs[i];
                                var perp = vv - dirCur * vv.Dot(dirCur);
                                if (perp.Len() > opt.RailTolFt) sameDir = false;
                            }
                            if (!sameDir) break;
                            if (dirCur.Dot(uN) < 0) dirCur = new Pt(-dirCur.X, -dirCur.Y);
                            lenCur += LN;
                            k++;
                        }
                        if (lenCur >= opt.MinRunFt)
                        {
                            runs.Add((vs[i], vs[(k + 1) % n], lenCur));
                            res.StraightRuns++;
                        }
                        i = k + 1;
                    }
                }

                // ---- jamb classification (sibling test) ----
                int runCount = runs.Count;
                if (runCount >= 3)
                {
                    for (int j = 0; j < runCount; j++)
                    {
                        var prevR = runs[(j + runCount - 1) % runCount];
                        var nextR = runs[(j + 1) % runCount];
                        var dj = runs[j].e - runs[j].s;
                        var dp = prevR.e - prevR.s;
                        var dn = nextR.e - nextR.s;
                        double lj = dj.Len(), lp = dp.Len(), ln2 = dn.Len();
                        if (lj < 1e-9 || lp < 1e-9 || ln2 < 1e-9) continue;
                        double angP = Math.Acos(Math.Min(1.0, Math.Abs(dj.Dot(dp) / (lj * lp)))) * 180.0 / Math.PI;
                        double angN = Math.Acos(Math.Min(1.0, Math.Abs(dj.Dot(dn) / (lj * ln2)))) * 180.0 / Math.PI;
                        if (angP < opt.JambPerpMinDeg || angN < opt.JambPerpMinDeg) continue;

                        // Sibling test: a jamb is one of a PAIR - door
                        // openings produce two perpendicular short runs facing
                        // each other across the opening (parallel, near-equal
                        // length, within MaxOpeningGapFt).
                        var mj = (runs[j].s + runs[j].e) * 0.5;
                        bool hasSibling = false;
                        for (int s2 = 0; s2 < runCount && !hasSibling; s2++)
                        {
                            if (s2 == j) continue;
                            var ds2 = runs[s2].e - runs[s2].s;
                            double ls2 = ds2.Len();
                            if (ls2 < 1e-9) continue;
                            double parallelDev = Math.Acos(Math.Min(1.0, Math.Abs(dj.Dot(ds2) / (lj * ls2)))) * 180.0 / Math.PI;
                            if (parallelDev > opt.JambSiblingParallelTolDeg) continue;
                            double lenDev = Math.Abs(ls2 - lj);
                            if (lenDev > opt.JambSiblingLenTol * Math.Max(lj, ls2)) continue;
                            var ms = (runs[s2].s + runs[s2].e) * 0.5;
                            if (mj.Dist(ms) > opt.MaxOpeningGapFt) continue;
                            hasSibling = true;
                        }
                        if (!hasSibling) continue;
                        res.JambCandidates++;
                        res.JambMidpoints.Add(mj);
                    }
                }

                // ---- face pairing within the loop ----
                var runMatched = new bool[runCount];
                for (int a = 0; a < runCount; a++)
                {
                    var sa = runs[a].s;
                    var ea = runs[a].e;
                    var da = ea - sa;
                    double la = da.Len();
                    if (la < 1e-9) continue;
                    var ua = da * (1.0 / la);

                    for (int b = a + 1; b < runCount; b++)
                    {
                        var sb = runs[b].s;
                        var eb = runs[b].e;
                        var db = eb - sb;
                        double lb = db.Len();
                        if (lb < 1e-9) continue;
                        var ub = db * (1.0 / lb);

                        double dotAbs2 = Math.Abs(ua.Dot(ub));
                        double dev2 = Math.Acos(Math.Min(1.0, dotAbs2)) * 180.0 / Math.PI;
                        if (dev2 > opt.PairAngleTolDeg) continue;

                        var mb = (sb + eb) * 0.5;
                        var vv = mb - sa;
                        var perp = vv - ua * vv.Dot(ua);
                        double thick = perp.Len();
                        if (thick < opt.MinWallThicknessFt || thick > opt.MaxWallThicknessFt)
                        {
                            res.PairsNoThickness++;
                            res.RejectLog.Log("pairThickness", new Dictionary<string, object>
                            {
                                ["lenA"] = R2(la), ["lenB"] = R2(lb), ["thick"] = R2(thick),
                                ["aStart"] = P2(sa), ["aEnd"] = P2(ea),
                                ["bStart"] = P2(sb), ["bEnd"] = P2(eb)
                            });
                            continue;
                        }

                        double tb0 = (sb - sa).Dot(ua) / la;
                        double tb1 = (eb - sa).Dot(ua) / la;
                        double lo = Math.Max(0.0, Math.Min(tb0, tb1));
                        double hi = Math.Min(1.0, Math.Max(tb0, tb1));
                        if (hi <= lo) continue;
                        double overlapLen = (hi - lo) * la;
                        double minLen = Math.Min(la, lb);
                        if (overlapLen / minLen < opt.MinOverlapFrac)
                        {
                            res.PairsNoOverlap++;
                            res.RejectLog.Log("pairOverlap", new Dictionary<string, object>
                            {
                                ["lenA"] = R2(la), ["lenB"] = R2(lb), ["thick"] = R2(thick),
                                ["overlapFrac"] = R2(overlapLen / minLen),
                                ["aStart"] = P2(sa), ["aEnd"] = P2(ea),
                                ["bStart"] = P2(sb), ["bEnd"] = P2(eb)
                            });
                            continue;
                        }

                        var pStart = sa + da * lo;
                        var projS = sb + ub * (pStart - sb).Dot(ub);
                        var c0 = (pStart + projS) * 0.5;
                        var pEnd = sa + da * hi;
                        var projE = sb + ub * (pEnd - sb).Dot(ub);
                        var c1 = (pEnd + projE) * 0.5;

                        var mid = (c0 + c1) * 0.5;
                        if (!InsideAnyPocheFaces(mid, faces, opt.PocheProximityFt))
                        {
                            res.PairsOutsidePoche++;
                            res.RejectLog.Log("outsidePoche", new Dictionary<string, object>
                            {
                                ["lenA"] = R2(la), ["lenB"] = R2(lb), ["thick"] = R2(thick),
                                ["mid"] = P2(mid),
                                ["aStart"] = P2(sa), ["aEnd"] = P2(ea),
                                ["bStart"] = P2(sb), ["bEnd"] = P2(eb)
                            });
                            continue;
                        }
                        res.PairsInsidePoche++;

                        res.CenterlinePieces.Add(new[] { c0.X, c0.Y, c1.X, c1.Y, thick, dev2 });
                        res.FacePairs++;
                        runMatched[a] = true;
                        runMatched[b] = true;
                    }
                }

                // Runs with no partner at all: pure linework / unpaired faces.
                for (int a = 0; a < runCount; a++)
                {
                    if (runMatched[a]) continue;
                    var ra = runs[a];
                    var dir = ra.e - ra.s;
                    double rl = dir.Len();
                    if (rl < 1e-9) continue;
                    var u2 = dir * (1.0 / rl);
                    var mid = (ra.s + ra.e) * 0.5;
                    var nrm = new Pt(-u2.Y, u2.X);
                    res.RejectLog.Log("unpairedRun", new Dictionary<string, object>
                    {
                        ["len"] = R2(ra.len),
                        ["mid"] = P2(mid),
                        ["probe"] = P2(mid + nrm * Math.Min(opt.MaxWallThicknessFt, 1.0))
                    });
                }
            }

            // ---- independent collinear merge (with opening bridging) ----
            res.Merged = MergeCollinear(res.CenterlinePieces, res.JambMidpoints, res.Bridge, opt, res.RejectLog, faces);
            res.MergedAll = new List<WallPairCore>(res.Merged);

            // ---- pocket-door slot filter (nested sliver pairs) ----
            var kept = new List<WallPairCore>();
            foreach (var p in res.Merged)
            {
                if (IsNestedSliver(p, res.Merged, opt))
                {
                    res.SliversCulled++;
                    res.RejectLog.Log("nestedSliver", new Dictionary<string, object>
                    {
                        ["thick"] = R2(p.Thickness),
                        ["start"] = new[] { Math.Round(p.Sx, 2), Math.Round(p.Sy, 2) },
                        ["end"] = new[] { Math.Round(p.Ex, 2), Math.Round(p.Ey, 2) }
                    });
                    continue;
                }
                kept.Add(p);
            }
            res.Merged = kept;

            // ---- post-merge cleanup: junction duplicates, thick edge bands,
            // crossing cap stubs; same-rail fragments merge or cull ----
            var dedup = new DedupStats();
            res.Merged = DedupAndClean(res.Merged, opt, res.RejectLog, dedup);
            res.DedupFragmentsCulled = dedup.Fragments;
            res.DedupBandsCulled = dedup.Bands;
            res.DedupStubsCulled = dedup.Stubs;
            res.DedupStripsCulled = dedup.Strips;
            res.DedupAbsorbed = dedup.Absorbed;

            // ---- end extension: snap wall ends outward to the rail of the
            // nearest crossing wall (reference convention: walls end on
            // crossing walls' rails) ----
            int extended = 0;
            if (opt.EnableEndExtension)
            {
                res.Merged = ExtendEnds(res.Merged, opt, res.RejectLog, faces, out extended);
            }
            res.ExtendsDone = extended;

            res.MergedCenterlines = res.Merged.Count;
            return res;
        }

        /// <summary>
        /// Counters for the post-merge cleanup pass.
        /// </summary>
        public class DedupStats
        {
            public int Fragments;
            public int Bands;
            public int Stubs;
            public int Strips;
            public int Absorbed;
        }

        /// <summary>
        /// Cross-loop collinear merge of centerline pieces. Loops are needed
        /// for evidence-gated bridging (gap midpoint inside a hatch patch).
        /// </summary>
        public static List<WallPairCore> MergeCollinear(List<double[]> pieces, List<Pt> jambMidpoints,
            BridgeStatsCore stats, PochePipelineOptions opt, RejectLog rejects, List<List<List<Pt>>> faces)
        {
            var wallPairs = new List<WallPairCore>();
            var items = pieces
                .Select(p => new
                {
                    ang = Math.Atan2(p[3] - p[1], p[2] - p[0]) < 0
                        ? Math.Atan2(p[3] - p[1], p[2] - p[0]) + Math.PI
                        : Math.Atan2(p[3] - p[1], p[2] - p[0]),
                    p // [sX, sY, eX, eY, thick, dev]
                })
                .OrderBy(it => it.ang)
                .ToList();

            var clusters = new List<List<double[]>>();
            var curCluster = new List<double[]>();
            double curAng = -1;
            foreach (var it in items)
            {
                if (curCluster.Count == 0 || it.ang - curAng <= opt.ClusterAngleTolRad)
                {
                    curCluster.Add(it.p);
                    if (curCluster.Count == 1) curAng = it.ang;
                }
                else
                {
                    clusters.Add(curCluster);
                    curCluster = new List<double[]> { it.p };
                    curAng = it.ang;
                }
            }
            if (curCluster.Count > 0) clusters.Add(curCluster);

            // wrap merge first/last clusters (angles are in [0, PI); a first
            // cluster near 0 and a last cluster near PI wrap together).
            if (clusters.Count > 1)
            {
                var aF = clusters[0][0];
                var aL = clusters[clusters.Count - 1][clusters[clusters.Count - 1].Count - 1];
                double aFv = Math.Atan2(aF[3] - aF[1], aF[2] - aF[0]);
                double aLv = Math.Atan2(aL[3] - aL[1], aL[2] - aL[0]);
                double span = (aFv + Math.PI) - aLv;
                if (span <= opt.ClusterAngleTolRad)
                {
                    clusters[clusters.Count - 1].AddRange(clusters[0]);
                    clusters.RemoveAt(0);
                }
            }

            var railsOut = new List<double[]>(); // v(0), u0(1), u1(2), thick(3)
            foreach (var cl in clusters)
            {
                double cs0 = 0, sn0 = 0;
                foreach (var p in cl)
                {
                    double a0 = Math.Atan2(p[3] - p[1], p[2] - p[0]);
                    if (a0 < 0) a0 += Math.PI;
                    if (a0 >= Math.PI) a0 -= Math.PI;
                    cs0 += Math.Cos(a0);
                    sn0 += Math.Sin(a0);
                }
                double am = Math.Atan2(sn0, cs0);
                if (am < 0) am += Math.PI;
                if (am >= Math.PI) am -= Math.PI;
                double cu = Math.Cos(am), su = Math.Sin(am);

                var frags = new List<double[]>(); // v0, u0, u1, thick
                foreach (var p in cl)
                {
                    double u0 = p[0] * cu + p[1] * su, v0 = -p[0] * su + p[1] * cu;
                    double u1 = p[2] * cu + p[3] * su;
                    if (u1 < u0) { double t = u0; u0 = u1; u1 = t; }
                    frags.Add(new[] { v0, u0, u1, p[4] });
                }
                frags.Sort((aa, bb) => aa[0].CompareTo(bb[0]));

                var curRail = new List<double[]>();
                double railV = 0;
                foreach (var f in frags)
                {
                    if (curRail.Count == 0) { railV = f[0]; curRail.Add(f); }
                    else if (Math.Abs(f[0] - railV) <= opt.RailTolFt) { curRail.Add(f); }
                    else { FlushRailWithBridge(curRail, railV, railsOut, am, cu, su, jambMidpoints, stats, opt, rejects, pieces, faces); railV = f[0]; curRail.Add(f); }
                }
                FlushRailWithBridge(curRail, railV, railsOut, am, cu, su, jambMidpoints, stats, opt, rejects, pieces, faces);

                foreach (var r in railsOut)
                {
                    double v = r[0], u0 = r[1], u1 = r[2], thick = r[3];
                    wallPairs.Add(new WallPairCore
                    {
                        Sx = u0 * cu - v * su,
                        Sy = u0 * su + v * cu,
                        Ex = u1 * cu - v * su,
                        Ey = u1 * su + v * cu,
                        Thickness = thick
                    });
                }
                railsOut.Clear();
            }
            return wallPairs;
        }

        /// <summary>
        /// Chain one rail's fragments: silent merge for sloppy gaps, bridged
        /// merge for opening gaps with thickness agreement + evidence (jamb
        /// runs, a crossing wall's piece through the gap, or hatch at the gap
        /// midpoint - the latter two explain junction interruptions);
        /// otherwise the rail splits (and the gap is logged).
        /// </summary>
        public static void FlushRailWithBridge(List<double[]> curRail, double railV, List<double[]> railsOut,
            double am, double cu, double su, List<Pt> jambMidpoints, BridgeStatsCore stats,
            PochePipelineOptions opt, RejectLog rejects, List<double[]> allPieces, List<List<List<Pt>>> faces)
        {
            if (curRail.Count == 0) return;
            curRail.Sort((a, b) => a[1].CompareTo(b[1]));
            double u0 = curRail[0][1], u1 = curRail[0][2], thick = curRail[0][3];
            for (int k = 1; k < curRail.Count; k++)
            {
                double f0 = curRail[k][1], f1 = curRail[k][2];
                double gap = f0 - u1;
                if (gap <= opt.MergeGapFt)
                {
                    if (f1 > u1) u1 = f1;
                    if (curRail[k][3] > thick) thick = curRail[k][3];
                    stats.Silent++;
                }
                else if (gap <= opt.MaxOpeningGapFt &&
                         Math.Abs(curRail[k][3] - thick) <= opt.BridgeThicknessTolFt &&
                         HasJambBetween(jambMidpoints, am, railV, u1, f0, opt))
                {
                    if (f1 > u1) u1 = f1;
                    if (curRail[k][3] > thick) thick = curRail[k][3];
                    stats.Bridged++;
                }
                else if (opt.EnableJunctionEvidenceBridge &&
                         gap <= opt.MaxOpeningGapFt &&
                         Math.Abs(curRail[k][3] - thick) <= opt.BridgeThicknessTolFt &&
                         HasJunctionEvidence(allPieces, faces, am, cu, su, railV, u1, f0, opt))
                {
                    if (f1 > u1) u1 = f1;
                    if (curRail[k][3] > thick) thick = curRail[k][3];
                    stats.BridgedEvidence++;
                }
                else
                {
                    railsOut.Add(new double[] { railV, u0, u1, thick });
                    stats.Unbridged++;
                    if (rejects != null)
                        rejects.Log("unbridgedGap", new Dictionary<string, object>
                        {
                            ["gapLen"] = R2(gap),
                            ["thickA"] = R2(thick), ["thickB"] = R2(curRail[k][3]),
                            ["gapStart"] = new[] { Math.Round(u1 * cu - railV * su, 2), Math.Round(u1 * su + railV * cu, 2) },
                            ["gapEnd"] = new[] { Math.Round(f0 * cu - railV * su, 2), Math.Round(f0 * su + railV * cu, 2) }
                        });
                    u0 = f0; u1 = f1; thick = curRail[k][3];
                }
            }
            railsOut.Add(new double[] { railV, u0, u1, thick });
            curRail.Clear();
        }

        /// <summary>
        /// Junction evidence for gap bridging beyond jamb runs: (a) a
        /// near-perpendicular piece whose rail crosses this rail inside the
        /// gap span (a crossing wall explains the face interruption), or (b)
        /// the gap midpoint lies inside/near a hatch patch (the wall's band
        /// continues through the gap). Door openings carry neither: hatches
        /// break at openings and no crossing wall passes through.
        /// A near-parallel piece whose band spans the gap midpoint BLOCKS
        /// the bridge: the gap is then explained by an adjacent parallel
        /// wall (a separate wall with its own band, e.g. wythes meeting
        /// end-to-end), not by a junction crossing.
        /// </summary>
        public static bool HasJunctionEvidence(List<double[]> allPieces, List<List<List<Pt>>> faces,
            double am, double cu, double su, double railV, double gapStartU, double gapEndU, PochePipelineOptions opt)
        {
            bool hasEvidence = false;
            if (allPieces != null)
            {
                foreach (var p in allPieces)
                {
                    double dx = p[2] - p[0], dy = p[3] - p[1];
                    double pl = Math.Sqrt(dx * dx + dy * dy);
                    if (pl < 1e-9) continue;
                    // piece direction in the cluster frame
                    double pu = (dx * cu + dy * su) / pl;
                    if (Math.Abs(pu) > opt.BridgeCrossMaxDot) continue;   // near-perpendicular to the rail
                    // piece endpoints in the cluster frame
                    double uA = p[0] * cu + p[1] * su, vA = -p[0] * su + p[1] * cu;
                    double uB = p[2] * cu + p[3] * su, vB = -p[2] * su + p[3] * cu;
                    double vLo = Math.Min(vA, vB), vHi = Math.Max(vA, vB);
                    if (vLo > railV + opt.RailTolFt || vHi < railV - opt.RailTolFt) continue;  // doesn't reach this rail
                    // where the piece crosses railV (interpolate along the piece)
                    double t = vB == vA ? 0.5 : (railV - vA) / (vB - vA);
                    double uCross = uA + (uB - uA) * t;
                    if (gapStartU - opt.BridgeCrossSnapFt <= uCross && uCross <= gapEndU + opt.BridgeCrossSnapFt)
                    {
                        hasEvidence = true;
                        break;
                    }
                }
            }
            if (!hasEvidence && faces != null && faces.Count > 0)
            {
                double midU = (gapStartU + gapEndU) / 2.0;
                var mid = new Pt(midU * cu - railV * su, midU * su + railV * cu);
                if (InsideAnyPocheFaces(mid, faces, opt.PocheProximityFt)) hasEvidence = true;
            }
            if (!hasEvidence) return false;

            // Parallel-occupancy block: a near-parallel piece spanning the
            // gap midpoint with its band covering this rail means a separate
            // parallel wall occupies the gap - never bridge across it.
            if (allPieces != null)
            {
                double midU = (gapStartU + gapEndU) / 2.0;
                foreach (var p in allPieces)
                {
                    double dx = p[2] - p[0], dy = p[3] - p[1];
                    double pl = Math.Sqrt(dx * dx + dy * dy);
                    if (pl < 1e-9) continue;
                    double pu = (dx * cu + dy * su) / pl;
                    if (Math.Abs(pu) < opt.DedupAngleDot) continue;   // near-parallel to the rail
                    double uA = p[0] * cu + p[1] * su, vA = -p[0] * su + p[1] * cu;
                    double uB = p[2] * cu + p[3] * su, vB = -p[2] * su + p[3] * cu;
                    double uLo = Math.Min(uA, uB), uHi = Math.Max(uA, uB);
                    double vMid = (vA + vB) * 0.5;
                    double halfBand = p[4] / 2.0 + opt.RailTolFt;
                    if (midU < uLo - 0.05 || midU > uHi + 0.05) continue;                  // doesn't span the gap midpoint
                    if (Math.Abs(railV - vMid) > halfBand + Math.Abs(vA - vB) * 0.5) continue;  // band doesn't cover this rail
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Bridge evidence: a classified jamb midpoint on the rail (perp
        /// offset within RailTolFt in the cluster frame) with an
        /// along-track coordinate near either side of the gap.
        /// </summary>
        public static bool HasJambBetween(List<Pt> jambMidpoints, double am, double railV, double gapStartU, double gapEndU, PochePipelineOptions opt)
        {
            double cu = Math.Cos(am), su = Math.Sin(am);
            foreach (var jp in jambMidpoints)
            {
                double jU = jp.X * cu + jp.Y * su;
                double jV = -jp.X * su + jp.Y * cu;
                if (Math.Abs(jV - railV) > opt.RailTolFt) continue;
                if (gapStartU - opt.BridgeJambSnapFt <= jU && jU <= gapEndU + opt.BridgeJambSnapFt) return true;
            }
            return false;
        }

        /// <summary>
        /// Poche containment: true when p lies inside one hatch polygon or
        /// within toleranceFt of one of its edges. The near-edge allowance
        /// covers multi-wythe seams (adjacent hatch patches with a thin
        /// unfilled gap between materials).
        /// </summary>
        public static bool InsideAnyPoche(Pt p, List<List<Pt>> polys, double toleranceFt)
        {
            foreach (var poly in polys)
            {
                if (poly == null || poly.Count < 3) continue;
                if (PointNearOrInsidePoly(poly, p, toleranceFt)) return true;
            }
            return false;
        }

        /// <summary>
        /// Face-aware pochte containment. Each face carries the complete ring
        /// set of one hatch patch (outer ring plus interior hole rings). A
        /// point is inside pochte when it lies within tolerance of any ring
        /// edge (multi-wythe seam allowance), or when it falls inside an ODD
        /// number of one face's rings - the even-odd rule makes interior
        /// holes (chase/shaft cavities, pocket slots) count as empty space,
        /// so pairs across a cavity fail containment instead of becoming
        /// phantom walls.
        /// </summary>
        public static bool InsideAnyPocheFaces(Pt p, List<List<List<Pt>>> faces, double toleranceFt)
        {
            if (faces == null) return false;
            foreach (var rings in faces)
            {
                if (rings == null || rings.Count == 0) continue;
                int crossings = 0;
                foreach (var poly in rings)
                {
                    if (poly == null || poly.Count < 3) continue;
                    if (toleranceFt > 0 && PointNearEdge(poly, p, toleranceFt)) return true;
                    if (PointInsidePoly(poly, p)) crossings++;
                }
                if ((crossings & 1) == 1) return true;
            }
            return false;
        }

        private static bool PointNearOrInsidePoly(List<Pt> poly, Pt p, double toleranceFt)
        {
            if (toleranceFt > 0 && PointNearEdge(poly, p, toleranceFt)) return true;
            return PointInsidePoly(poly, p);
        }

        private static bool PointNearEdge(List<Pt> poly, Pt p, double toleranceFt)
        {
            int n = poly.Count;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                var a = poly[i]; var b = poly[j];
                var ab = b - a;
                double L = ab.Len();
                if (L < 1e-9) continue;
                double t = (p - a).Dot(ab) / (L * L);
                if (t < 0.0 || t > 1.0) continue;
                var q = a + ab * t;
                if (p.Dist(q) <= toleranceFt) return true;
            }
            return false;
        }

        private static bool PointInsidePoly(List<Pt> poly, Pt p)
        {
            bool inside = false;
            int n = poly.Count;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                var a = poly[i]; var b = poly[j];
                if ((a.Y > p.Y) != (b.Y > p.Y))
                {
                    double xInter = (b.X - a.X) * (p.Y - a.Y) / (b.Y - a.Y) + a.X;
                    if (p.X < xInter) inside = !inside;
                }
            }
            return inside;
        }

        /// <summary>
        /// Nested sliver test (pocket-door slots): true when pair a is thin
        /// (&lt; PocketSlotDepthFt) and a clearly longer, thicker,
        /// near-parallel mate exists whose band contains a's centerline rail
        /// and whose span contains a's span.
        /// </summary>
        public static bool IsNestedSliver(WallPairCore a, List<WallPairCore> all, PochePipelineOptions opt)
        {
            if (a.Thickness >= opt.PocketSlotDepthFt) return false;
            double dAx = a.Ex - a.Sx, dAy = a.Ey - a.Sy;
            double lenA = Math.Sqrt(dAx * dAx + dAy * dAy);
            if (lenA < 1e-9) return false;
            var uAx = dAx / lenA; var uAy = dAy / lenA;
            foreach (var b in all)
            {
                if (ReferenceEquals(a, b)) continue;
                if (b.Thickness < a.Thickness + 0.02) continue;
                double dBx = b.Ex - b.Sx, dBy = b.Ey - b.Sy;
                double lenB = Math.Sqrt(dBx * dBx + dBy * dBy);
                if (lenB < 1e-9) continue;
                if (lenB < lenA * 1.25) continue; // must be clearly longer

                if (Math.Abs(uAx * (dBx / lenB) + uAy * (dBy / lenB)) < 0.995) continue; // near-parallel rails
                var uBx = dBx / lenB; var uBy = dBy / lenB;
                var nBx = -uBy; var nBy = uBx;
                var mBx = (b.Sx + b.Ex) * 0.5; var mBy = (b.Sy + b.Ey) * 0.5;
                var mAx = (a.Sx + a.Ex) * 0.5; var mAy = (a.Sy + a.Ey) * 0.5;
                double rx = mAx - mBx, ry = mAy - mBy;
                double along = rx * uBx + ry * uBy;
                double perp = rx * nBx + ry * nBy;
                if (Math.Abs(perp) > b.Thickness / 2.0 + opt.SliverRailTolFt) continue;

                double a0 = (a.Sx - mBx) * uBx + (a.Sy - mBy) * uBy;
                double a1 = (a.Ex - mBx) * uBx + (a.Ey - mBy) * uBy;
                double b0 = along - lenB / 2.0, b1 = along + lenB / 2.0;
                double lo = Math.Min(a0, a1), hi = Math.Max(a0, a1);
                if (lo < b0 - 0.1 || hi > b1 + 0.1) continue;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Post-merge cleanup pass over the final wall list. Processes walls
        /// longest-first as keepers and applies, per (candidate, keeper) pair:
        ///  C. crossing stub cull: short thick near-perpendicular candidate whose
        ///     rail crosses a &gt;=2x longer keeper's rail well inside both spans
        ///     (end-cap artifact), or crosses the rails of two near-parallel
        ///     keepers (closed-rectangle pochte caps);
        ///  A. rail cull (same-rail fragments: tiny rail offset and thickness
        ///     agreement; or thick junction bands: candidate clearly thicker,
        ///     rail outside the keeper's band) when along-overlap is high;
        ///  B. rail merge: same rail, same thickness, small gap - the candidate
        ///     is absorbed into the keeper's span (fragmentation fix).
        /// Everything here is pure geometry; the reference layout is never seen.
        /// </summary>
        public static List<WallPairCore> DedupAndClean(List<WallPairCore> merged, PochePipelineOptions opt,
            RejectLog rejects, DedupStats stats)
        {
            if (merged == null || merged.Count == 0) return merged;
            var order = merged.OrderByDescending(p => p.Length).ThenByDescending(p => p.Thickness).ToList();
            var keepers = new List<WallPairCore>();

            // crossing info for stub rule, computed per candidate
            for (int ci = 0; ci < order.Count; ci++)
            {
                var cand = order[ci];
                bool consumed = false;

                // ---- rule C: crossing stub ----
                if (cand.Length <= opt.StubMaxLenFt && cand.Thickness >= opt.StubMinThickFt)
                {
                    var cu = (cand.End - cand.Start);
                    double cl = cu.Len();
                    if (cl > 1e-9)
                    {
                        cu = cu * (1.0 / cl);
                        var crossings = new List<int>(); // keeper indices crossed interiorly
                        for (int ki = 0; ki < keepers.Count; ki++)
                        {
                            var k = keepers[ki];
                            var ku = k.End - k.Start;
                            double kl = ku.Len();
                            if (kl < 1e-9) continue;
                            ku = ku * (1.0 / kl);
                            double dotCK = Math.Abs(cu.Dot(ku));
                            if (dotCK > opt.StubCrossMaxDot) continue;    // near-perpendicular only
                            // solve cand.S + s*cu = k.S + t*ku (s along cand, t along keeper)
                            double denom2 = cu.X * ku.Y - cu.Y * ku.X;
                            if (Math.Abs(denom2) < 1e-9) continue;
                            double rx = k.Sx - cand.Sx, ry = k.Sy - cand.Sy;
                            double s = (rx * ku.Y - ry * ku.X) / denom2;
                            double t = (rx * cu.Y - ry * cu.X) / denom2;
                            double m = opt.StubCrossMarginFt;
                            if (t < m || t > kl - m || s < m || s > cl - m) continue;
                            if (kl >= 2.0 * cand.Length)
                            {
                                // single long keeper: end-cap crossing a wall's body
                                stats.Stubs++;
                                LogCull(rejects, "dedupStub", cand, k);
                                consumed = true;
                                break;
                            }
                            crossings.Add(ki);
                        }
                        if (!consumed && crossings.Count >= 2)
                        {
                            bool twoParallel = false;
                            for (int x1 = 0; x1 < crossings.Count && !twoParallel; x1++)
                            {
                                for (int x2 = x1 + 1; x2 < crossings.Count && !twoParallel; x2++)
                                {
                                    var k1 = keepers[crossings[x1]];
                                    var k2 = keepers[crossings[x2]];
                                    var u1 = k1.End - k1.Start; u1 = u1 * (1.0 / u1.Len());
                                    var u2 = k2.End - k2.Start; u2 = u2 * (1.0 / u2.Len());
                                    if (Math.Abs(u1.Dot(u2)) >= opt.DedupAngleDot) twoParallel = true;
                                }
                            }
                            if (twoParallel)
                            {
                                stats.Stubs++;
                                LogCull(rejects, "dedupStubPair", cand, keepers[crossings[0]]);
                                consumed = true;
                            }
                        }
                    }
                }

                // ---- rules A/B: near-parallel against each keeper ----
                if (!consumed)
                {
                    var cu2 = cand.End - cand.Start;
                    double cl2 = cu2.Len();
                    if (cl2 > 1e-9)
                    {
                        cu2 = cu2 * (1.0 / cl2);
                        foreach (var k in keepers)
                        {
                            var ku = k.End - k.Start;
                            double kl = ku.Len();
                            if (kl < 1e-9) continue;
                            ku = ku * (1.0 / kl);
                            double dot = Math.Abs(cu2.Dot(ku));
                            if (dot < opt.DedupAngleDot) continue;

                            double rx = cand.Sx - k.Sx, ry = cand.Sy - k.Sy;
                            double along = rx * ku.X + ry * ku.Y;
                            double perp = Math.Abs(-rx * ku.Y + ry * ku.X);
                            double cs = along;
                            double ce = along + (cand.Ex - cand.Sx) * ku.X + (cand.Ey - cand.Sy) * ku.Y;
                            double clo = Math.Min(cs, ce), chi = Math.Max(cs, ce);
                            double ovLo = Math.Max(0.0, clo), ovHi = Math.Min(kl, chi);
                            double ov = Math.Max(0.0, ovHi - ovLo);
                            double minLen = Math.Min(cand.Length, k.Length);

                            bool candShorter = cand.Length < k.Length ||
                                (Math.Abs(cand.Length - k.Length) < 1e-9 && cand.Thickness <= k.Thickness);

                            // A: fragment cull - same rail, same thickness
                            if (candShorter && perp <= opt.DedupFragPerpFt &&
                                Math.Abs(cand.Thickness - k.Thickness) <= opt.DedupFragThickFt &&
                                ov >= opt.DedupCullOverlapFrac * minLen)
                            {
                                stats.Fragments++;
                                LogCull(rejects, "dedupFragment", cand, k);
                                consumed = true;
                                break;
                            }

                            // A: thick junction band - clearly thicker, rail at or
                            // outside the keeper's band face, high overlap
                            if (candShorter && cand.Thickness - k.Thickness >= opt.DedupBandMinThickDelta &&
                                perp <= opt.DedupRailCullTolFt && perp >= k.Thickness / 2.0 &&
                                ov >= opt.DedupCullOverlapFrac * minLen)
                            {
                                stats.Bands++;
                                LogCull(rejects, "dedupBand", cand, k);
                                consumed = true;
                                break;
                            }

                            // A: pocket-door slot strip - the candidate's rail
                            // lies inside a clearly longer, thicker mate's
                            // band and largely overlaps it. Pocket-door
                            // channels (~4in gap in the pochte, never
                            // intentional otherwise) yield thin strips
                            // hugging the real band wall; their area is
                            // already inside the mate. The length margin
                            // keeps equal-length parallel pairs (two real
                            // thin walls with a junk band between them).
                            if (candShorter &&
                                k.Length >= cand.Length * opt.DedupStripMinLenRatio &&
                                k.Thickness > cand.Thickness + opt.DedupStripMinThickDelta &&
                                perp <= k.Thickness / 2.0 + opt.DedupStripRailTolFt &&
                                ov >= opt.DedupStripOverlapFrac * minLen)
                            {
                                stats.Strips++;
                                LogCull(rejects, "dedupStrip", cand, k);
                                consumed = true;
                                break;
                            }

                            // B: same-rail merge (fragmentation fix)
                            if (perp <= opt.DedupMergePerpFt &&
                                Math.Abs(cand.Thickness - k.Thickness) <= opt.BridgeThicknessTolFt)
                            {
                                double gap = ov > 0 ? 0.0 : (chi < 0 ? -clo : (clo - kl));
                                if (gap <= opt.DedupMergeGapFt)
                                {
                                    double newLo = Math.Min(0.0, clo);
                                    double newHi = Math.Max(kl, chi);
                                    k.Sx = k.Sx + ku.X * newLo; k.Sy = k.Sy + ku.Y * newLo;
                                    k.Ex = k.Sx + ku.X * (newHi - newLo); k.Ey = k.Sy + ku.Y * (newHi - newLo);
                                    if (cand.Thickness > k.Thickness) k.Thickness = cand.Thickness;
                                    stats.Absorbed++;
                                    if (rejects != null)
                                        rejects.Log("dedupAbsorbed", new Dictionary<string, object>
                                        {
                                            ["len"] = R2(cand.Length),
                                            ["start"] = new[] { Math.Round(cand.Sx, 2), Math.Round(cand.Sy, 2) },
                                            ["end"] = new[] { Math.Round(cand.Ex, 2), Math.Round(cand.Ey, 2) }
                                        });
                                    consumed = true;
                                    break;
                                }
                            }
                        }
                    }
                }

                if (!consumed) keepers.Add(cand);
            }
            return keepers;
        }

        private static void LogCull(RejectLog rejects, string stage, WallPairCore cand, WallPairCore keeper)
        {
            if (rejects == null) return;
            rejects.Log(stage, new Dictionary<string, object>
            {
                ["len"] = R2(cand.Length),
                ["thick"] = R2(cand.Thickness),
                ["start"] = new[] { Math.Round(cand.Sx, 2), Math.Round(cand.Sy, 2) },
                ["end"] = new[] { Math.Round(cand.Ex, 2), Math.Round(cand.Ey, 2) },
                ["keeperLen"] = R2(keeper.Length),
                ["keeperThick"] = R2(keeper.Thickness)
            });
        }

        /// <summary>
        /// End extension: snap each wall's ends outward onto the rail line of
        /// the nearest crossing wall when the junction is real - the crossing
        /// wall is meaningfully non-parallel, long enough to be a wall (not a
        /// jamb stub), and its span reaches the junction. Thick walls (likely
        /// junction bands) never extend, and the extension segment must stay
        /// inside the hatch (the wall physically continues there). Only
        /// outward moves: ends never shorten.
        /// </summary>
        public static List<WallPairCore> ExtendEnds(List<WallPairCore> walls, PochePipelineOptions opt,
            RejectLog rejects, List<List<List<Pt>>> faces, out int extendedCount)
        {
            extendedCount = 0;
            if (walls == null || walls.Count < 2) return walls;

            var result = new List<WallPairCore>(walls);
            for (int i = 0; i < result.Count; i++)
            {
                var w = result[i];
                if (w.Thickness > opt.ExtMaxThickFt) continue;   // thick = junction band; leave alone
                var d0 = w.End - w.Start;
                double len = d0.Len();
                if (len < 1e-9) continue;
                var u = d0 * (1.0 / len);

                for (int endIdx = 0; endIdx < 2; endIdx++)
                {
                    var endPt = endIdx == 0 ? w.Start : w.End;
                    var outDir = endIdx == 0 ? new Pt(-u.X, -u.Y) : u;

                    double bestT = opt.ExtMaxFt;
                    Pt bestP = default(Pt);
                    bool found = false;

                    for (int j = 0; j < result.Count; j++)
                    {
                        if (j == i) continue;
                        var c = result[j];
                        if (c.Length < opt.ExtCrossMinLenFt) continue;
                        var dc = c.End - c.Start;
                        double lc = dc.Len();
                        if (lc < 1e-9) continue;
                        var uc = dc * (1.0 / lc);
                        if (Math.Abs(u.Dot(uc)) > opt.ExtCrossMaxDot) continue;   // must be a crossing wall

                        // intersect w's rail line with c's rail line:
                        // endPt + t*outDir = c.Start + s*uc  (t outward along this
                        // wall, s along the crossing wall)
                        double denom2 = outDir.X * uc.Y - outDir.Y * uc.X;
                        if (Math.Abs(denom2) < 1e-9) continue;   // parallel rails
                        double rx = c.Sx - endPt.X, ry = c.Sy - endPt.Y;
                        double t = (rx * uc.Y - ry * uc.X) / denom2;
                        double s = (rx * outDir.Y - ry * outDir.X) / denom2;

                        if (t <= 0.02 || t > bestT) continue;   // only outward, within range
                        if (s < -opt.ExtCrossSpanMarginFt || s > lc + opt.ExtCrossSpanMarginFt) continue;   // crossing wall must reach the junction

                        // the extension segment must lie inside the hatch:
                        // sample its quarter points.
                        if (faces != null && faces.Count > 0)
                        {
                            bool inside = true;
                            for (int q = 1; q <= 3 && inside; q++)
                            {
                                double f = t * q / 4.0;
                                var sample = new Pt(endPt.X + outDir.X * f, endPt.Y + outDir.Y * f);
                                if (!InsideAnyPocheFaces(sample, faces, opt.ExtPocheTolFt)) inside = false;
                            }
                            if (!inside) continue;
                        }

                        bestT = t;
                        bestP = new Pt(endPt.X + outDir.X * t, endPt.Y + outDir.Y * t);
                        found = true;
                    }

                    if (found)
                    {
                        if (endIdx == 0) { w.Sx = bestP.X; w.Sy = bestP.Y; }
                        else { w.Ex = bestP.X; w.Ey = bestP.Y; }
                        extendedCount++;
                        if (rejects != null)
                            rejects.Log("endExtend", new Dictionary<string, object>
                            {
                                ["dist"] = R2(bestT),
                                ["point"] = new[] { Math.Round(bestP.X, 2), Math.Round(bestP.Y, 2) }
                            });
                    }
                }
            }
            return result;
        }
    }
}

