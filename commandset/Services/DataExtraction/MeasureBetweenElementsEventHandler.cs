using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCPCommandSet.Models.Common;
using RevitMCPSDK.API.Interfaces;

namespace RevitMCPCommandSet.Services.DataExtraction
{
    public class MeasureBetweenElementsEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        private long _elementId1;
        private long _elementId2;
        private double[] _point1;
        private double[] _point2;
        private string _measureType = "center_to_center";

        public AIResult<object> Result { get; private set; }
        public bool TaskCompleted { get; private set; }
        private readonly ManualResetEvent _resetEvent = new ManualResetEvent(false);

        public void SetParameters(long elementId1, long elementId2, double[] point1, double[] point2, string measureType)
        {
            _elementId1 = elementId1;
            _elementId2 = elementId2;
            _point1 = point1;
            _point2 = point2;
            _measureType = measureType ?? "center_to_center";
            TaskCompleted = false;
            _resetEvent.Reset();
        }

        public bool WaitForCompletion(int timeoutMilliseconds = 10000)
        {
            _resetEvent.Reset();
            return _resetEvent.WaitOne(timeoutMilliseconds);
        }

        public void Execute(UIApplication app)
        {
            try
            {
                var doc = app.ActiveUIDocument.Document;

                XYZ p1 = ResolvePoint(doc, _elementId1, _point1);
                XYZ p2 = ResolvePoint(doc, _elementId2, _point2);

                if (p1 == null || p2 == null)
                    throw new ArgumentException("Must provide two valid references (element IDs or points)");

                double distanceFeet = p1.DistanceTo(p2);
                double distanceMm = ConvertToMm(distanceFeet);
                double dx = ConvertToMm(Math.Abs(p2.X - p1.X));
                double dy = ConvertToMm(Math.Abs(p2.Y - p1.Y));
                double dz = ConvertToMm(Math.Abs(p2.Z - p1.Z));

                Result = new AIResult<object>
                {
                    Success = true,
                    Message = $"Distance: {distanceMm:F1} mm ({distanceMm / 1000:F3} m)",
                    Response = new
                    {
                        distance = Math.Round(distanceMm, 1),
                        distanceMeters = Math.Round(distanceMm / 1000, 3),
                        deltaX = Math.Round(dx, 1),
                        deltaY = Math.Round(dy, 1),
                        deltaZ = Math.Round(dz, 1),
                        point1 = FormatPoint(p1),
                        point2 = FormatPoint(p2),
                        measureType = _measureType
                    }
                };
            }
            catch (Exception ex)
            {
                Result = new AIResult<object> { Success = false, Message = $"Measure failed: {ex.Message}" };
            }
            finally
            {
                TaskCompleted = true;
                _resetEvent.Set();
            }
        }

        private XYZ ResolvePoint(Document doc, long elementId, double[] point)
        {
            if (point != null && point.Length >= 3)
            {
                return new XYZ(
                    ConvertToFeet(point[0]),
                    ConvertToFeet(point[1]),
                    ConvertToFeet(point[2])
                );
            }

            if (elementId > 0)
            {
                var element = doc.GetElement(ToElementId(elementId));
                if (element == null) throw new ArgumentException($"Element {elementId} not found");

                var bb = element.get_BoundingBox(null);
                if (bb != null)
                    return (bb.Min + bb.Max) / 2.0;

                if (element.Location is LocationPoint lp) return lp.Point;
                if (element.Location is LocationCurve lc) return lc.Curve.Evaluate(0.5, true);

                throw new ArgumentException($"Element {elementId} has no measurable geometry");
            }

            return null;
        }

        private static double ConvertToMm(double feet)
        {
#if REVIT2022_OR_GREATER
            return UnitUtils.ConvertFromInternalUnits(feet, UnitTypeId.Millimeters);
#else
            return UnitUtils.ConvertFromInternalUnits(feet, DisplayUnitType.DUT_MILLIMETERS);
#endif
        }

        private static double ConvertToFeet(double mm)
        {
#if REVIT2022_OR_GREATER
            return UnitUtils.ConvertToInternalUnits(mm, UnitTypeId.Millimeters);
#else
            return UnitUtils.ConvertToInternalUnits(mm, DisplayUnitType.DUT_MILLIMETERS);
#endif
        }

        private static ElementId ToElementId(long id)
        {
#if REVIT2024_OR_GREATER
            return new ElementId(id);
#else
            return new ElementId((int)id);
#endif
        }

        private object FormatPoint(XYZ p)
        {
            return new
            {
                x = Math.Round(ConvertToMm(p.X), 1),
                y = Math.Round(ConvertToMm(p.Y), 1),
                z = Math.Round(ConvertToMm(p.Z), 1)
            };
        }

        public string GetName() => "Measure Between Elements";
    }
}
