// 
//                       RevitAPI-Solutions
// Copyright (c) Duong Tran Quang (DTDucas) (baymax.contact@gmail.com)
// 
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.
//

using RevitMCPCommandSet.Models.Common;

namespace RevitMCPCommandSet.Utils;

/// <summary>
///     Utilities for geometry processing in Revit
/// </summary>
public static class GeometryUtils
{
    /// <summary>
    ///     Creates a rectangular curve array from dimensions and position
    /// </summary>
    /// <param name="width">Width (feet)</param>
    /// <param name="length">Length (feet)</param>
    /// <param name="origin">Origin coordinate (0,0 if null)</param>
    /// <param name="elevation">Elevation (0 if not specified)</param>
    /// <returns>Curve array forming a rectangle</returns>
    public static CurveArray CreateRectangularFootprint(double width, double length, XYZ origin = null,
        double elevation = 0)
    {
        // If origin not provided, default to coordinate origin
        if (origin == null)
            origin = new XYZ(0, 0, elevation);
        else if (Math.Abs(origin.Z - elevation) > 1e-6)
            // If origin has a different Z than elevation, create a new origin with Z = elevation
            origin = new XYZ(origin.X, origin.Y, elevation);

        var curves = new CurveArray();

        // Create 4 vertices of the rectangle
        var p1 = origin;
        var p2 = new XYZ(origin.X + width, origin.Y, origin.Z);
        var p3 = new XYZ(origin.X + width, origin.Y + length, origin.Z);
        var p4 = new XYZ(origin.X, origin.Y + length, origin.Z);

        // Create 4 edges connecting the vertices
        curves.Append(Line.CreateBound(p1, p2));
        curves.Append(Line.CreateBound(p2, p3));
        curves.Append(Line.CreateBound(p3, p4));
        curves.Append(Line.CreateBound(p4, p1));

        return curves;
    }

    /// <summary>
    ///     Creates a rectangular CurveLoop from dimensions and position
    /// </summary>
    /// <param name="width">Width (feet)</param>
    /// <param name="length">Length (feet)</param>
    /// <param name="elevation">Elevation (0 if not specified)</param>
    /// <param name="origin">Origin coordinate (0,0 if null)</param>
    /// <returns>CurveLoop forming a rectangle</returns>
    public static CurveLoop CreateRectangularBoundary(double width, double length, double elevation = 0,
        XYZ origin = null)
    {
        // If origin not provided, default to coordinate origin
        if (origin == null)
            origin = new XYZ(0, 0, elevation);
        else if (Math.Abs(origin.Z - elevation) > 1e-6)
            // If origin has a different Z than elevation, create a new origin with Z = elevation
            origin = new XYZ(origin.X, origin.Y, elevation);

        var curveLoop = new CurveLoop();

        // Create 4 vertices of the rectangle
        var p1 = origin;
        var p2 = new XYZ(origin.X + width, origin.Y, origin.Z);
        var p3 = new XYZ(origin.X + width, origin.Y + length, origin.Z);
        var p4 = new XYZ(origin.X, origin.Y + length, origin.Z);

        // Create 4 edges connecting the vertices
        curveLoop.Append(Line.CreateBound(p1, p2));
        curveLoop.Append(Line.CreateBound(p2, p3));
        curveLoop.Append(Line.CreateBound(p3, p4));
        curveLoop.Append(Line.CreateBound(p4, p1));

        return curveLoop;
    }

    /// <summary>
    ///     Calculates the center point of a rectangle
    /// </summary>
    /// <param name="width">Width</param>
    /// <param name="length">Length</param>
    /// <param name="origin">Origin point</param>
    /// <returns>Center point</returns>
    public static XYZ CalculateRectangleCenter(double width, double length, XYZ origin)
    {
        return new XYZ(
            origin.X + width / 2,
            origin.Y + length / 2,
            origin.Z
        );
    }

    /// <summary>
    ///     Converts JZPoint to Revit XYZ (with unit conversion)
    /// </summary>
    /// <param name="point">JZPoint (mm)</param>
    /// <returns>XYZ (feet)</returns>
    public static XYZ ConvertToXYZ(JZPoint point)
    {
        if (point == null) return null;
        return new XYZ(
            point.X / 304.8,
            point.Y / 304.8,
            point.Z / 304.8
        );
    }

