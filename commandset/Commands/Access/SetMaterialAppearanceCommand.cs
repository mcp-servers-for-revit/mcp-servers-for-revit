using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Services;
using RevitMCPSDK.API.Base;

namespace RevitMCPCommandSet.Commands.Access
{
    public class SetMaterialAppearanceCommand : ExternalEventCommandBase
    {
        private static readonly object _executionLock = new object();
        private SetMaterialAppearanceEventHandler _handler => (SetMaterialAppearanceEventHandler)Handler;
        public override string CommandName => "set_material_appearance";

        public SetMaterialAppearanceCommand(UIApplication uiApp)
            : base(new SetMaterialAppearanceEventHandler(), uiApp) { }

        public override object Execute(JObject parameters, string requestId)
        {
            lock (_executionLock)
            {
                try
                {
                    _handler.MaterialId = parameters?["materialId"]?.Value<long>();
                    _handler.MaterialName = parameters?["materialName"]?.Value<string>();
                    // Graphic properties
                    _handler.ColorR = parameters?["colorR"]?.Value<int>();
                    _handler.ColorG = parameters?["colorG"]?.Value<int>();
                    _handler.ColorB = parameters?["colorB"]?.Value<int>();
                    _handler.Transparency = parameters?["transparency"]?.Value<int>();
                    _handler.Shininess = parameters?["shininess"]?.Value<int>();
                    _handler.Smoothness = parameters?["smoothness"]?.Value<int>();
                    _handler.UseRenderAppearanceForShading = parameters?["useRenderAppearanceForShading"]?.Value<bool>();
                    // Surface pattern colors
                    _handler.SurfacePatternColorR = parameters?["surfacePatternColorR"]?.Value<int>();
                    _handler.SurfacePatternColorG = parameters?["surfacePatternColorG"]?.Value<int>();
                    _handler.SurfacePatternColorB = parameters?["surfacePatternColorB"]?.Value<int>();
                    _handler.CutPatternColorR = parameters?["cutPatternColorR"]?.Value<int>();
                    _handler.CutPatternColorG = parameters?["cutPatternColorG"]?.Value<int>();
                    _handler.CutPatternColorB = parameters?["cutPatternColorB"]?.Value<int>();
                    // Rendering appearance
                    _handler.RenderColorR = parameters?["renderColorR"]?.Value<int>();
                    _handler.RenderColorG = parameters?["renderColorG"]?.Value<int>();
                    _handler.RenderColorB = parameters?["renderColorB"]?.Value<int>();
                    _handler.RenderTransparency = parameters?["renderTransparency"]?.Value<double>();
                    _handler.RenderGlossiness = parameters?["renderGlossiness"]?.Value<double>();

                    if (_handler.MaterialId == null && string.IsNullOrEmpty(_handler.MaterialName))
                        throw new ArgumentException("materialId or materialName is required");

                    if (RaiseAndWaitForCompletion(120000))
                    {
                        return _handler.Result;
                    }
                    else
                    {
                        throw new TimeoutException("Set material appearance timed out");
                    }
                }
                catch (Exception ex)
                {
                    throw new Exception($"Set material appearance failed: {ex.Message}");
                }
            }
        }
    }
}
