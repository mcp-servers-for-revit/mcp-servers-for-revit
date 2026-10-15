namespace RevitMCPCommandSet.Utils
{
    /// <summary>
    /// Converts Revit's internal units (feet) to the units the tools use: mm for lengths, m² for areas and m³ for volumes.
    /// Plain factors rather than UnitUtils, whose unit type API differs between Revit 2020 and 2021+.
    /// </summary>
    public static class UnitConversion
    {
        public static double ToMillimeters(double feet) => feet * 304.8;

        public static double ToSquareMeters(double squareFeet) => squareFeet * 0.09290304;

        public static double ToCubicMeters(double cubicFeet) => cubicFeet * 0.028316846592;
    }
}
