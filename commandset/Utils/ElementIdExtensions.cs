using Autodesk.Revit.DB;

namespace RevitMCPCommandSet.Utils
{
    /// <summary>
    /// Extension methods for ElementId to handle API differences between Revit versions.
    /// In Revit 2024+, ElementId.Value returns long.
    /// In earlier versions, ElementId.IntegerValue returns int.
    /// </summary>
    public static class ElementIdExtensions
    {
        /// <summary>
        /// Gets the numeric value of an ElementId as a long, compatible with all Revit versions.
        /// </summary>
        public static long GetValue(this ElementId id) => id.Value;

        /// <summary>
        /// Gets the numeric value of an ElementId as an int, compatible with all Revit versions.
        /// Use this when you need an int (e.g., for serialization to existing schemas).
        /// </summary>
        public static int GetIntValue(this ElementId id) => (int)id.Value;
    }
}
