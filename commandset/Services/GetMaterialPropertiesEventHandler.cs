using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCPCommandSet.Models.Common;
using RevitMCPSDK.API.Interfaces;

namespace RevitMCPCommandSet.Services
{
    public class GetMaterialPropertiesEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        private readonly ManualResetEvent _resetEvent = new ManualResetEvent(false);

        public long? MaterialId { get; set; }
        public string MaterialName { get; set; }
        public AIResult<object> Result { get; private set; }

        public void SetParameters(long? materialId, string materialName)
        {
            MaterialId = materialId;
            MaterialName = materialName;
            _resetEvent.Reset();
        }

        public bool WaitForCompletion(int timeoutMilliseconds = 10000)
        {
            return _resetEvent.WaitOne(timeoutMilliseconds);
        }

        public void Execute(UIApplication app)
        {
            try
            {
                var doc = app.ActiveUIDocument.Document;

                Material mat = null;

                if (MaterialId.HasValue)
                {
#if REVIT2024_OR_GREATER
                    var elementId = new ElementId(MaterialId.Value);
#else
                    var elementId = new ElementId((int)MaterialId.Value);
#endif
                    mat = doc.GetElement(elementId) as Material;
                }

                if (mat == null && !string.IsNullOrEmpty(MaterialName))
                {
                    mat = new FilteredElementCollector(doc)
                        .OfClass(typeof(Material))
                        .Cast<Material>()
                        .FirstOrDefault(m => string.Equals(m.Name, MaterialName, StringComparison.OrdinalIgnoreCase));
                }

                if (mat == null)
                    throw new Exception("Material not found");

                string colorHex = null;
                if (mat.Color.IsValid)
                {
                    colorHex = $"#{mat.Color.Red:X2}{mat.Color.Green:X2}{mat.Color.Blue:X2}";
                }

                var result = new Dictionary<string, object>
                {
#if REVIT2024_OR_GREATER
                    ["id"] = mat.Id.Value,
#else
                    ["id"] = mat.Id.IntegerValue,
#endif
                    ["name"] = mat.Name,
                    ["materialClass"] = mat.MaterialClass,
                    ["materialCategory"] = mat.MaterialCategory,
                    ["color"] = colorHex,
                    ["transparency"] = mat.Transparency,
                    ["shininess"] = mat.Shininess,
                    ["smoothness"] = mat.Smoothness
                };

                // Structural properties
                if (mat.StructuralAssetId != ElementId.InvalidElementId)
                {
                    var structPropElem = doc.GetElement(mat.StructuralAssetId) as PropertySetElement;
                    if (structPropElem != null)
                    {
                        try
                        {
                            var structAsset = structPropElem.GetStructuralAsset();
                            var structProps = new Dictionary<string, object>();

                            TrySet(structProps, "density", () => structAsset.Density);
                            TrySet(structProps, "youngModulusX", () => structAsset.YoungModulus.X);
                            TrySet(structProps, "youngModulusY", () => structAsset.YoungModulus.Y);
                            TrySet(structProps, "youngModulusZ", () => structAsset.YoungModulus.Z);
                            TrySet(structProps, "poissonRatioX", () => structAsset.PoissonRatio.X);
                            TrySet(structProps, "poissonRatioY", () => structAsset.PoissonRatio.Y);
                            TrySet(structProps, "poissonRatioZ", () => structAsset.PoissonRatio.Z);
                            TrySet(structProps, "shearModulusX", () => structAsset.ShearModulus.X);
                            TrySet(structProps, "shearModulusY", () => structAsset.ShearModulus.Y);
                            TrySet(structProps, "shearModulusZ", () => structAsset.ShearModulus.Z);
                            TrySet(structProps, "thermalExpansionCoefficientX", () => structAsset.ThermalExpansionCoefficient.X);
                            TrySet(structProps, "thermalExpansionCoefficientY", () => structAsset.ThermalExpansionCoefficient.Y);
                            TrySet(structProps, "thermalExpansionCoefficientZ", () => structAsset.ThermalExpansionCoefficient.Z);
                            TrySet(structProps, "behavior", () => structAsset.Behavior.ToString());
                            TrySet(structProps, "minimumYieldStress", () => structAsset.MinimumYieldStress);
                            TrySet(structProps, "minimumTensileStrength", () => structAsset.MinimumTensileStrength);
                            TrySet(structProps, "subClass", () => structAsset.SubClass.ToString());

                            result["structuralProperties"] = structProps;
                        }
                        catch (Exception ex)
                        {
                            result["structuralProperties"] = new Dictionary<string, object>
                            {
                                ["error"] = $"Failed to read structural asset: {ex.Message}"
                            };
                        }
                    }
                }

                // Thermal properties
                if (mat.ThermalAssetId != ElementId.InvalidElementId)
                {
                    var thermalPropElem = doc.GetElement(mat.ThermalAssetId) as PropertySetElement;
                    if (thermalPropElem != null)
                    {
                        try
                        {
                            var thermalAsset = thermalPropElem.GetThermalAsset();
                            var thermalProps = new Dictionary<string, object>();

                            TrySet(thermalProps, "thermalConductivity", () => thermalAsset.ThermalConductivity);
                            TrySet(thermalProps, "specificHeat", () => thermalAsset.SpecificHeat);
                            TrySet(thermalProps, "density", () => thermalAsset.Density);
                            TrySet(thermalProps, "emissivity", () => thermalAsset.Emissivity);
                            TrySet(thermalProps, "permeability", () => thermalAsset.Permeability);
                            TrySet(thermalProps, "porosity", () => thermalAsset.Porosity);

                            result["thermalProperties"] = thermalProps;
                        }
                        catch (Exception ex)
                        {
                            result["thermalProperties"] = new Dictionary<string, object>
                            {
                                ["error"] = $"Failed to read thermal asset: {ex.Message}"
                            };
                        }
                    }
                }

                Result = new AIResult<object>
                {
                    Success = true,
                    Message = $"Retrieved properties for material '{mat.Name}'",
                    Response = result
                };
            }
            catch (Exception ex)
            {
                Result = new AIResult<object>
                {
                    Success = false,
                    Message = $"Failed to get material properties: {ex.Message}"
                };
            }
            finally
            {
                _resetEvent.Set();
            }
        }

        private static void TrySet(Dictionary<string, object> dict, string key, Func<object> getValue)
        {
            try
            {
                dict[key] = getValue();
            }
            catch
            {
                // Property not available for this material; omit it
            }
        }

        public string GetName() => "Get Material Properties";
    }
}
