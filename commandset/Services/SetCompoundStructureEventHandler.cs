using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCPCommandSet.Helpers;
using RevitMCPCommandSet.Models.Common;
using RevitMCPSDK.API.Interfaces;

namespace RevitMCPCommandSet.Services
{
    public class SetCompoundStructureEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        private readonly ManualResetEvent _resetEvent = new ManualResetEvent(false);

        public long? TypeId { get; set; }
        public string TypeName { get; set; }
        public string Category { get; set; }
        public string DuplicateAsName { get; set; }
        public List<CompoundLayerInput> Layers { get; set; }
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

                // Resolve the HostObjAttributes type
                HostObjAttributes hostType = ResolveHostType(doc);
                if (hostType == null)
                {
                    Result = new AIResult<object>
                    {
                        Success = false,
                        Message = "Could not find the specified type. Provide a valid typeId, or typeName + category."
                    };
                    return;
                }

                // Duplicate first if requested
                if (!string.IsNullOrEmpty(DuplicateAsName))
                {
                    using (var dupTx = new Transaction(doc, "Duplicate Type for Compound Structure"))
                    {
                        dupTx.Start();
                        try
                        {
                            hostType = hostType.Duplicate(DuplicateAsName) as HostObjAttributes;
                            if (hostType == null)
                                throw new Exception("Duplicate returned null — the type may not support duplication.");
                            dupTx.Commit();
                        }
                        catch
                        {
                            if (dupTx.GetStatus() == TransactionStatus.Started)
                                dupTx.RollBack();
                            throw;
                        }
                    }
                }

                var cs = hostType.GetCompoundStructure();
                if (cs == null)
                {
                    Result = new AIResult<object>
                    {
                        Success = false,
                        Message = $"Type '{hostType.Name}' does not have a compound structure (it may be a curtain wall or similar)."
                    };
                    return;
                }

                // User confirmation
                if (!ConfirmationHelper.Confirm($"modify compound structure of type '{hostType.Name}'", 1))
                {
                    Result = new AIResult<object>
                    {
                        Success = false,
                        Message = "Operation cancelled by user",
                        Response = new { cancelled = true }
                    };
                    return;
                }

                // Build material lookup dictionary once
                var materialLookup = new Dictionary<string, ElementId>(StringComparer.OrdinalIgnoreCase);
                foreach (var mat in new FilteredElementCollector(doc).OfClass(typeof(Material)).Cast<Material>())
                {
                    materialLookup[mat.Name] = mat.Id;
                }

                // Build new layers
                var newLayers = new List<CompoundStructureLayer>();
                foreach (var layer in Layers)
                {
                    if (!Enum.TryParse<MaterialFunctionAssignment>(layer.Function, true, out var func))
                        throw new Exception($"Invalid layer function '{layer.Function}'. Valid values: Structure, Substrate, Insulation, Finish1, Finish2, Membrane, StructuralDeck.");

                    // Membrane layers must have width 0
                    double widthFeet = func == MaterialFunctionAssignment.Membrane
                        ? 0.0
                        : layer.WidthMm / 304.8;

                    // Resolve material
                    ElementId materialId = ElementId.InvalidElementId;
                    if (layer.MaterialId.HasValue)
                    {
#if REVIT2024_OR_GREATER
                        materialId = new ElementId(layer.MaterialId.Value);
#else
                        materialId = new ElementId((int)layer.MaterialId.Value);
#endif
                    }
                    else if (!string.IsNullOrEmpty(layer.MaterialName))
                    {
                        if (materialLookup.TryGetValue(layer.MaterialName, out var foundId))
                        {
                            materialId = foundId;
                        }
                        else
                        {
                            throw new Exception($"Material '{layer.MaterialName}' not found in the project.");
                        }
                    }

                    var newLayer = new CompoundStructureLayer(widthFeet, func, materialId);
                    newLayer.LayerCapFlag = layer.Wraps ?? false;
                    newLayers.Add(newLayer);
                }

