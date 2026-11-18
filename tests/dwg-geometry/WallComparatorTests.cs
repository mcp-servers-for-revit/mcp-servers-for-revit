using RevitMCPCommandSet.Geometry;
using TUnit.Assertions;
using TUnit.Core;
using System.Collections.Generic;
using System.Linq;

namespace RevitMCPCommandSet.Geometry.Tests
{
    public static class W
    {
        public static WallPairCore H(double x0, double y, double x1, double t = 1.0)
        {
            return new WallPairCore { Sx = x0, Sy = y, Ex = x1, Ey = y, Thickness = t };
        }
        public static WallPairCore V(double x, double y0, double y1, double t = 1.0)
        {
            return new WallPairCore { Sx = x, Sy = y0, Ex = x, Ey = y1, Thickness = t };
        }
    }

    public class WallComparatorTests
    {
        [Test]
        public async Task IdenticalWalls_MatchExactly()
        {
            var targets = new List<WallPairCore> { W.H(0, 0.5, 10, 1.0) };
            var actual = new List<WallPairCore> { W.H(0, 0.5, 10, 1.0) };
            var rep = WallComparator.Compare(targets, actual);

            await Assert.That(rep.Matched.Count).IsEqualTo(1);
            await Assert.That(rep.Missing.Count).IsEqualTo(0);
            await Assert.That(rep.Extra.Count).IsEqualTo(0);
            await Assert.That(rep.MatchFrac).IsEqualTo(1.0);
            await Assert.That(Math.Abs(rep.Matched[0].LengthDeltaFt)).IsLessThanOrEqualTo(1e-6);
        }

        [Test]
        public async Task SameRailExtension_IsReshapedNotExtra()
        {
            // target 10ft; generator produced a 12ft version (2ft extension)
            var targets = new List<WallPairCore> { W.H(0, 0.5, 10, 1.0) };
            var actual = new List<WallPairCore> { W.H(0, 0.5, 12, 1.0) };
            var rep = WallComparator.Compare(targets, actual);

            await Assert.That(rep.Matched.Count).IsEqualTo(1);
            await Assert.That(rep.Matched[0].Reshaped).IsTrue();
            await Assert.That(rep.Missing.Count).IsEqualTo(0);
            await Assert.That(rep.Extra.Count).IsEqualTo(0);
        }

        [Test]
        public async Task MissingAndExtraAreReported()
        {
            var targets = new List<WallPairCore>
            {
                W.H(0, 0.5, 10, 1.0),
                W.V(20, 0, 8, 1.0) // missing in the generator output
            };
            var actual = new List<WallPairCore>
            {
                W.H(0, 0.5, 10, 1.0),
                W.H(40, 2.5, 50, 1.2) // no target here -> extra
            };
            var rep = WallComparator.Compare(targets, actual);

            await Assert.That(rep.Matched.Count).IsEqualTo(1);
            await Assert.That(rep.Missing.Count).IsEqualTo(1);
            await Assert.That(rep.Extra.Count).IsEqualTo(1);
            await Assert.That(rep.MatchFrac).IsEqualTo(0.5).Within(1e-9);
        }

        [Test]
        public async Task OffRailFarWall_DoesNotMatch()
        {
            // 2ft parallel offset: beyond the 3" rail tolerance
            var targets = new List<WallPairCore> { W.H(0, 0.5, 10, 1.0) };
            var actual = new List<WallPairCore> { W.H(0, 2.5, 10, 1.0) };
            var rep = WallComparator.Compare(targets, actual);

            await Assert.That(rep.Matched.Count).IsEqualTo(0);
            await Assert.That(rep.Missing.Count).IsEqualTo(1);
            await Assert.That(rep.Extra.Count).IsEqualTo(1);
        }

        [Test]
        public async Task GreedyBestFirst_PrevailsOverAmbiguity()
        {
            // two targets; only ONE actual wall matches target 1 exactly and
            // partially overlaps target 2 - the real match must win.
            var targets = new List<WallPairCore>
            {
                W.H(0, 0.5, 10, 1.0),
                W.H(8, 0.5, 14, 1.0)
            };
            var actual = new List<WallPairCore> { W.H(0, 0.5, 10, 1.0) };
            var rep = WallComparator.Compare(targets, actual);

            await Assert.That(rep.Matched.Count).IsEqualTo(1);
            await Assert.That(rep.Matched[0].TargetIndex).IsEqualTo(0);
            await Assert.That(rep.Missing.Count).IsEqualTo(1);
        }

        [Test]
        public async Task ThicknessDeltaIsInformational()
        {
            var targets = new List<WallPairCore> { W.H(0, 0.5, 10, 0.75) };
            var actual = new List<WallPairCore> { W.H(0, 0.5, 10, 1.0) };
            var rep = WallComparator.Compare(targets, actual);

            await Assert.That(rep.Matched.Count).IsEqualTo(1);
            await Assert.That(rep.Matched[0].ThicknessDeltaFt > 0).IsTrue();
        }
    }
}
