using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace RevitMCPCommandSet.Utils
{
    /// <summary>
    /// Creation of surface elements, compatible with all supported Revit versions.
    /// Floor.Create and Ceiling.Create arrived in Revit 2022; Revit 2020-2021 only offer
    /// Document.Create.NewFloor and have no ceiling creation API at all.
    /// </summary>
    public static class SurfaceElementCompat
    {
        /// <summary>
        /// Creates a floor from boundary loops.
        /// On Revit 2020-2021 only the first (outer) loop is used - the legacy
        /// Document.Create.NewFloor takes a single profile, so openings are ignored.
        /// </summary>
        public static Floor CreateFloor(Document doc, IList<CurveLoop> curveLoops, ElementId floorTypeId, ElementId levelId)
        {
            if (curveLoops == null || curveLoops.Count == 0)
                throw new ArgumentException("At least one boundary loop is required", nameof(curveLoops));

#if REVIT2022_OR_GREATER
            return Floor.Create(doc, curveLoops, floorTypeId, levelId);
#else
            var floorType = doc.GetElement(floorTypeId) as FloorType;
            var level = doc.GetElement(levelId) as Level;
            if (floorType == null)
                throw new ArgumentException("Invalid floor type id", nameof(floorTypeId));
            if (level == null)
                throw new ArgumentException("Invalid level id", nameof(levelId));

            var profile = new CurveArray();
            foreach (var curve in curveLoops[0])
                profile.Append(curve);

            return doc.Create.NewFloor(profile, floorType, level, false);
#endif
        }

        /// <summary>
        /// Creates a ceiling from boundary loops. Requires Revit 2022 or newer -
        /// earlier versions have no ceiling creation API.
        /// </summary>
        public static Ceiling CreateCeiling(Document doc, IList<CurveLoop> curveLoops, ElementId ceilingTypeId, ElementId levelId)
        {
#if REVIT2022_OR_GREATER
            if (curveLoops == null || curveLoops.Count == 0)
                throw new ArgumentException("At least one boundary loop is required", nameof(curveLoops));

            return Ceiling.Create(doc, curveLoops, ceilingTypeId, levelId);
#else
            throw new NotSupportedException("Creating ceilings requires Revit 2022 or newer");
#endif
        }
    }
}
