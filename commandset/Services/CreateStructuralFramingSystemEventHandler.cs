using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using RevitMCPCommandSet.Models.Common;
using RevitMCPCommandSet.Models.Structure;
using RevitMCPCommandSet.Utils;
using RevitMCPSDK.API.Interfaces;

namespace RevitMCPCommandSet.Services
{
    public class CreateStructuralFramingSystemEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        private UIApplication uiApp;
        private UIDocument uiDoc => uiApp.ActiveUIDocument;
        private Document doc => uiDoc.Document;

        /// <summary>
        /// Event synchronization object
        /// </summary>
        private readonly ManualResetEvent _resetEvent = new ManualResetEvent(false);

        /// <summary>
        /// Beam system creation parameters
        /// </summary>
        public StructuralFramingSystemCreationInfo Parameters { get; private set; }

        /// <summary>
        /// Execution result
        /// </summary>
        public AIResult<StructuralFramingSystemCreationResult> Result { get; private set; }

        /// <summary>
        /// Set parameters for beam system creation
        /// </summary>
        public void SetParameters(StructuralFramingSystemCreationInfo parameters)
        {
            Parameters = parameters;
            _resetEvent.Reset();
        }

        public void Execute(UIApplication uiapp)
        {
            uiApp = uiapp;

            try
            {
                // Validate parameters
                if (!Parameters.Validate(out string validationError))
                {
                    Result = new AIResult<StructuralFramingSystemCreationResult>
                    {
                        Success = false,
                        Message = $"Validation failed: {validationError}",
                        Response = null
                    };
                    return;
                }

                // Pre-flight checks
                List<string> warnings = new List<string>();

                // Check active view type
                View activeView = uiDoc.ActiveView;
                if (activeView.ViewType != ViewType.FloorPlan && activeView.ViewType != ViewType.EngineeringPlan)
                {
                    warnings.Add($"Active view is {activeView.ViewType}. BeamSystem creation works best in Floor Plan or Structural Plan views.");
                }

                // Check if structural framing families exist
                int beamTypeCount = new FilteredElementCollector(doc)
                    .OfClass(typeof(FamilySymbol))
                    .OfCategory(BuiltInCategory.OST_StructuralFraming)
                    .GetElementCount();

                if (beamTypeCount == 0)
                {
                    Result = new AIResult<StructuralFramingSystemCreationResult>
                    {
                        Success = false,
                        Message = "No structural framing families found in project. Please load beam families before creating beam systems.",
                        Response = null
                    };
                    return;
                }

                using (Transaction trans = new Transaction(doc, "Create Beam System"))
                {
                    trans.Start();

                    // 1. Resolve Level - Find nearest existing level to the target elevation (like walls do)
                    Level level = doc.FindNearestLevel(Parameters.Elevation / 304.8);
                    if (level == null)
                    {
                        // Fallback: try to find any level
                        level = new FilteredElementCollector(doc)
                            .OfClass(typeof(Level))
                            .Cast<Level>()
                            .FirstOrDefault();
                    }
                    if (level == null)
                    {
                        throw new Exception("No levels found in project. Please create at least one level before creating beam systems.");
                    }

                    // Add info about which level was selected
                    double levelElevationMm = level.Elevation * 304.8;
                    warnings.Add($"Using level '{level.Name}' at elevation {levelElevationMm:F0}mm (nearest to target elevation {Parameters.Elevation:F0}mm)");

                    // 2. Build rectangular profile (4 curves in closed loop)
                    List<Curve> profileCurves = BuildRectangularProfile(
                        Parameters.XMin, Parameters.XMax,
                        Parameters.YMin, Parameters.YMax,
                        Parameters.Elevation
                    );

                    // 3. Map direction edge to curve index
                    int curveIndexForDirection = MapDirectionEdgeToCurveIndex(Parameters.DirectionEdge);

                    // 4. Resolve beam type
                    FamilySymbol beamType = ResolveBeamType(doc, Parameters.BeamTypeName);

                    // 5. Create beam system
                    BeamSystem beamSystem = BeamSystem.Create(
                        doc,
                        profileCurves,
                        level,
                        curveIndexForDirection,
                        Parameters.Is3D
                    );

                    // 6. Set beam type
                    beamSystem.BeamType = beamType;

                    // 7. Set layout rule (fixed distance + justification)
                    LayoutRuleFixedDistance layoutRule = new LayoutRuleFixedDistance(
                        Parameters.Spacing / 304.8,  // spacing in feet
                        MapJustifyString(Parameters.Justify)  // justification type
                    );
                    beamSystem.LayoutRule = layoutRule;

                    // 7b. Force regeneration so BeamSystem creates its member beams
                    doc.Regenerate();

                    // 8. Apply elevation offset to all beams using Z_OFFSET_VALUE parameter
                    // Note: BeamSystem.Create() places beams at level elevation. We use Z_OFFSET_VALUE to offset them.
                    double elevationOffsetFt = (Parameters.Elevation / 304.8) - level.Elevation;

                    if (Math.Abs(elevationOffsetFt) > 0.001) // Only apply if there's a meaningful offset
                    {
                        ICollection<ElementId> beamIdsForElevation = beamSystem.GetBeamIds();
                        int elevationSetCount = 0;

                        foreach (ElementId beamId in beamIdsForElevation)
                        {
                            FamilyInstance beam = doc.GetElement(beamId) as FamilyInstance;
                            if (beam != null)
                            {
                                Parameter zOffset = beam.get_Parameter(BuiltInParameter.Z_OFFSET_VALUE);
                                if (zOffset != null && !zOffset.IsReadOnly)
                                {
                                    zOffset.Set(elevationOffsetFt);
                                    elevationSetCount++;
                                }
                            }
                        }

                        if (elevationSetCount > 0)
                        {
                            warnings.Add($"Applied elevation offset of {Parameters.Elevation:F0}mm to {elevationSetCount} beams");
                        }
                        else if (beamIdsForElevation.Count > 0)
                        {
                            warnings.Add("Could not apply elevation offset - Z_OFFSET_VALUE parameter not available");
                        }
                    }

                    trans.Commit();

                    // 9. Get member beam IDs
                    ICollection<ElementId> beamIds = beamSystem.GetBeamIds();
                    List<long> beamIdsList = new List<long>();

#if REVIT2024_OR_GREATER
                    long beamSystemId = beamSystem.Id.Value;
                    foreach (ElementId beamId in beamIds)
                    {
                        beamIdsList.Add(beamId.Value);
                    }
#else
                    long beamSystemId = beamSystem.Id.IntegerValue;
                    foreach (ElementId beamId in beamIds)
                    {
                        beamIdsList.Add(beamId.IntegerValue);
                    }
#endif

                    // 10. Read actual spacing from layout rule
                    double actualSpacing = layoutRule.Spacing * 304.8;  // ft to mm

                    // 11. Create result with detailed summary
                    double widthMm = Parameters.XMax - Parameters.XMin;
                    double heightMm = Parameters.YMax - Parameters.YMin;

                    string successMessage = $"✓ Successfully created beam system on '{level.Name}'\n" +
                        $"  • Beam count: {beamIdsList.Count}\n" +
                        $"  • Beam type: {beamType.Family.Name}: {beamType.Name}\n" +
                        $"  • Coverage area: {widthMm:F0}mm × {heightMm:F0}mm\n" +
                        $"  • Actual spacing: {actualSpacing:F0}mm\n" +
                        $"  • Direction: Perpendicular to {Parameters.DirectionEdge} edge\n" +
                        $"  • Justification: {Parameters.Justify}";

                    // Add warnings if any
                    if (warnings.Count > 0)
                    {
                        successMessage += "\n\n⚠ Warnings:\n  • " + string.Join("\n  • ", warnings);
                    }

                    Result = new AIResult<StructuralFramingSystemCreationResult>
                    {
                        Success = true,
                        Message = successMessage,
                        Response = new StructuralFramingSystemCreationResult
                        {
                            BeamSystemId = beamSystemId,
                            BeamIds = beamIdsList,
                            BeamCount = beamIdsList.Count,
                            ActualSpacing = actualSpacing,
                            UsedBeamTypeName = $"{beamType.Family.Name}: {beamType.Name}",
                            LevelName = level.Name
                        }
                    };
                }
            }
            catch (Exception ex)
            {
                // Build detailed error message with troubleshooting
                string errorDetails = $"Failed to create beam system: {ex.Message}";

                // Add troubleshooting hints based on common errors
                if (ex.Message.Contains("level") || ex.Message.Contains("Level"))
                {
                    errorDetails += "\n\nTroubleshooting: Check that the level name is correct and exists in the project.";
                }
                else if (ex.Message.Contains("beam") || ex.Message.Contains("family"))
                {
                    errorDetails += "\n\nTroubleshooting: Ensure structural framing families are loaded in the project.";
                }
                else if (ex.Message.Contains("curve") || ex.Message.Contains("boundary"))
                {
                    errorDetails += "\n\nTroubleshooting: Verify boundary coordinates form a valid rectangle (xMin < xMax, yMin < yMax).";
                }
                else if (ex.Message.Contains("view") || ex.Message.Contains("3D"))
                {
                    errorDetails += "\n\nTroubleshooting: BeamSystem creation may require a floor plan view to be active.";
                }

                // Add parameter summary for debugging
                errorDetails += $"\n\nParameters used:\n" +
                    $"- Level: {Parameters.LevelName}\n" +
                    $"- Boundary: X({Parameters.XMin}, {Parameters.XMax}), Y({Parameters.YMin}, {Parameters.YMax})\n" +
                    $"- Spacing: {Parameters.Spacing}mm\n" +
                    $"- Direction: {Parameters.DirectionEdge}\n" +
                    $"- Beam Type: {Parameters.BeamTypeName ?? "Auto-select"}";

                // Suggest fallback approach
                errorDetails += "\n\n💡 Alternative: If BeamSystem creation fails, consider using 'create_line_based_element' " +
                    "to create individual structural framing beams in a loop.";

                Result = new AIResult<StructuralFramingSystemCreationResult>
                {
                    Success = false,
                    Message = errorDetails,
                    Response = null
                };
            }
            finally
            {
                _resetEvent.Set(); // Signal completion
            }
        }

