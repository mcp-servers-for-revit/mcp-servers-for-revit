namespace RevitMCPCommandSet.Utils
{
    /// <summary>
    /// Works out the base and top levels (and offsets from them) for a new line- or point-based element.
    /// </summary>
    public static class LevelPlacement
    {
        /// <param name="levelId">Level to place on, or -1 to use the level nearest <paramref name="baseLevelMm"/>.</param>
        /// <param name="baseLevelMm">Elevation in mm; only used when no levelId is given (the older way of choosing a level).</param>
        /// <param name="baseOffsetMm">Offset in mm above the base level.</param>
        /// <param name="heightMm">Element height in mm, used to find the top level.</param>
        /// <returns>Offsets are in feet, Revit's internal unit.</returns>
        public static (Level BaseLevel, double BaseOffset, Level TopLevel, double TopOffset) Resolve(
            Document doc, long levelId, double baseLevelMm, double baseOffsetMm, double heightMm)
        {
            Level baseLevel;
            double baseElevation;
            if (levelId > 0)
            {
                baseLevel = doc.GetElement(levelId.ToRevitElementId()) as Level
                    ?? throw new ArgumentException($"levelId {levelId} is not a level in this model.");
                baseElevation = baseLevel.Elevation + baseOffsetMm / 304.8;
            }
            else
            {
                baseLevel = doc.FindNearestLevel(baseLevelMm / 304.8)
                    ?? throw new InvalidOperationException("The model has no levels.");
                baseElevation = (baseLevelMm + baseOffsetMm) / 304.8;
            }

            double topElevation = baseElevation + heightMm / 304.8;
            Level topLevel = doc.FindNearestLevel(topElevation);

            return (baseLevel, baseElevation - baseLevel.Elevation, topLevel, topElevation - topLevel.Elevation);
        }
    }
}
