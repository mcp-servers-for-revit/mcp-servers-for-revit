using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace RevitMCPCommandSet.Utils.OffAxis
{
    /// <summary>
    /// Shared geometric utilities for off-axis line detection, ray-ray intersections,
    /// angular deviations, and length-preserving snap solvers.
    /// </summary>
    public static class OffAxisGeometryUtils
    {
        public const double DegToRad = Math.PI / 180.0;
        public const double RadToDeg = 180.0 / Math.PI;

        public const double FlagMovementInches = 0.5;
        public const double FlagDeviationDegrees = 0.1;

        public const double MinDeviationDeg = 0.001;
        public const double MaxDeviationDeg = 0.1;

        /// <summary>
        /// Default maximum allowed movement (in inches) before a fix is skipped by the safety cap.
        /// Callers may raise this via the maxMoveInches parameter.
        /// </summary>
        public const double DefaultMaxMoveInches = 1.0;

        /// <summary>
        /// Validates a min/max angular deviation band. Returns an error message, or null if valid.
        /// </summary>
        public static string ValidateDeviationBand(double minAng, double maxAng)
        {
            if (double.IsNaN(minAng) || double.IsNaN(maxAng)) return "minAngleDeg/maxAngleDeg must be numeric";
            if (minAng < 0 || maxAng < 0) return "minAngleDeg/maxAngleDeg must be non-negative";
            if (minAng >= maxAng) return "minAngleDeg must be less than maxAngleDeg";
            return null;
        }

        private static readonly double InvSqrt2 = 1.0 / Math.Sqrt(2.0);

        /// <summary>
        /// 18 candidate directions in 3D world space (3 coordinate axes +/- and 6 diagonals +/-).
        /// </summary>
        public static readonly XYZ[] WorldCandidateDirections = new XYZ[]
        {
            new XYZ( 1, 0, 0), new XYZ(-1, 0, 0),
            new XYZ( 0, 1, 0), new XYZ( 0,-1, 0),
            new XYZ( 0, 0, 1), new XYZ( 0, 0,-1),
            new XYZ( InvSqrt2,  InvSqrt2, 0), new XYZ(-InvSqrt2, -InvSqrt2, 0),
            new XYZ( InvSqrt2, -InvSqrt2, 0), new XYZ(-InvSqrt2,  InvSqrt2, 0),
            new XYZ( InvSqrt2, 0,  InvSqrt2), new XYZ(-InvSqrt2, 0, -InvSqrt2),
            new XYZ( InvSqrt2, 0, -InvSqrt2), new XYZ(-InvSqrt2, 0,  InvSqrt2),
            new XYZ(0,  InvSqrt2,  InvSqrt2), new XYZ(0, -InvSqrt2, -InvSqrt2),
            new XYZ(0,  InvSqrt2, -InvSqrt2), new XYZ(0, -InvSqrt2,  InvSqrt2),
        };

        /// <summary>
        /// Calculates the minimum angular deviation in degrees of a unit vector from any of the 18 candidate world directions.
        /// </summary>
        public static double WorldDev(XYZ u)
        {
            double maxDot = -1.0;
            foreach (var c in WorldCandidateDirections)
            {
                double d = u.DotProduct(c);
                if (d > maxDot) maxDot = d;
            }
            if (maxDot > 1.0) maxDot = 1.0;
            if (maxDot < -1.0) maxDot = -1.0;
            return Math.Acos(maxDot) * RadToDeg;
        }

        /// <summary>
        /// Finds the closest candidate world direction to the given direction vector.
        /// </summary>
        public static XYZ ClosestWorldCandidate(XYZ u)
        {
            XYZ best = WorldCandidateDirections[0];
            double maxDot = -1.0;
            foreach (var c in WorldCandidateDirections)
            {
                double d = u.DotProduct(c);
                if (d > maxDot)
                {
                    maxDot = d;
                    best = c;
                }
            }
            return best;
        }

        /// <summary>
        /// Deviation from the nearest orthogonal axis (0..45 degrees).
        /// </summary>
        public static double DeviationFromAxis(double angleDeg)
        {
            double a = angleDeg % 90.0;
            if (a > 45.0) a = 90.0 - a;
            return a;
        }

        /// <summary>
        /// Predicted endpoint swing (in inches) for a line of given length being rotated about p0.
        /// Swing = 2 * length * sin(dev / 2) (converted to inches).
        /// </summary>
        public static double OccupiedSwingInches(double lengthFt, double devDeg)
        {
            return (2.0 * lengthFt * Math.Abs(Math.Sin(devDeg * DegToRad / 2.0))) * 12.0;
        }

        /// <summary>
        /// 2D angle of a Line in degrees relative to the X axis (0..90).
        /// </summary>
        public static double LineAngleDeg2D(Line ln)
        {
            XYZ p0 = ln.GetEndPoint(0);
            XYZ p1 = ln.GetEndPoint(1);
            return Math.Atan2(Math.Abs(p1.Y - p0.Y), Math.Abs(p1.X - p0.X)) * RadToDeg;
        }

        /// <summary>
        /// Nearest 0, 45, 90, 135, 180 snap target angle in degrees.
        /// </summary>
        public static double SnapTargetDeg(double angleDeg)
        {
            return Math.Round(angleDeg / 45.0) * 45.0;
        }

        /// <summary>
        /// Snaps a line to the nearest 0/45/90 ray about endpoint p0, preserving total length.
        /// </summary>
        public static Line SnapLineP0(Line orig, out double devDeg, out double swingIn)
        {
            XYZ p0 = orig.GetEndPoint(0);
            XYZ p1 = orig.GetEndPoint(1);
            double length = orig.Length;
            double angleDeg = LineAngleDeg2D(orig);
            double targetDeg = SnapTargetDeg(angleDeg);
            devDeg = Math.Abs(angleDeg - targetDeg);
            swingIn = OccupiedSwingInches(length, devDeg);

            double targetRad = targetDeg * DegToRad;
            double dx = (p1.X >= p0.X ? 1.0 : -1.0) * Math.Abs(Math.Cos(targetRad)) * length;
            double dy = (p1.Y >= p0.Y ? 1.0 : -1.0) * Math.Abs(Math.Sin(targetRad)) * length;
            XYZ newP1 = new XYZ(p0.X + dx, p0.Y + dy, p1.Z);

            return Line.CreateBound(p0, newP1);
        }

        /// <summary>
        /// Snaps a line by rotating about its midpoint, preserving total length.
        /// </summary>
        public static Line SnapLineMidpoint(Line orig, out double devDeg, out double swingIn)
        {
            XYZ p0 = orig.GetEndPoint(0);
            XYZ p1 = orig.GetEndPoint(1);
            XYZ mid = 0.5 * (p0 + p1);
            XYZ u = (p1 - p0).Normalize();
            XYZ snappedDir = ClosestWorldCandidate(u);

            devDeg = WorldDev(u);
            swingIn = OccupiedSwingInches(orig.Length, devDeg);

            XYZ half = 0.5 * orig.Length * snappedDir;
            return Line.CreateBound(mid - half, mid + half);
        }

        /// <summary>
        /// 2D Ray-Ray intersection solver. Returns null if lines are parallel or degenerate.
        /// </summary>
        public static XYZ IntersectRays2D(XYZ p1, XYZ d1, XYZ p2, XYZ d2, double tolerance = 1e-9)
        {
            double cross = d1.X * d2.Y - d1.Y * d2.X;
            if (Math.Abs(cross) < tolerance)
            {
                return null;
            }

            double dx = p2.X - p1.X;
            double dy = p2.Y - p1.Y;
            double t = (dx * d2.Y - dy * d2.X) / cross;

            return new XYZ(p1.X + t * d1.X, p1.Y + t * d1.Y, p1.Z);
        }
    }
}
