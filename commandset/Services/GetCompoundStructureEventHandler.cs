using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCPCommandSet.Models.Common;
using RevitMCPSDK.API.Interfaces;

namespace RevitMCPCommandSet.Services
{
    public class GetCompoundStructureEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        private readonly ManualResetEvent _resetEvent = new ManualResetEvent(false);

        public long? TypeId { get; set; }
        public string TypeName { get; set; }
        public string Category { get; set; }
        public AIResult<object> Result { get; private set; }

        public bool WaitForCompletion(int timeoutMilliseconds = 10000)
        {
            _resetEvent.Reset();
            return _resetEvent.WaitOne(timeoutMilliseconds);
        }

        public void Execute(UIApplication app)
        {
            try
            {
                var doc = app.ActiveUIDocument.Document;

                HostObjAttributes hostType = null;

                // Resolve the type element
                if (TypeId.HasValue)
                {
#if REVIT2024_OR_GREATER
                    var elementId = new ElementId(TypeId.Value);
#else
                    var elementId = new ElementId((int)TypeId.Value);
#endif
                    hostType = doc.GetElement(elementId) as HostObjAttributes;
                    if (hostType == null)
                        throw new Exception($"Element with id {TypeId.Value} is not a valid compound-structure type (wall, floor, roof, or ceiling type)");
                }
                else if (!string.IsNullOrEmpty(TypeName))
                {
                    var typeClass = ResolveTypeClass(Category);
                    hostType = new FilteredElementCollector(doc)
                        .OfClass(typeClass)
                        .Cast<HostObjAttributes>()
                        .FirstOrDefault(t => t.Name.Equals(TypeName, StringComparison.OrdinalIgnoreCase));

                    if (hostType == null)
                        throw new Exception($"Type '{TypeName}' not found in category '{Category ?? "(not specified)"}'");
                }
                else
                {
                    throw new Exception("Either typeId or typeName (with category) must be provided");
                }

                // Get compound structure
                var cs = hostType.GetCompoundStructure();
                if (cs == null)
                {
                    Result = new AIResult<object>
                    {
                        Success = true,
                        Message = $"Type '{hostType.Name}' does not have a compound structure (e.g. curtain wall)",
                        Response = new Dictionary<string, object>
                        {
                            ["typeName"] = hostType.Name,
#if REVIT2024_OR_GREATER
                            ["typeId"] = hostType.Id.Value,
#else
                            ["typeId"] = (long)hostType.Id.IntegerValue,
#endif
                            ["category"] = hostType.Category?.Name ?? "",
                            ["hasCompoundStructure"] = false
                        }
                    };
                    return;
                }

                // Read layers
                var layers = cs.GetLayers();
                var layerList = new List<Dictionary<string, object>>();

                for (int i = 0; i < layers.Count; i++)
                {
                    var layer = layers[i];
                    string materialName = "< By Category >";
                    if (layer.MaterialId != ElementId.InvalidElementId)
                    {
                        var matElem = doc.GetElement(layer.MaterialId);
                        if (matElem != null)
                            materialName = matElem.Name;
                    }

                    layerList.Add(new Dictionary<string, object>
                    {
                        ["index"] = i,
                        ["function"] = layer.Function.ToString(),
                        ["widthMm"] = Math.Round(layer.Width * 304.8, 1),
#if REVIT2024_OR_GREATER
                        ["materialId"] = layer.MaterialId.Value,
#else
                        ["materialId"] = (long)layer.MaterialId.IntegerValue,
#endif
                        ["materialName"] = materialName,
                        ["wraps"] = layer.LayerCapFlag
                    });
                }

                // Build result
                var result = new Dictionary<string, object>
                {
                    ["typeName"] = hostType.Name,
#if REVIT2024_OR_GREATER
                    ["typeId"] = hostType.Id.Value,
#else
                    ["typeId"] = (long)hostType.Id.IntegerValue,
#endif
                    ["category"] = hostType.Category?.Name ?? "",
                    ["totalWidthMm"] = Math.Round(cs.GetWidth() * 304.8, 1),
                    ["layerCount"] = cs.LayerCount,
                    ["firstCoreLayerIndex"] = cs.GetFirstCoreLayerIndex(),
                    ["lastCoreLayerIndex"] = cs.GetLastCoreLayerIndex(),
                    ["isVerticallyCompound"] = cs.IsVerticallyCompound,
                    ["variableLayerIndex"] = cs.VariableLayerIndex,
                    ["structuralMaterialIndex"] = cs.StructuralMaterialIndex,
                    ["openingWrapping"] = cs.OpeningWrapping.ToString(),
                    ["endCap"] = cs.EndCap.ToString(),
                    ["layers"] = layerList
                };

                Result = new AIResult<object>
                {
                    Success = true,
                    Message = $"Retrieved compound structure for '{hostType.Name}' with {cs.LayerCount} layer(s)",
                    Response = result
                };
            }
            catch (Exception ex)
            {
                Result = new AIResult<object>
                {
                    Success = false,
                    Message = $"Failed to get compound structure: {ex.Message}"
                };
            }
            finally
            {
                _resetEvent.Set();
            }
        }

        private static Type ResolveTypeClass(string category)
        {
            if (string.IsNullOrEmpty(category))
                throw new Exception("Category must be specified when searching by typeName. Valid values: Walls, Floors, Roofs, Ceilings");

            switch (category.ToLowerInvariant())
            {
                case "walls":
                case "wall":
                    return typeof(WallType);
                case "floors":
                case "floor":
                    return typeof(FloorType);
                case "roofs":
                case "roof":
                    return typeof(RoofType);
                case "ceilings":
                case "ceiling":
                    return typeof(CeilingType);
                default:
                    throw new Exception($"Unsupported category '{category}'. Valid values: Walls, Floors, Roofs, Ceilings");
            }
        }

        public string GetName() => "Get Compound Structure";
    }
}
