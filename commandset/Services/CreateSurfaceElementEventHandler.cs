using Autodesk.Revit.UI;
using RevitMCPCommandSet.Models.Common;
using RevitMCPCommandSet.Utils;
using RevitMCPSDK.API.Interfaces;

namespace RevitMCPCommandSet.Services
{
    public class CreateSurfaceElementEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        private UIApplication uiApp;
        private UIDocument uiDoc => uiApp.ActiveUIDocument;
        private Document doc => uiDoc.Document;
        private Autodesk.Revit.ApplicationServices.Application app => uiApp.Application;
        /// <summary>
        /// Event wait object
        /// </summary>
        private readonly ManualResetEvent _resetEvent = new ManualResetEvent(false);
        /// <summary>
        /// Input data
        /// </summary>
        public List<SurfaceElement> CreatedInfo { get; private set; }
        /// <summary>
        /// Execution result (output data)
        /// </summary>
        public AIResult<List<int>> Result { get; private set; }
        public string _floorName = "Generic - ";
        public bool _structural = true;
        private List<string> _warnings = new List<string>();

        /// <summary>
        /// Sets the creation parameters
        /// </summary>
        public void SetParameters(List<SurfaceElement> data)
        {
            CreatedInfo = data;
            _resetEvent.Reset();
        }
        public void Execute(UIApplication uiapp)
        {
            uiApp = uiapp;

            try
            {
                var elementIds = new List<int>();
                _warnings.Clear();
                foreach (var data in CreatedInfo)
                {
                    int requestedTypeId = data.TypeId;
                    // Step0 Get the element category
                    BuiltInCategory builtInCategory = BuiltInCategory.INVALID;
                    Enum.TryParse(data.Category.Replace(".", "").Replace("BuiltInCategory", ""), true, out builtInCategory);

                    // Step1 Get the levels and offsets
                    Level baseLevel = null;
                    Level topLevel = null;
                    double topOffset = -1;  // ft
                    double baseOffset = -1; // ft
                    baseLevel = doc.FindNearestLevel(data.BaseLevel / 304.8);
                    baseOffset = (data.BaseOffset + data.BaseLevel) / 304.8 - baseLevel.Elevation;
                    topLevel = doc.FindNearestLevel((data.BaseLevel + data.BaseOffset + data.Thickness) / 304.8);
                    topOffset = (data.BaseLevel + data.BaseOffset + data.Thickness) / 304.8 - topLevel.Elevation;
                    if (baseLevel == null)
                        continue;

                    // Step2 Get the family type
                    FamilySymbol symbol = null;
                    FloorType floorType = null;
                    RoofType roofType = null;
                    CeilingType ceilingType = null;
                    if (data.TypeId != -1 && data.TypeId != 0)
                    {
                        ElementId typeELeId = new ElementId(data.TypeId);
                        if (typeELeId != null)
                        {
                            Element typeEle = doc.GetElement(typeELeId);
                            if (typeEle != null && typeEle is FamilySymbol)
                            {
                                symbol = typeEle as FamilySymbol;
                                // Get the symbol's Category object and convert to the BuiltInCategory enum
                                builtInCategory = (BuiltInCategory)symbol.Category.Id.GetIntValue();
                            }
                            else if (typeEle != null && typeEle is FloorType)
                            {
                                floorType = typeEle as FloorType;
                                builtInCategory = (BuiltInCategory)floorType.Category.Id.GetIntValue();
                            }
                            else if (typeEle != null && typeEle is RoofType)
                            {
                                roofType = typeEle as RoofType;
                                builtInCategory = (BuiltInCategory)roofType.Category.Id.GetIntValue();
                            }
                            else if (typeEle != null && typeEle is CeilingType)
                            {
                                ceilingType = typeEle as CeilingType;
                                builtInCategory = (BuiltInCategory)ceilingType.Category.Id.GetIntValue();
                            }
                        }
                    }
                    if (builtInCategory == BuiltInCategory.INVALID)
                        continue;
                    switch (builtInCategory)
                    {
                        case BuiltInCategory.OST_Floors:
                            if (floorType == null)
                            {
                                // Requested typeId was invalid or not provided, fall back to first available
                                floorType = new FilteredElementCollector(doc)
                                    .OfClass(typeof(FloorType))
                                    .OfCategory(BuiltInCategory.OST_Floors)
                                    .Cast<FloorType>()
                                    .FirstOrDefault();
                                if (floorType == null)
                                {
                                    _warnings.Add($"No floor types available in project.");
                                    continue;
                                }
                                if (requestedTypeId != -1 && requestedTypeId != 0)
                                {
                                    _warnings.Add($"Requested floor typeId {requestedTypeId} not found. Defaulted to '{floorType.Name}' (ID: {floorType.Id.GetIntValue()})");
                                }
                            }
                            break;
                        case BuiltInCategory.OST_Roofs:
                            if (roofType == null)
                            {
                                // Get default roof type if not specified
                                roofType = new FilteredElementCollector(doc)
                                    .OfClass(typeof(RoofType))
                                    .OfCategory(BuiltInCategory.OST_Roofs)
                                    .Cast<RoofType>()
                                    .FirstOrDefault();
                                if (roofType == null)
                                {
                                    _warnings.Add($"No roof types available in project.");
                                    continue;
                                }
                                if (requestedTypeId != -1 && requestedTypeId != 0)
                                {
                                    _warnings.Add($"Requested roof typeId {requestedTypeId} not found. Defaulted to '{roofType.Name}' (ID: {roofType.Id.GetIntValue()})");
                                }
                            }
                            break;
                        case BuiltInCategory.OST_Ceilings:
                            if (ceilingType == null)
                            {
                                // Get default ceiling type if not specified
                                ceilingType = new FilteredElementCollector(doc)
                                    .OfClass(typeof(CeilingType))
                                    .OfCategory(BuiltInCategory.OST_Ceilings)
                                    .Cast<CeilingType>()
                                    .FirstOrDefault();
                                if (ceilingType == null)
                                {
                                    _warnings.Add($"No ceiling types available in project.");
                                    continue;
                                }
                                if (requestedTypeId != -1 && requestedTypeId != 0)
                                {
                                    _warnings.Add($"Requested ceiling typeId {requestedTypeId} not found. Defaulted to '{ceilingType.Name}' (ID: {ceilingType.Id.GetIntValue()})");
                                }
                            }
                            break;
                        default:
                            if (symbol == null)
                            {
                                symbol = new FilteredElementCollector(doc)
                                    .OfClass(typeof(FamilySymbol))
                                    .OfCategory(builtInCategory)
                                    .Cast<FamilySymbol>()
                                    .FirstOrDefault(fs => fs.IsActive); // Use the active type as the default
                                if (symbol == null)
                                {
                                    symbol = new FilteredElementCollector(doc)
                                    .OfClass(typeof(FamilySymbol))
                                    .OfCategory(builtInCategory)
                                    .Cast<FamilySymbol>()
                                    .FirstOrDefault();
                                }
                            }
                            if (symbol == null)
                                continue;
                            break;
                    }

                    // Step3 Batch-create floors
                    Floor floor = null;
                    using (Transaction transaction = new Transaction(doc, "Create Surface-Based Elements"))
                    {
                        transaction.Start();

                        switch (builtInCategory)
                        {
                            case BuiltInCategory.OST_Floors:
                                CurveArray curves = new CurveArray();
                                foreach (var jzLine in data.Boundary.OuterLoop)
                                {
                                    curves.Append(JZLine.ToLine(jzLine));
                                }
                                CurveLoop curveLoop = CurveLoop.Create(data.Boundary.OuterLoop.Select(l => JZLine.ToLine(l) as Curve).ToList());

                                // Multi-version - Floor.Create introduced in Revit 2022 but stable in 2023+
#if REVIT2023_OR_GREATER
                                floor = Floor.Create(doc, new List<CurveLoop> { curveLoop }, floorType.Id, baseLevel.Id);
#else
                                floor = doc.Create.NewFloor(curves, floorType, baseLevel, _structural);
#endif
                                //Edit the floor parameters
                                if (floor != null)
                                {
                                    floor.get_Parameter(BuiltInParameter.FLOOR_HEIGHTABOVELEVEL_PARAM).Set(baseOffset);
                                    elementIds.Add(floor.Id.GetIntValue());
                                }
                                break;
                            case BuiltInCategory.OST_Roofs:
                                CurveArray roofCurves = new CurveArray();
                                foreach (var jzLine in data.Boundary.OuterLoop)
                                {
                                    roofCurves.Append(JZLine.ToLine(jzLine));
                                }

                                ModelCurveArray modelCurves = new ModelCurveArray();
                                FootPrintRoof roof = doc.Create.NewFootPrintRoof(roofCurves, baseLevel, roofType, out modelCurves);

                                if (roof != null)
                                {
                                    // Set all edges to non-sloped for flat roof
                                    foreach (ModelCurve mc in modelCurves)
                                    {
                                        roof.set_DefinesSlope(mc, false);
                                    }
                                    // Set the roof offset from level
                                    Parameter offsetParam = roof.get_Parameter(BuiltInParameter.ROOF_LEVEL_OFFSET_PARAM);
                                    if (offsetParam != null)
                                    {
                                        offsetParam.Set(baseOffset);
                                    }
                                    elementIds.Add(roof.Id.GetIntValue());
                                }
                                break;
                            case BuiltInCategory.OST_Ceilings:
                                CurveLoop ceilingCurveLoop = CurveLoop.Create(data.Boundary.OuterLoop.Select(l => JZLine.ToLine(l) as Curve).ToList());

#if REVIT2022_OR_GREATER
                                Ceiling ceiling = Ceiling.Create(doc, new List<CurveLoop> { ceilingCurveLoop }, ceilingType.Id, baseLevel.Id);
#else
                                // Ceiling.Create API not available before Revit 2022
                                Ceiling ceiling = null;
                                _warnings.Add("Ceiling creation is not supported in Revit versions before 2022.");
#endif
                                if (ceiling != null)
                                {
                                    // Set the ceiling height offset from level
                                    Parameter ceilingOffsetParam = ceiling.get_Parameter(BuiltInParameter.CEILING_HEIGHTABOVELEVEL_PARAM);
                                    if (ceilingOffsetParam != null)
                                    {
                                        ceilingOffsetParam.Set(baseOffset);
                                    }
                                    elementIds.Add(ceiling.Id.GetIntValue());
                                }
                                break;
                            default:
                                break;
                        }

                        transaction.Commit();
                    }
                }
                string message = $"Successfully created {elementIds.Count} element(s).";
                if (_warnings.Count > 0)
                {
                    message += "\n\n⚠ Warnings:\n  • " + string.Join("\n  • ", _warnings);
                }
                Result = new AIResult<List<int>>
                {
                    Success = true,
                    Message = message,
                    Response = elementIds,
                };
            }
            catch (Exception ex)
            {
                Result = new AIResult<List<int>>
                {
                    Success = false,
                    Message = $"Error creating surface-based elements: {ex.Message}",
                };
                TaskDialog.Show("Error", $"Error creating surface-based elements: {ex.Message}");
            }
            finally
            {
                _resetEvent.Set(); // Notify the waiting thread that the operation is complete
            }
        }

