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
#if REVIT2024_OR_GREATER
        public static long GetValue(this ElementId id) => id.Value;
#else
        public static long GetValue(this ElementId id) => id.IntegerValue;
#endif

        /// <summary>
        /// Gets the numeric value of an ElementId as an int, compatible with all Revit versions.
        /// Use this when you need an int (e.g., for serialization to existing schemas).
        /// </summary>
#if REVIT2024_OR_GREATER
        public static int GetIntValue(this ElementId id) => (int)id.Value;
#else
        public static int GetIntValue(this ElementId id) => id.IntegerValue;
#endif

        /// <summary>
        /// Creates an ElementId from a long value, compatible with all Revit versions.
        /// </summary>
#if REVIT2024_OR_GREATER
        public static ElementId FromLong(long id) => new ElementId(id);
#else
        public static ElementId FromLong(long id) => new ElementId((int)id);
#endif
    }
}