                // Apply inside a transaction
                using (var transaction = new Transaction(doc, "Set Compound Structure"))
                {
                    transaction.Start();
                    try
                    {
                        cs.SetLayers(newLayers);
                        hostType.SetCompoundStructure(cs);
                        transaction.Commit();
                    }
                    catch
                    {
                        if (transaction.GetStatus() == TransactionStatus.Started)
                            transaction.RollBack();
                        throw;
                    }
                }

                // Build result
                double totalWidthMm = 0;
                var appliedLayers = new List<object>();
                var finalCs = hostType.GetCompoundStructure();
                if (finalCs != null)
                {
                    foreach (var cl in finalCs.GetLayers())
                    {
                        double layerWidthMm = cl.Width * 304.8;
                        totalWidthMm += layerWidthMm;

                        string matName = "By Category";
                        if (cl.MaterialId != ElementId.InvalidElementId)
                        {
                            var matElem = doc.GetElement(cl.MaterialId) as Material;
                            if (matElem != null)
                                matName = matElem.Name;
                        }

                        appliedLayers.Add(new
                        {
                            function = cl.Function.ToString(),
                            widthMm = Math.Round(layerWidthMm, 2),
                            material = matName,
#if REVIT2024_OR_GREATER
                            materialId = cl.MaterialId.Value,
#else
                            materialId = (long)cl.MaterialId.IntegerValue,
#endif
                            wraps = cl.LayerCapFlag
                        });
                    }
                }

                Result = new AIResult<object>
                {
                    Success = true,
                    Message = $"Compound structure of type '{hostType.Name}' updated with {newLayers.Count} layer(s).",
                    Response = new
                    {
                        typeName = hostType.Name,
#if REVIT2024_OR_GREATER
                        typeId = hostType.Id.Value,
#else
                        typeId = (long)hostType.Id.IntegerValue,
#endif
                        layerCount = appliedLayers.Count,
                        totalWidthMm = Math.Round(totalWidthMm, 2),
                        layers = appliedLayers
                    }
                };
            }
            catch (Exception ex)
            {
                Result = new AIResult<object>
                {
                    Success = false,
                    Message = $"Failed to set compound structure: {ex.Message}"
                };
            }
            finally
            {
                _resetEvent.Set();
            }
        }

        private HostObjAttributes ResolveHostType(Document doc)
        {
            // By TypeId
            if (TypeId.HasValue)
            {
#if REVIT2024_OR_GREATER
                var elementId = new ElementId(TypeId.Value);
#else
                var elementId = new ElementId((int)TypeId.Value);
#endif
                var elem = doc.GetElement(elementId);
                if (elem is HostObjAttributes hostById)
                    return hostById;
            }

            // By TypeName + Category
            if (!string.IsNullOrEmpty(TypeName) && !string.IsNullOrEmpty(Category))
            {
                Type typeClass = ResolveCategoryType(Category);
                if (typeClass == null)
                    throw new Exception($"Unsupported category '{Category}'. Use: Walls, Floors, Roofs, or Ceilings.");

                var match = new FilteredElementCollector(doc)
                    .OfClass(typeClass)
                    .Cast<HostObjAttributes>()
                    .FirstOrDefault(t => string.Equals(t.Name, TypeName, StringComparison.OrdinalIgnoreCase));

                return match;
            }

            // By TypeName only (search all host types)
            if (!string.IsNullOrEmpty(TypeName))
            {
                var types = new[] { typeof(WallType), typeof(FloorType), typeof(RoofType), typeof(CeilingType) };
                foreach (var typeClass in types)
                {
                    var match = new FilteredElementCollector(doc)
                        .OfClass(typeClass)
                        .Cast<HostObjAttributes>()
                        .FirstOrDefault(t => string.Equals(t.Name, TypeName, StringComparison.OrdinalIgnoreCase));

                    if (match != null)
                        return match;
                }
            }

            return null;
        }

        private Type ResolveCategoryType(string category)
        {
            switch (category?.ToLowerInvariant())
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
                    return null;
            }
        }

        public string GetName() => "Set Compound Structure";
    }
}