        /// <summary>
        /// Waits for the operation to complete
        /// </summary>
        /// <param name="timeoutMilliseconds">Timeout in milliseconds</param>
        /// <returns>Whether the operation completed before the timeout</returns>
        public bool WaitForCompletion(int timeoutMilliseconds = 10000)
        {
            _resetEvent.Reset();
            return _resetEvent.WaitOne(timeoutMilliseconds);
        }

        /// <summary>
        /// IExternalEventHandler.GetName implementation
        /// </summary>
        public string GetName()
        {
            return "Create Surface-Based Elements";
        }

        /// <summary>
        /// Gets or creates a floor type with the specified thickness
        /// </summary>
        /// <param name="thickness">Target thickness (ft)</param>
        /// <returns>A floor type meeting the thickness requirement</returns>
        private FloorType CreateOrGetFloorType(Document doc, double thickness = 200 / 304.8)
        {

            // Look for a floor type matching the thickness
            FloorType existingType = new FilteredElementCollector(doc)
                                     .OfClass(typeof(FloorType))                    // Only FloorType classes
                                     .OfCategory(BuiltInCategory.OST_Floors)        // Only floor category
                                     .Cast<FloorType>()                            // Cast to FloorType
                                     .FirstOrDefault(w => w.Name == $"{_floorName}{thickness * 304.8}mm");
            if (existingType != null)
                return existingType;
            // If no matching floor type is found, create a new one
            FloorType baseFloorType = existingType = new FilteredElementCollector(doc)
                                     .OfClass(typeof(FloorType))                    // Only FloorType classes
                                     .OfCategory(BuiltInCategory.OST_Floors)        // Only floor category
                                     .Cast<FloorType>()                            // Cast to FloorType
                                     .FirstOrDefault(w => w.Name.Contains("常规"));
            if (existingType != null)
            {
                baseFloorType = existingType = new FilteredElementCollector(doc)
                                     .OfClass(typeof(FloorType))                    // Only FloorType classes
                                     .OfCategory(BuiltInCategory.OST_Floors)        // Only floor category
                                     .Cast<FloorType>()                            // Cast to FloorType
                                     .FirstOrDefault();
            }

            // Duplicate the floor type
            FloorType newFloorType = null;
            newFloorType = baseFloorType.Duplicate($"{_floorName}{thickness * 304.8}mm") as FloorType;

            // Set the thickness of the new floor type
            // Get the layer structure
            CompoundStructure cs = newFloorType.GetCompoundStructure();
            if (cs != null)
            {
                // Get all layers
                IList<CompoundStructureLayer> layers = cs.GetLayers();
                if (layers.Count > 0)
                {
                    // Calculate the current total thickness
                    double currentTotalThickness = cs.GetWidth();

                    // Adjust each layer thickness proportionally
                    for (int i = 0; i < layers.Count; i++)
                    {
                        CompoundStructureLayer layer = layers[i];
                        double newLayerThickness = thickness;
                        cs.SetLayerWidth(i, newLayerThickness);
                    }

                    // Apply the modified layer structure
                    newFloorType.SetCompoundStructure(cs);
                }
            }
            return newFloorType;
        }

    }
}
