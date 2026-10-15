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
        /// Gets the numeric value of an ElementId as an int. Only for built-in category and parameter ids,
        /// which are always small negative numbers, e.g. to cast to <see cref="BuiltInCategory"/>.
        /// Element ids can exceed the int range in Revit 2024+, so use <see cref="GetValue"/> for those.
        /// </summary>
#if REVIT2024_OR_GREATER
        public static int GetIntValue(this ElementId id) => (int)id.Value;
#else
        public static int GetIntValue(this ElementId id) => id.IntegerValue;
#endif

        /// <summary>
        /// Creates an ElementId from a numeric value, compatible with all Revit versions.
        /// Named to avoid Nice3point's ToElementId (parameter type varies by Revit version) and Parameter.AsElementId.
        /// </summary>
#if REVIT2024_OR_GREATER
        public static ElementId ToRevitElementId(this long value) => new ElementId(value);
#else
        public static ElementId ToRevitElementId(this long value) => new ElementId(checked((int)value));
#endif
    }
}
