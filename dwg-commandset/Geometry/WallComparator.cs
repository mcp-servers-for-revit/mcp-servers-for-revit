using System;
using System.Collections.Generic;
using System.Linq;

namespace RevitMCPCommandSet.Geometry
{
    /// <summary>
    /// Compares a pipeline run against an approved target layout. Targets
    /// are the reference walls the user approved; generated walls score
    /// against them. Matching is greedy best-first on a similarity metric:
    /// near-parallel rails (~2.5deg), centerline within 3", and span
    /// overlap (SharedSpanFrac of the shorter wall). Matched walls with
    /// length deltas beyond ReshapeEndTolFt are additionally reported as
    /// 'reshaped'. Unmatched targets = missing; unmatched generated = extra
    /// (false positives).
    /// </summary>
    public class WallCompareReport
    {
        public List<MatchedWall> Matched = new List<MatchedWall>();
        public List<WallPairCore> Missing = new List<WallPairCore>();
        public List<WallPairCore> Extra = new List<WallPairCore>();
        public int TargetCount;
        public int ActualCount;
        public double MatchFrac; // matched / targets
    }

    public class MatchedWall
    {
        public int TargetIndex;
        public int ActualIndex;
        public double LengthDeltaFt;  // actual - target
        public double ThicknessDeltaFt;
        public bool Reshaped;
        public WallPairCore Target;
        public WallPairCore Actual;
    }

    public static class WallComparator
    {
        public const double MatchRailTolFt = 0.25;   // centerline must sit within 3"
        public const double SameDirCosMin = 0.995;   // ~2.5deg
        public const double MatchSpanFracMin = 0.6;  // partial-overlap floor
        public const double ReshapeEndTolFt = 0.35;  // below = 'matched'

        /// <summary>
        /// Greedy best-first matching: each (target, wall) candidate pair is
        /// scored; the strongest match consumes both entries.
        /// </summary>
        public static WallCompareReport Compare(List<WallPairCore> targets, List<WallPairCore> actual)
        {
            var report = new WallCompareReport
            {
                TargetCount = targets.Count,
                ActualCount = actual.Count
            };
            var usedActual = new bool[actual.Count];
            var usedTarget = new bool[targets.Count];

            var candidates = new List<(int t, int a, double score)>();
            for (int t = 0; t < targets.Count; t++)
                for (int a = 0; a < actual.Count; a++)
                {
                    double score = Similarity(targets[t], actual[a]);
                    if (score > 0) candidates.Add((t, a, score));
                }
            candidates.Sort((x, y) => y.score.CompareTo(x.score));

            foreach (var c in candidates)
            {
                if (usedTarget[c.t] || usedActual[c.a]) continue;
                usedTarget[c.t] = true;
                usedActual[c.a] = true;

                report.Matched.Add(new MatchedWall
                {
                    TargetIndex = c.t,
                    ActualIndex = c.a,
                    Target = targets[c.t],
                    Actual = actual[c.a],
                    LengthDeltaFt = actual[c.a].Length - targets[c.t].Length,
                    ThicknessDeltaFt = actual[c.a].Thickness - targets[c.t].Thickness,
                    Reshaped = Math.Abs(actual[c.a].Length - targets[c.t].Length) > ReshapeEndTolFt
                });
            }

            for (int t = 0; t < targets.Count; t++) if (!usedTarget[t]) report.Missing.Add(targets[t]);
            for (int a = 0; a < actual.Count; a++) if (!usedActual[a]) report.Extra.Add(actual[a]);
            report.MatchFrac = report.TargetCount == 0 ? 1.0 : (double)report.Matched.Count / report.TargetCount;
            return report;
        }

        /// <summary>0 = no match; otherwise higher is better. Perpendicular distance weighted lightly.</summary>
        public static double Similarity(WallPairCore target, WallPairCore actual)
        {
            double dTx = target.Ex - target.Sx, dTy = target.Ey - target.Sy;
            double lenT = Math.Sqrt(dTx * dTx + dTy * dTy);
            double dAx = actual.Ex - actual.Sx, dAy = actual.Ey - actual.Sy;
            double lenA = Math.Sqrt(dAx * dAx + dAy * dAy);
            if (lenT < 1e-9 || lenA < 1e-9) return 0;
            double cos = Math.Abs((dTx * dAx + dTy * dAy) / (lenT * lenA));
            if (cos < SameDirCosMin) return 0;

            var uAx = dAx / lenA; var uAy = dAy / lenA;
            var nAx = -uAy; var nAy = uAx;
            double rx = (target.Sx + target.Ex) / 2.0 - (actual.Sx + actual.Ex) / 2.0;
            double ry = (target.Sy + target.Ey) / 2.0 - (actual.Sy + actual.Ey) / 2.0;
            double perp = Math.Abs(rx * nAx + ry * nAy);
            if (perp > MatchRailTolFt) return 0;

            double ov = SharedSpanFrac(target, actual);
            if (ov < MatchSpanFracMin) return 0;
            return Math.Min(lenT, lenA) / Math.Max(lenT, lenA) - perp * 0.5;
        }

        /// <summary>Overlap length / min(spanX, spanY), in the pair's own frame.</summary>
        public static double SharedSpanFrac(WallPairCore x, WallPairCore y)
        {
            double dx = x.Ex - x.Sx, dy = x.Ey - x.Sy;
            double len = Math.Sqrt(dx * dx + dy * dy);
            if (len < 1e-9) return 0;
            var ux = dx / len; var uy = dy / len;
            // project both walls' endpoints onto x's axis
            double a0 = 0, a1 = x.Length;
            double b0 = (y.Sx - x.Sx) * ux + (y.Sy - x.Sy) * uy;
            double b1 = (y.Ex - x.Sx) * ux + (y.Ey - x.Sy) * uy;
            double yLo = Math.Min(b0, b1), yHi = Math.Max(b0, b1);
            double lo = Math.Max(yLo, a0), hi = Math.Min(yHi, a1);
            if (hi <= lo) return 0;
            return (hi - lo) / Math.Min(a1, yHi - yLo);
        }
    }
}
