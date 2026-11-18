using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCPCommandSet.Models.Common;
using RevitMCPSDK.API.Interfaces;

namespace RevitMCPCommandSet.Services
{
    public class GetMaterialsEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        private readonly ManualResetEvent _resetEvent = new ManualResetEvent(false);

        public string MaterialClass { get; set; }
        public string NameFilter { get; set; }
        public AIResult<object> Result { get; private set; }

        public void SetParameters(string materialClass, string nameFilter)
        {
            MaterialClass = materialClass;
            NameFilter = nameFilter;
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

                var materials = new FilteredElementCollector(doc)
                    .OfClass(typeof(Material))
                    .Cast<Material>();

                var results = new List<object>();

                foreach (var mat in materials)
                {
                    // Apply materialClass filter (case-insensitive equals)
                    if (!string.IsNullOrEmpty(MaterialClass) &&
                        !string.Equals(mat.MaterialClass, MaterialClass, StringComparison.OrdinalIgnoreCase))
                        continue;

                    // Apply nameFilter (case-insensitive substring)
                    if (!string.IsNullOrEmpty(NameFilter) &&
                        (mat.Name == null || mat.Name.IndexOf(NameFilter, StringComparison.OrdinalIgnoreCase) < 0))
                        continue;

                    string colorHex = null;
                    if (mat.Color.IsValid)
                    {
                        colorHex = $"#{mat.Color.Red:X2}{mat.Color.Green:X2}{mat.Color.Blue:X2}";
                    }

                    results.Add(new
                    {
#if REVIT2024_OR_GREATER
                        id = mat.Id.Value,
#else
                        id = mat.Id.IntegerValue,
#endif
                        name = mat.Name,
                        materialClass = mat.MaterialClass,
                        materialCategory = mat.MaterialCategory,
                        color = colorHex,
                        transparency = mat.Transparency,
                        shininess = mat.Shininess,
                        smoothness = mat.Smoothness,
                        hasAppearanceAsset = mat.AppearanceAssetId != ElementId.InvalidElementId,
                        hasStructuralAsset = mat.StructuralAssetId != ElementId.InvalidElementId,
                        hasThermalAsset = mat.ThermalAssetId != ElementId.InvalidElementId
                    });
                }

                Result = new AIResult<object>
                {
                    Success = true,
                    Message = $"Retrieved {results.Count} materials",
                    Response = results
                };
            }
            catch (Exception ex)
            {
                Result = new AIResult<object>
                {
                    Success = false,
                    Message = $"Failed to get materials: {ex.Message}"
                };
            }
            finally
            {
                _resetEvent.Set();
            }
        }

        public string GetName() => "Get Materials";
    }
}
