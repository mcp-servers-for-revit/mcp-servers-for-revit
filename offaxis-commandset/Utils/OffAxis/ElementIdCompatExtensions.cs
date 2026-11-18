using Autodesk.Revit.DB;

namespace RevitMCPCommandSet.Utils.OffAxis
{
    /// <summary>
    /// Revit 2025+ removed ElementId.IntegerValue (long via .Value).
    /// Supplies an int-returning compat accessor used across the toolkit;
    /// mirrors RevitMCPCommandSet.Utils.ElementIdExtensions.
    /// </summary>
    public static class ElementIdCompatExtensions
    {
#if REVIT2024_OR_GREATER
        public static int GetIntValue(this ElementId id) => (int)id.Value;
#else
        public static int GetIntValue(this ElementId id) => id.IntegerValue;
#endif
    }
}