    /// <summary>
    ///     Converts Revit XYZ to JZPoint (with unit conversion)
    /// </summary>
    /// <param name="point">XYZ (feet)</param>
    /// <returns>JZPoint (mm)</returns>
    public static JZPoint ConvertToJZPoint(XYZ point)
    {
        if (point == null) return null;
        return new JZPoint(
            point.X * 304.8,
            point.Y * 304.8,
            point.Z * 304.8
        );
    }

    /// <summary>
    ///     Creates JZLine from two points
    /// </summary>
    /// <param name="start">Start point</param>
    /// <param name="end">End point</param>
    /// <returns>JZLine</returns>
    public static JZLine CreateJZLine(JZPoint start, JZPoint end)
    {
        return new JZLine(start, end);
    }

    /// <summary>
    ///     Creates JZFace from array of points (polygon)
    /// </summary>
    /// <param name="points">Array of points</param>
    /// <returns>JZFace</returns>
    public static JZFace CreateJZFaceFromPoints(List<JZPoint> points)
    {
        var face = new JZFace();
        var outerLoop = new List<JZLine>();

        // Create edges connecting consecutive points
        for (var i = 0; i < points.Count; i++)
        {
            var startPoint = points[i];
            var endPoint = points[(i + 1) % points.Count]; // Loop back to first point from last point
            outerLoop.Add(CreateJZLine(startPoint, endPoint));
        }

        face.OuterLoop = outerLoop;
        return face;
    }

    /// <summary>
    ///     Calculates distance between two points
    /// </summary>
    /// <param name="p1">First point</param>
    /// <param name="p2">Second point</param>
    /// <returns>Distance</returns>
    public static double Distance(XYZ p1, XYZ p2)
    {
        var dx = p2.X - p1.X;
        var dy = p2.Y - p1.Y;
        var dz = p2.Z - p1.Z;
        return Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    /// <summary>
    ///     Finds intersection of two lines
    /// </summary>
    /// <param name="line1">First line</param>
    /// <param name="line2">Second line</param>
    /// <returns>Intersection point or null if none</returns>
    public static XYZ FindIntersection(Line line1, Line line2)
    {
        // Implement algorithm to calculate intersection
        // Simple method: use Revit API to find intersection
#pragma warning disable CS0618 // Curve.Intersect deprecated in Revit 2026
        var results = new IntersectionResultArray();
        if (line1.Intersect(line2, out results) == SetComparisonResult.Overlap && results.Size > 0)
            return results.get_Item(0).XYZPoint;
#pragma warning restore CS0618
        return null;
    }

    /// <summary>
    ///     Finds normal vector of a plane
    /// </summary>
    /// <param name="p1">First point on plane</param>
    /// <param name="p2">Second point on plane</param>
    /// <param name="p3">Third point on plane</param>
    /// <returns>Normalized normal vector</returns>
    public static XYZ ComputeNormal(XYZ p1, XYZ p2, XYZ p3)
    {
        var v1 = p2.Subtract(p1);
        var v2 = p3.Subtract(p1);
        var normal = v1.CrossProduct(v2);
        return normal.Normalize();
    }

    /// <summary>
    ///     Checks if two lines are parallel
    /// </summary>
    /// <param name="line1">First line</param>
    /// <param name="line2">Second line</param>
    /// <param name="tolerance">Tolerance</param>
    /// <returns>True if parallel</returns>
    public static bool IsParallel(Line line1, Line line2, double tolerance = 1e-6)
    {
        var dir1 = line1.Direction.Normalize();
        var dir2 = line2.Direction.Normalize();

        // Check if direction vectors are parallel
        var dot = Math.Abs(dir1.DotProduct(dir2));
        return Math.Abs(dot - 1.0) < tolerance;
    }

    /// <summary>
    ///     Checks if two lines are perpendicular
    /// </summary>
    /// <param name="line1">First line</param>
    /// <param name="line2">Second line</param>
    /// <param name="tolerance">Tolerance</param>
    /// <returns>True if perpendicular</returns>
    public static bool IsPerpendicular(Line line1, Line line2, double tolerance = 1e-6)
    {
        var dir1 = line1.Direction.Normalize();
        var dir2 = line2.Direction.Normalize();

        // Check if direction vectors are perpendicular
        var dot = Math.Abs(dir1.DotProduct(dir2));
        return dot < tolerance;
    }
}