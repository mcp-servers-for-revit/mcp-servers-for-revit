using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RevitMCPCommandSet.Models.Common
{
    /// <summary>
    /// Filter settings - supports combined condition filtering
    /// </summary>
    public class FilterSetting
    {
        /// <summary>
        /// Gets or sets the Revit built-in category name to filter by (e.g. "OST_Walls").
        /// If null or empty, no category filtering is applied.
        /// </summary>
        [JsonProperty("filterCategory")]
        public string FilterCategory { get; set; } = null;
        /// <summary>
        /// Gets or sets the Revit element type name to filter by (e.g. "Wall" or "Autodesk.Revit.DB.Wall").
        /// If null or empty, no type filtering is applied.
        /// </summary>
        [JsonProperty("filterElementType")]
        public string FilterElementType { get; set; } = null;
        /// <summary>
        /// Gets or sets the ElementId value of the family type (FamilySymbol) to filter by.
        /// If 0 or negative, no family filtering is applied.
        /// Note: This filter only applies to element instances, not to type elements.
        /// </summary>
        [JsonProperty("filterFamilySymbolId")]
        public int FilterFamilySymbolId { get; set; } = -1;
        /// <summary>
        /// Gets or sets whether to include element types (e.g. wall types, door types, etc.)
        /// </summary>
        [JsonProperty("includeTypes")]
        public bool IncludeTypes { get; set; } = false;
        /// <summary>
        /// Gets or sets whether to include element instances (e.g. placed walls, doors, etc.)
        /// </summary>
        [JsonProperty("includeInstances")]
        public bool IncludeInstances { get; set; } = true;
        /// <summary>
        /// Gets or sets whether to return only elements visible in the current view.
        /// Note: This filter only applies to element instances, not to type elements.
        /// </summary>
        [JsonProperty("filterVisibleInCurrentView")]
        public bool FilterVisibleInCurrentView { get; set; }
        /// <summary>
        /// Gets or sets the minimum point coordinates for spatial range filtering (unit: mm).
        /// If this value and BoundingBoxMax are set, elements intersecting this bounding box will be filtered.
        /// </summary>
        [JsonProperty("boundingBoxMin")]
        public JZPoint BoundingBoxMin { get; set; } = null;
        /// <summary>
        /// Gets or sets the maximum point coordinates for spatial range filtering (unit: mm).
        /// If this value and BoundingBoxMin are set, elements intersecting this bounding box will be filtered.
        /// </summary>
        [JsonProperty("boundingBoxMax")]
        public JZPoint BoundingBoxMax { get; set; } = null;
        /// <summary>
        /// Maximum element count limit
        /// </summary>
        [JsonProperty("maxElements")]
        public int MaxElements { get; set; } = 50;
        /// <summary>
        /// Validates the filter settings for correctness, checking for potential conflicts
        /// </summary>
        /// <returns>Returns true if the settings are valid, otherwise returns false</returns>
        public bool Validate(out string errorMessage)
        {
            errorMessage = null;

            // Check that at least one element kind is selected
            if (!IncludeTypes && !IncludeInstances)
            {
                errorMessage = "Invalid filter settings: must include at least one of element types or element instances";
                return false;
            }

            // Check that at least one filter condition is specified
            if (string.IsNullOrWhiteSpace(FilterCategory) &&
                string.IsNullOrWhiteSpace(FilterElementType) &&
                FilterFamilySymbolId <= 0)
            {
                errorMessage = "Invalid filter settings: must specify at least one filter condition (category, element type, or family type)";
                return false;
            }

            // Check for conflicts between type elements and certain filters
            if (IncludeTypes && !IncludeInstances)
            {
                List<string> invalidFilters = new List<string>();
                if (FilterFamilySymbolId > 0)
                    invalidFilters.Add("family instance filter");
                if (FilterVisibleInCurrentView)
                    invalidFilters.Add("view visibility filter");
                if (invalidFilters.Count > 0)
                {
                    errorMessage = $"When filtering type elements only, the following filters are not applicable: {string.Join(", ", invalidFilters)}";
                    return false;
                }
            }
            // Check validity of the spatial range filter
            if (BoundingBoxMin != null && BoundingBoxMax != null)
            {
                // Ensure minimum point is less than or equal to maximum point
                if (BoundingBoxMin.X > BoundingBoxMax.X ||
                    BoundingBoxMin.Y > BoundingBoxMax.Y ||
                    BoundingBoxMin.Z > BoundingBoxMax.Z)
                {
                    errorMessage = "Invalid spatial range filter settings: minimum point coordinates must be less than or equal to maximum point coordinates";
                    return false;
                }
            }
            else if (BoundingBoxMin != null || BoundingBoxMax != null)
            {
                errorMessage = "Invalid spatial range filter settings: both minimum and maximum point coordinates must be set";
                return false;
            }
            return true;
        }
    }
}
