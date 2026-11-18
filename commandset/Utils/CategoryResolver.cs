using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RevitMCPCommandSet.Utils
{
    /// <summary>
    /// Language-independent category resolution.
    /// Accepts English names, OST_ enum names, BuiltInCategory names, and localized display names.
    /// </summary>
    public static class CategoryResolver
    {
        // English friendly-name → BuiltInCategory mapping
        private static readonly Dictionary<string, BuiltInCategory> FriendlyNameMap =
            new Dictionary<string, BuiltInCategory>(StringComparer.OrdinalIgnoreCase)
            {
                { "Walls", BuiltInCategory.OST_Walls },
                { "Floors", BuiltInCategory.OST_Floors },
                { "Roofs", BuiltInCategory.OST_Roofs },
                { "Doors", BuiltInCategory.OST_Doors },
                { "Windows", BuiltInCategory.OST_Windows },
                { "Rooms", BuiltInCategory.OST_Rooms },
                { "Columns", BuiltInCategory.OST_Columns },
                { "StructuralColumns", BuiltInCategory.OST_StructuralColumns },
                { "Beams", BuiltInCategory.OST_StructuralFraming },
                { "StructuralFraming", BuiltInCategory.OST_StructuralFraming },
                { "Stairs", BuiltInCategory.OST_Stairs },
                { "Railings", BuiltInCategory.OST_StairsRailing },
                { "Ceilings", BuiltInCategory.OST_Ceilings },
                { "Furniture", BuiltInCategory.OST_Furniture },
                { "FurnitureSystems", BuiltInCategory.OST_FurnitureSystems },
                { "Casework", BuiltInCategory.OST_Casework },
                { "Plumbing", BuiltInCategory.OST_PlumbingFixtures },
                { "PlumbingFixtures", BuiltInCategory.OST_PlumbingFixtures },
                { "MechanicalEquipment", BuiltInCategory.OST_MechanicalEquipment },
                { "ElectricalFixtures", BuiltInCategory.OST_ElectricalFixtures },
                { "ElectricalEquipment", BuiltInCategory.OST_ElectricalEquipment },
                { "LightingFixtures", BuiltInCategory.OST_LightingFixtures },
                { "GenericModels", BuiltInCategory.OST_GenericModel },
                { "GenericModel", BuiltInCategory.OST_GenericModel },
                { "Areas", BuiltInCategory.OST_Areas },
                { "Spaces", BuiltInCategory.OST_MEPSpaces },
                { "MEPSpaces", BuiltInCategory.OST_MEPSpaces },
                { "CurtainPanels", BuiltInCategory.OST_CurtainWallPanels },
                { "Curtain Panels", BuiltInCategory.OST_CurtainWallPanels },
                { "CurtainWallPanels", BuiltInCategory.OST_CurtainWallPanels },
                { "Mullions", BuiltInCategory.OST_CurtainWallMullions },
                { "CurtainWallMullions", BuiltInCategory.OST_CurtainWallMullions },
                { "Grids", BuiltInCategory.OST_Grids },
                { "Levels", BuiltInCategory.OST_Levels },
                { "Views", BuiltInCategory.OST_Views },
                { "Sheets", BuiltInCategory.OST_Sheets },
                { "Pipes", BuiltInCategory.OST_PipeCurves },
                { "PipeCurves", BuiltInCategory.OST_PipeCurves },
                { "Ducts", BuiltInCategory.OST_DuctCurves },
                { "DuctCurves", BuiltInCategory.OST_DuctCurves },
                { "CableTray", BuiltInCategory.OST_CableTray },
                { "Conduit", BuiltInCategory.OST_Conduit },
                { "Parking", BuiltInCategory.OST_Parking },
                { "Topography", BuiltInCategory.OST_Topography },
                { "Site", BuiltInCategory.OST_Site },
                { "Ramps", BuiltInCategory.OST_Ramps },
                { "StructuralFoundation", BuiltInCategory.OST_StructuralFoundation },
                { "Foundations", BuiltInCategory.OST_StructuralFoundation },
            };

        /// <summary>
        /// Resolve a category name to a BuiltInCategory, language-independently.
        /// Tries: OST_ enum → friendly English name → localized display name match.
        /// </summary>
        public static BuiltInCategory? Resolve(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;

            // 1. Try OST_ enum parse (e.g., "OST_Walls" or "Walls" → OST_Walls)
            string bicName = name.StartsWith("OST_") ? name : "OST_" + name;
            if (Enum.TryParse<BuiltInCategory>(bicName, true, out var bic))
                return bic;

            // 2. Try friendly English name map
            if (FriendlyNameMap.TryGetValue(name, out var mapped))
                return mapped;

            return null;
        }

        /// <summary>
        /// Resolve a category name to an ElementId using document categories.
        /// Falls back to localized display name matching if enum resolution fails.
        /// </summary>
        public static ElementId ResolveToId(Document doc, string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;

            // 1. Try enum-based resolution first (language-independent)
            var bic = Resolve(name);
            if (bic.HasValue)
            {
                var cat = doc.Settings.Categories.Cast<Category>()
                    .FirstOrDefault(c => c.Id.Equals(new ElementId(bic.Value)));
                if (cat != null) return cat.Id;
            }

            // 2. Fallback: match localized display name (case-insensitive)
            var displayMatch = doc.Settings.Categories.Cast<Category>()
                .FirstOrDefault(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (displayMatch != null) return displayMatch.Id;

            // 3. Last resort: partial/contains match
            var partialMatch = doc.Settings.Categories.Cast<Category>()
                .FirstOrDefault(c => c.Name.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0);
            return partialMatch?.Id;
        }

        /// <summary>
        /// Resolve a category name to a Category object.
        /// </summary>
        public static Category ResolveToCategory(Document doc, string name)
        {
            var id = ResolveToId(doc, name);
            if (id == null) return null;
            return doc.Settings.Categories.Cast<Category>().FirstOrDefault(c => c.Id.Equals(id));
        }

        /// <summary>
        /// Check if an element's category matches the given name (language-independent).
        /// </summary>
        public static bool CategoryMatches(Document doc, Element element, string categoryName)
        {
            if (element?.Category == null || string.IsNullOrWhiteSpace(categoryName)) return false;

            var targetId = ResolveToId(doc, categoryName);
            if (targetId == null) return false;

            return element.Category.Id.Equals(targetId);
        }

        /// <summary>
        /// Collect elements by category name (language-independent).
        /// </summary>
        public static List<Element> CollectByCategory(Document doc, string categoryName)
        {
            var bic = Resolve(categoryName);
            if (bic.HasValue)
            {
                return new FilteredElementCollector(doc)
                    .OfCategory(bic.Value)
                    .WhereElementIsNotElementType()
                    .ToList();
            }

            // Fallback: resolve via display name
            var catId = ResolveToId(doc, categoryName);
            if (catId != null)
            {
                return new FilteredElementCollector(doc)
                    .OfCategoryId(catId)
                    .WhereElementIsNotElementType()
                    .ToList();
            }

            return new List<Element>();
        }
    }
}
