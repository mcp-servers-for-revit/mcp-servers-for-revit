using System.Globalization;

namespace RevitMCPCommandSet.Utils
{
    /// <summary>
    /// The basic properties reported for an element by get_current_view_elements and get_selected_elements.
    /// Coordinates and lengths are in mm.
    /// </summary>
    public static class ElementProperties
    {
        public static Dictionary<string, string> GetBasic(Element element)
        {
            var properties = new Dictionary<string, string>
            {
                ["ElementId"] = element.Id.GetValue().ToString()
            };

            if (element.Location is LocationPoint locationPoint)
            {
                XYZ point = locationPoint.Point;
                properties["LocationX"] = Mm(point.X);
                properties["LocationY"] = Mm(point.Y);
                properties["LocationZ"] = Mm(point.Z);
            }
            else if (element.Location is LocationCurve locationCurve)
            {
                Curve curve = locationCurve.Curve;
                properties["Start"] = MmPoint(curve.GetEndPoint(0));
                properties["End"] = MmPoint(curve.GetEndPoint(1));
                properties["Length"] = Mm(curve.Length);
            }

            // The "Family" and "Type" parameters hold element ids; report the names instead.
            if (element.Document.GetElement(element.GetTypeId()) is ElementType type)
            {
                properties["Family"] = type.FamilyName;
                properties["Type"] = type.Name;
            }

            if (element.Document.GetElement(element.LevelId) is Level level)
            {
                properties["Level"] = level.Name;
            }

            foreach (var paramName in new[] { "Comments", "Mark" })
            {
                Parameter param = element.LookupParameter(paramName);
                if (param != null && param.StorageType == StorageType.String)
                {
                    properties[paramName] = param.AsString() ?? "";
                }
            }

            return properties;
        }

        private static string Mm(double feet) =>
            UnitConversion.ToMillimeters(feet).ToString("F1", CultureInfo.InvariantCulture);

        private static string MmPoint(XYZ point) => $"{Mm(point.X)}, {Mm(point.Y)}, {Mm(point.Z)}";
    }
}
