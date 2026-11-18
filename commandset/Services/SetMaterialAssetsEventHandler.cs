using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCPCommandSet.Models.Common;
using RevitMCPSDK.API.Interfaces;

namespace RevitMCPCommandSet.Services
{
    public class SetMaterialAssetsEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        private readonly ManualResetEvent _resetEvent = new ManualResetEvent(false);

        // Identification
        public long? MaterialId { get; set; }
        public string MaterialName { get; set; }

        // Structural properties
        public double? Density { get; set; }
        public double? YoungModulus { get; set; }
        public double? PoissonRatio { get; set; }
        public double? ShearModulus { get; set; }
        public double? ThermalExpansionCoefficient { get; set; }
        public double? MinimumYieldStress { get; set; }
        public double? MinimumTensileStrength { get; set; }
        public string Behavior { get; set; }

        // Thermal properties
        public double? ThermalConductivity { get; set; }
        public double? SpecificHeat { get; set; }
        public double? ThermalDensity { get; set; }
        public double? Emissivity { get; set; }
        public double? Permeability { get; set; }
        public double? Porosity { get; set; }

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

                // Find material
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

                var changedProperties = new List<string>();
                bool hasStructuralChanges = Density.HasValue || YoungModulus.HasValue ||
                    PoissonRatio.HasValue || ShearModulus.HasValue ||
                    ThermalExpansionCoefficient.HasValue || MinimumYieldStress.HasValue ||
                    MinimumTensileStrength.HasValue || !string.IsNullOrEmpty(Behavior);

                bool hasThermalChanges = ThermalConductivity.HasValue || SpecificHeat.HasValue ||
                    ThermalDensity.HasValue || Emissivity.HasValue ||
                    Permeability.HasValue || Porosity.HasValue;

                using (var transaction = new Transaction(doc, "Set Material Assets"))
                {
                    transaction.Start();
                    try
                    {
                        // Structural asset
                        if (hasStructuralChanges)
                        {
                            if (mat.StructuralAssetId == ElementId.InvalidElementId)
                            {
                                changedProperties.Add("WARNING: Material has no structural asset — structural properties skipped");
                            }
                            else
                            {
                                var structPropElem = doc.GetElement(mat.StructuralAssetId) as PropertySetElement;
                                if (structPropElem != null)
                                {
                                    var structAsset = structPropElem.GetStructuralAsset();

                                    if (Density.HasValue)
                                    {
                                        structAsset.Density = Density.Value;
                                        changedProperties.Add("density");
                                    }
                                    if (YoungModulus.HasValue)
                                    {
                                        structAsset.SetYoungModulus(YoungModulus.Value);
                                        changedProperties.Add("youngModulus");
                                    }
                                    if (PoissonRatio.HasValue)
                                    {
                                        structAsset.SetPoissonRatio(PoissonRatio.Value);
                                        changedProperties.Add("poissonRatio");
                                    }
                                    if (ShearModulus.HasValue)
                                    {
                                        structAsset.SetShearModulus(ShearModulus.Value);
                                        changedProperties.Add("shearModulus");
                                    }
                                    if (ThermalExpansionCoefficient.HasValue)
                                    {
                                        structAsset.SetThermalExpansionCoefficient(ThermalExpansionCoefficient.Value);
                                        changedProperties.Add("thermalExpansionCoefficient");
                                    }
                                    if (MinimumYieldStress.HasValue)
                                    {
                                        structAsset.MinimumYieldStress = MinimumYieldStress.Value;
                                        changedProperties.Add("minimumYieldStress");
                                    }
                                    if (MinimumTensileStrength.HasValue)
                                    {
                                        structAsset.MinimumTensileStrength = MinimumTensileStrength.Value;
                                        changedProperties.Add("minimumTensileStrength");
                                    }
                                    if (!string.IsNullOrEmpty(Behavior))
                                    {
                                        if (Enum.TryParse<StructuralBehavior>(Behavior, true, out var behavior))
                                        {
                                            structAsset.Behavior = behavior;
                                            changedProperties.Add("behavior");
                                        }
                                        else
                                        {
                                            changedProperties.Add($"WARNING: Invalid behavior '{Behavior}'. Valid: Isotropic, Orthotropic, TransverselyIsotropic");
                                        }
                                    }

                                    // Write back the copy
                                    structPropElem.SetStructuralAsset(structAsset);
                                }
                            }
                        }

                        // Thermal asset
                        if (hasThermalChanges)
                        {
                            if (mat.ThermalAssetId == ElementId.InvalidElementId)
                            {
                                changedProperties.Add("WARNING: Material has no thermal asset — thermal properties skipped");
                            }
                            else
                            {
                                var thermalPropElem = doc.GetElement(mat.ThermalAssetId) as PropertySetElement;
                                if (thermalPropElem != null)
                                {
                                    var thermalAsset = thermalPropElem.GetThermalAsset();

                                    if (ThermalConductivity.HasValue)
                                    {
                                        thermalAsset.ThermalConductivity = ThermalConductivity.Value;
                                        changedProperties.Add("thermalConductivity");
                                    }
                                    if (SpecificHeat.HasValue)
                                    {
                                        thermalAsset.SpecificHeat = SpecificHeat.Value;
                                        changedProperties.Add("specificHeat");
                                    }
                                    if (ThermalDensity.HasValue)
                                    {
                                        thermalAsset.Density = ThermalDensity.Value;
                                        changedProperties.Add("thermalDensity");
                                    }
                                    if (Emissivity.HasValue)
                                    {
                                        thermalAsset.Emissivity = Emissivity.Value;
                                        changedProperties.Add("emissivity");
                                    }
                                    if (Permeability.HasValue)
                                    {
                                        thermalAsset.Permeability = Permeability.Value;
                                        changedProperties.Add("permeability");
                                    }
                                    if (Porosity.HasValue)
                                    {
                                        thermalAsset.Porosity = Porosity.Value;
                                        changedProperties.Add("porosity");
                                    }

                                    // Write back the copy
                                    thermalPropElem.SetThermalAsset(thermalAsset);
                                }
                            }
                        }

                        transaction.Commit();
                    }
                    catch
                    {
                        if (transaction.GetStatus() == TransactionStatus.Started)
                            transaction.RollBack();
                        throw;
                    }
                }

                Result = new AIResult<object>
                {
                    Success = true,
                    Message = $"Updated {changedProperties.Count(p => !p.StartsWith("WARNING"))} asset propert(ies) on material '{mat.Name}'",
                    Response = new
                    {
#if REVIT2024_OR_GREATER
                        materialId = mat.Id.Value,
#else
                        materialId = (long)mat.Id.IntegerValue,
#endif
                        materialName = mat.Name,
                        changedProperties = changedProperties
                    }
                };
            }
            catch (Exception ex)
            {
                Result = new AIResult<object>
                {
                    Success = false,
                    Message = $"Failed to set material assets: {ex.Message}"
                };
            }
            finally
            {
                _resetEvent.Set();
            }
        }

        public string GetName() => "Set Material Assets";
    }
}
