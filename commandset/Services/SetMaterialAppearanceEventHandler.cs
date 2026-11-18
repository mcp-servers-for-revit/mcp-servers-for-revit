using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Visual;
using Autodesk.Revit.UI;
using RevitMCPCommandSet.Models.Common;
using RevitMCPSDK.API.Interfaces;

namespace RevitMCPCommandSet.Services
{
    public class SetMaterialAppearanceEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        private readonly ManualResetEvent _resetEvent = new ManualResetEvent(false);

        // Material identification
        public long? MaterialId { get; set; }
        public string MaterialName { get; set; }

        // Graphic/shading properties
        public int? ColorR { get; set; }
        public int? ColorG { get; set; }
        public int? ColorB { get; set; }
        public int? Transparency { get; set; }
        public int? Shininess { get; set; }
        public int? Smoothness { get; set; }
        public bool? UseRenderAppearanceForShading { get; set; }

        // Surface pattern colors
        public int? SurfacePatternColorR { get; set; }
        public int? SurfacePatternColorG { get; set; }
        public int? SurfacePatternColorB { get; set; }

        // Cut pattern colors
        public int? CutPatternColorR { get; set; }
        public int? CutPatternColorG { get; set; }
        public int? CutPatternColorB { get; set; }

        // Rendering appearance
        public int? RenderColorR { get; set; }
        public int? RenderColorG { get; set; }
        public int? RenderColorB { get; set; }
        public double? RenderTransparency { get; set; }
        public double? RenderGlossiness { get; set; }

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

                // --- Find material ---
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

                using (var transaction = new Transaction(doc, "Set Material Appearance"))
                {
                    transaction.Start();
                    try
                    {
                        // --- Graphic properties ---

                        // Shading color (requires all 3 RGB components)
                        if (ColorR.HasValue && ColorG.HasValue && ColorB.HasValue)
                        {
                            mat.Color = new Color((byte)ColorR.Value, (byte)ColorG.Value, (byte)ColorB.Value);
                            changedProperties.Add("color");
                        }

                        if (Transparency.HasValue)
                        {
                            mat.Transparency = Transparency.Value;
                            changedProperties.Add("transparency");
                        }

                        if (Shininess.HasValue)
                        {
                            mat.Shininess = Shininess.Value;
                            changedProperties.Add("shininess");
                        }

                        if (Smoothness.HasValue)
                        {
                            mat.Smoothness = Smoothness.Value;
                            changedProperties.Add("smoothness");
                        }

                        if (UseRenderAppearanceForShading.HasValue)
                        {
                            mat.UseRenderAppearanceForShading = UseRenderAppearanceForShading.Value;
                            changedProperties.Add("useRenderAppearanceForShading");
                        }

                        // Surface foreground pattern color (requires all 3 RGB)
                        if (SurfacePatternColorR.HasValue && SurfacePatternColorG.HasValue && SurfacePatternColorB.HasValue)
                        {
                            mat.SurfaceForegroundPatternColor = new Color(
                                (byte)SurfacePatternColorR.Value,
                                (byte)SurfacePatternColorG.Value,
                                (byte)SurfacePatternColorB.Value);
                            changedProperties.Add("surfacePatternColor");
                        }

                        // Cut foreground pattern color (requires all 3 RGB)
                        if (CutPatternColorR.HasValue && CutPatternColorG.HasValue && CutPatternColorB.HasValue)
                        {
                            mat.CutForegroundPatternColor = new Color(
                                (byte)CutPatternColorR.Value,
                                (byte)CutPatternColorG.Value,
                                (byte)CutPatternColorB.Value);
                            changedProperties.Add("cutPatternColor");
                        }

                        // --- Rendering appearance asset ---
                        bool hasRenderChanges = RenderColorR.HasValue || RenderColorG.HasValue || RenderColorB.HasValue
                            || RenderTransparency.HasValue || RenderGlossiness.HasValue;

                        if (hasRenderChanges)
                        {
                            if (mat.AppearanceAssetId == ElementId.InvalidElementId)
                                throw new Exception("Material has no appearance asset assigned; cannot modify rendering properties");

                            using (var editScope = new AppearanceAssetEditScope(doc))
                            {
                                Asset editableAsset = editScope.Start(mat.AppearanceAssetId);

                                // Diffuse color (requires all 3 RGB)
                                if (RenderColorR.HasValue && RenderColorG.HasValue && RenderColorB.HasValue)
                                {
                                    var diffuseProp = editableAsset.FindByName("generic_diffuse") as AssetPropertyDoubleArray4d;
                                    if (diffuseProp != null)
                                    {
                                        diffuseProp.SetValueAsColor(new Color(
                                            (byte)RenderColorR.Value,
                                            (byte)RenderColorG.Value,
                                            (byte)RenderColorB.Value));
                                        changedProperties.Add("renderColor");
                                    }
                                    else
                                    {
                                        changedProperties.Add("renderColor (property not found in asset)");
                                    }
                                }

                                // Transparency (0.0 to 1.0)
                                if (RenderTransparency.HasValue)
                                {
                                    var transProp = editableAsset.FindByName("generic_transparency") as AssetPropertyDouble;
                                    if (transProp != null)
                                    {
                                        transProp.Value = RenderTransparency.Value;
                                        changedProperties.Add("renderTransparency");
                                    }
                                    else
                                    {
                                        changedProperties.Add("renderTransparency (property not found in asset)");
                                    }
                                }

                                // Glossiness (0.0 to 1.0)
                                if (RenderGlossiness.HasValue)
                                {
                                    var glossProp = editableAsset.FindByName("generic_glossiness") as AssetPropertyDouble;
                                    if (glossProp != null)
                                    {
                                        glossProp.Value = RenderGlossiness.Value;
                                        changedProperties.Add("renderGlossiness");
                                    }
                                    else
                                    {
                                        changedProperties.Add("renderGlossiness (property not found in asset)");
                                    }
                                }

                                editScope.Commit(true);
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

                // --- Build result ---
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
                    ["color"] = colorHex,
                    ["transparency"] = mat.Transparency,
                    ["shininess"] = mat.Shininess,
                    ["smoothness"] = mat.Smoothness,
                    ["useRenderAppearanceForShading"] = mat.UseRenderAppearanceForShading,
                    ["changedProperties"] = changedProperties
                };

                Result = new AIResult<object>
                {
                    Success = true,
                    Message = $"Updated {changedProperties.Count} appearance properties on material '{mat.Name}'",
                    Response = result
                };
            }
            catch (Exception ex)
            {
                Result = new AIResult<object>
                {
                    Success = false,
                    Message = $"Failed to set material appearance: {ex.Message}"
                };
            }
            finally
            {
                _resetEvent.Set();
            }
        }

        public string GetName() => "Set Material Appearance";
    }
}