        /// <summary>
        /// Resolve level by name, with fallback to active view's level, or create if doesn't exist
        /// </summary>
        private Level ResolveLevel(Document doc, string levelName, List<string> warnings)
        {
            // Try exact name match first
            Level level = new FilteredElementCollector(doc)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .FirstOrDefault(l => l.Name.Equals(levelName, StringComparison.OrdinalIgnoreCase));

            if (level != null) return level;

            // Try to auto-create level if name follows pattern like "Level 2", "Level 10", etc.
            if (TryCreateLevelFromName(doc, levelName, out Level createdLevel, out string creationMessage))
            {
                warnings.Add(creationMessage);
                return createdLevel;
            }

            // Fallback to active view's level
            if (uiDoc.ActiveView is ViewPlan viewPlan)
            {
                level = viewPlan.GenLevel;
                if (level != null)
                {
                    warnings.Add($"Level '{levelName}' not found. Using active view's level '{level.Name}' instead.");
                    return level;
                }
            }

            // If still null, throw with available levels
            var availableLevels = new FilteredElementCollector(doc)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .Select(l => l.Name)
                .ToList();

            throw new Exception($"Level '{levelName}' not found. Available levels: {string.Join(", ", availableLevels)}");
        }

        /// <summary>
        /// Try to create a level from a name pattern like "Level 2", "Level 10", etc.
        /// </summary>
        private bool TryCreateLevelFromName(Document doc, string levelName, out Level createdLevel, out string message)
        {
            createdLevel = null;
            message = string.Empty;

            // Try to extract level number from name (e.g., "Level 2" → 2, "Level 10" → 10)
            var match = System.Text.RegularExpressions.Regex.Match(levelName, @"(?i)level\s*(\d+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (!match.Success)
            {
                return false; // Name doesn't follow "Level N" pattern
            }

            int levelNumber = int.Parse(match.Groups[1].Value);

            // Calculate elevation based on standard floor-to-floor height (4000mm = 13.123 ft)
            const double standardFloorHeight = 4000.0 / 304.8; // 4m in feet
            double elevation = levelNumber * standardFloorHeight;

            // Check if a level already exists at this elevation
            Level existingAtElevation = new FilteredElementCollector(doc)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .FirstOrDefault(l => Math.Abs(l.Elevation - elevation) < 0.01); // Within 0.01 ft tolerance

            if (existingAtElevation != null)
            {
                createdLevel = existingAtElevation;
                message = $"Found existing level '{existingAtElevation.Name}' at elevation {levelNumber * 4000}mm (used instead of creating '{levelName}')";
                return true;
            }

            // Create the new level
            try
            {
                // 1. Create the level
                createdLevel = Level.Create(doc, elevation);
                createdLevel.Name = levelName;

                // 2. Create associated floor plan view (REQUIRED for BeamSystem.Create)
                // Find the ViewFamilyType for floor plans
                ViewFamilyType floorPlanVFT = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewFamilyType))
                    .Cast<ViewFamilyType>()
                    .FirstOrDefault(vft => vft.ViewFamily == ViewFamily.FloorPlan);

                if (floorPlanVFT != null)
                {
                    // Create floor plan view associated with this level
                    ViewPlan floorPlan = ViewPlan.Create(doc, floorPlanVFT.Id, createdLevel.Id);
                    floorPlan.Name = levelName; // Match view name to level name

                    message = $"✨ Auto-created level '{levelName}' at elevation {levelNumber * 4000}mm with associated floor plan view (floor-to-floor height: 4000mm)";
                }
                else
                {
                    message = $"⚠ Created level '{levelName}' at elevation {levelNumber * 4000}mm but couldn't create floor plan view (no ViewFamilyType found). BeamSystem creation may fail.";
                }

                return true;
            }
            catch (Exception ex)
            {
                message = $"Failed to auto-create level '{levelName}': {ex.Message}";
                return false;
            }
        }

        /// <summary>
        /// Build rectangular profile from boundary coordinates
        /// </summary>
        private List<Curve> BuildRectangularProfile(double xMin, double xMax, double yMin, double yMax, double elevation)
        {
            // Convert mm to feet
            double xMinFt = xMin / 304.8;
            double xMaxFt = xMax / 304.8;
            double yMinFt = yMin / 304.8;
            double yMaxFt = yMax / 304.8;
            double elevFt = elevation / 304.8;

            List<Curve> curves = new List<Curve>();

            // Create 4 lines in order: bottom, right, top, left
            // Bottom edge (y=yMin, x from min to max)
            XYZ p0 = new XYZ(xMinFt, yMinFt, elevFt);
            XYZ p1 = new XYZ(xMaxFt, yMinFt, elevFt);
            curves.Add(Line.CreateBound(p0, p1));

            // Right edge (x=xMax, y from min to max)
            XYZ p2 = new XYZ(xMaxFt, yMaxFt, elevFt);
            curves.Add(Line.CreateBound(p1, p2));

            // Top edge (y=yMax, x from max to min)
            XYZ p3 = new XYZ(xMinFt, yMaxFt, elevFt);
            curves.Add(Line.CreateBound(p2, p3));

            // Left edge (x=xMin, y from max to min)
            curves.Add(Line.CreateBound(p3, p0));

            return curves;
        }

        /// <summary>
        /// Map direction edge string to curve index in profile
        /// </summary>
        private int MapDirectionEdgeToCurveIndex(string directionEdge)
        {
            switch (directionEdge.ToLower())
            {
                case "bottom": return 0;
                case "right": return 1;
                case "top": return 2;
                case "left": return 3;
                default: return 0;
            }
        }

        /// <summary>
        /// Resolve beam type by name, with fallback to first available
        /// </summary>
        private FamilySymbol ResolveBeamType(Document doc, string beamTypeName)
        {
            FamilySymbol beamType = null;

            if (!string.IsNullOrEmpty(beamTypeName))
            {
                // Try to find by name (check both just name and "Family: Name" format)
                beamType = new FilteredElementCollector(doc)
                    .OfClass(typeof(FamilySymbol))
                    .OfCategory(BuiltInCategory.OST_StructuralFraming)
                    .Cast<FamilySymbol>()
                    .FirstOrDefault(fs =>
                        fs.Name.IndexOf(beamTypeName, StringComparison.OrdinalIgnoreCase) >= 0 ||
                        fs.Family.Name.IndexOf(beamTypeName, StringComparison.OrdinalIgnoreCase) >= 0 ||
                        $"{fs.Family.Name}: {fs.Name}".IndexOf(beamTypeName, StringComparison.OrdinalIgnoreCase) >= 0);
            }

            // Fallback to first active beam type
            if (beamType == null)
            {
                beamType = new FilteredElementCollector(doc)
                    .OfClass(typeof(FamilySymbol))
                    .OfCategory(BuiltInCategory.OST_StructuralFraming)
                    .Cast<FamilySymbol>()
                    .FirstOrDefault(fs => fs.IsActive);
            }

            // Fallback to first beam type (even if inactive)
            if (beamType == null)
            {
                beamType = new FilteredElementCollector(doc)
                    .OfClass(typeof(FamilySymbol))
                    .OfCategory(BuiltInCategory.OST_StructuralFraming)
                    .Cast<FamilySymbol>()
                    .FirstOrDefault();
            }

            if (beamType == null)
                throw new Exception("No structural framing beam types found in project. Please load a beam family.");

            // Activate if not already active
            if (!beamType.IsActive)
                beamType.Activate();

            return beamType;
        }

        /// <summary>
        /// Map justify string to BeamSystemJustifyType enum
        /// </summary>
        private BeamSystemJustifyType MapJustifyString(string justify)
        {
            switch (justify.ToLower())
            {
                case "beginning": return BeamSystemJustifyType.Beginning;
                case "center": return BeamSystemJustifyType.Center;
                case "end": return BeamSystemJustifyType.End;
                case "directionline": return BeamSystemJustifyType.DirectionLine;
                default: return BeamSystemJustifyType.Center;
            }
        }

        /// <summary>
        /// Wait for beam system creation to complete
        /// </summary>
        public bool WaitForCompletion(int timeoutMilliseconds = 15000)
        {
            _resetEvent.Reset();
        return _resetEvent.WaitOne(timeoutMilliseconds);
        }

        /// <summary>
        /// Get handler name
        /// </summary>
        public string GetName()
        {
            return "Create Structural Framing System";
        }
    }
}
