using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Services;
using RevitMCPSDK.API.Base;

namespace RevitMCPCommandSet.Commands.Access
{
    public class SetMaterialAssetsCommand : ExternalEventCommandBase
    {
        private static readonly object _executionLock = new object();
        private SetMaterialAssetsEventHandler _handler => (SetMaterialAssetsEventHandler)Handler;

        public override string CommandName => "set_material_assets";

        public SetMaterialAssetsCommand(UIApplication uiApp)
            : base(new SetMaterialAssetsEventHandler(), uiApp)
        {
        }

        public override object Execute(JObject parameters, string requestId)
        {
            lock (_executionLock)
            {
                try
                {
                    _handler.MaterialId = parameters?["materialId"]?.Value<long>();
                    _handler.MaterialName = parameters?["materialName"]?.Value<string>();

                    if (_handler.MaterialId == null && string.IsNullOrEmpty(_handler.MaterialName))
                        throw new ArgumentException("materialId or materialName is required");

                    // Structural properties
                    _handler.Density = parameters?["density"]?.Value<double>();
                    _handler.YoungModulus = parameters?["youngModulus"]?.Value<double>();
                    _handler.PoissonRatio = parameters?["poissonRatio"]?.Value<double>();
                    _handler.ShearModulus = parameters?["shearModulus"]?.Value<double>();
                    _handler.ThermalExpansionCoefficient = parameters?["thermalExpansionCoefficient"]?.Value<double>();
                    _handler.MinimumYieldStress = parameters?["minimumYieldStress"]?.Value<double>();
                    _handler.MinimumTensileStrength = parameters?["minimumTensileStrength"]?.Value<double>();
                    _handler.Behavior = parameters?["behavior"]?.Value<string>();

                    // Thermal properties
                    _handler.ThermalConductivity = parameters?["thermalConductivity"]?.Value<double>();
                    _handler.SpecificHeat = parameters?["specificHeat"]?.Value<double>();
                    _handler.ThermalDensity = parameters?["thermalDensity"]?.Value<double>();
                    _handler.Emissivity = parameters?["emissivity"]?.Value<double>();
                    _handler.Permeability = parameters?["permeability"]?.Value<double>();
                    _handler.Porosity = parameters?["porosity"]?.Value<double>();

                    if (RaiseAndWaitForCompletion(120000))
                    {
                        return _handler.Result;
                    }
                    else
                    {
                        throw new TimeoutException("Set material assets timed out");
                    }
                }
                catch (Exception ex)
                {
                    throw new Exception($"Set material assets failed: {ex.Message}");
                }
            }
        }
    }
}
