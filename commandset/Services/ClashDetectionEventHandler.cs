using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCPCommandSet.Models.Common;
using RevitMCPSDK.API.Interfaces;

namespace RevitMCPCommandSet.Services
{
    public class ClashDetectionEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        private readonly ManualResetEvent _resetEvent = new ManualResetEvent(false);

        public string CategoryA { get; set; } = "";
        public string CategoryB { get; set; } = "";
        public List<long> ElementIdsA { get; set; } = new List<long>();
        public List<long> ElementIdsB { get; set; } = new List<long>();
        public double Tolerance { get; set; } = 0;
        public int MaxResults { get; set; } = 100;
        public AIResult<object> Result { get; private set; }

        public void SetParameters(string categoryA, string categoryB, List<long> elementIdsA, List<long> elementIdsB, double tolerance, int maxResults)
        {
            CategoryA = categoryA ?? "";
            CategoryB = categoryB ?? "";
            ElementIdsA = elementIdsA ?? new List<long>();
            ElementIdsB = elementIdsB ?? new List<long>();
            Tolerance = tolerance;
            MaxResults = maxResults > 0 ? maxResults : 100;
            _resetEvent.Reset();
        }

        public bool WaitForCompletion(int timeoutMilliseconds = 30000)
        {
            return _resetEvent.WaitOne(timeoutMilliseconds);
        }

        public void Execute(UIApplication app)
        {
            try
            {
                var doc = app.ActiveUIDocument.Document;

                // Get elements for set A
                var setA = GetElements(doc, ElementIdsA, CategoryA);
                // Get elements for set B
                var setB = GetElements(doc, ElementIdsB, CategoryB);

                if (setA.Count == 0 || setB.Count == 0)
                {
                    Result = new AIResult<object>
                    {
                        Success = false,
                        Message = $"Not enough elements: Set A has {setA.Count}, Set B has {setB.Count}"
                    };
                    return;
                }

                var clashes = new List<object>();

                foreach (var elemA in setA)
                {
                    if (clashes.Count >= MaxResults) break;

                    // Create filter to find elements in set B that intersect with elemA
                    var bbFilter = new ElementIntersectsElementFilter(elemA);

                    foreach (var elemB in setB)
                    {
                        if (clashes.Count >= MaxResults) break;
                        if (elemA.Id == elemB.Id) continue;

                        try
                        {
                            if (bbFilter.PassesFilter(elemB))
                            {
                                clashes.Add(new
                                {
#if REVIT2024_OR_GREATER
                                    elementIdA = elemA.Id.Value,
                                    elementIdB = elemB.Id.Value,
#else
                                    elementIdA = elemA.Id.IntegerValue,
                                    elementIdB = elemB.Id.IntegerValue,
#endif
                                    elementNameA = elemA.Name,
                                    elementNameB = elemB.Name,
                                    categoryA = elemA.Category?.Name ?? "",
                                    categoryB = elemB.Category?.Name ?? ""
                                });
                            }
                        }
                        catch { /* skip elements that can't be checked */ }
                    }
                }

                Result = new AIResult<object>
                {
                    Success = true,
                    Message = $"Found {clashes.Count} clashes between {setA.Count} and {setB.Count} elements",
                    Response = new
                    {
                        setACount = setA.Count,
                        setBCount = setB.Count,
                        clashCount = clashes.Count,
                        maxResults = MaxResults,
                        clashes
                    }
                };
            }
            catch (Exception ex)
            {
                Result = new AIResult<object> { Success = false, Message = $"Clash detection failed: {ex.Message}" };
            }
            finally
            {
                _resetEvent.Set();
            }
        }

        private List<Element> GetElements(Document doc, List<long> elementIds, string categoryName)
        {
            if (elementIds.Count > 0)
            {
                return elementIds
                    .Select(id => doc.GetElement(ToElementId(id)))
                    .Where(e => e != null)
                    .ToList();
            }

            if (!string.IsNullOrEmpty(categoryName))
            {
                var bic = ResolveCategoryByName(doc, categoryName);
                if (bic.HasValue)
                {
                    return new FilteredElementCollector(doc)
                        .OfCategory(bic.Value)
                        .WhereElementIsNotElementType()
                        .ToList();
                }
            }

            return new List<Element>();
        }

        private BuiltInCategory? ResolveCategoryByName(Document doc, string name)
        {
            var lowerName = name.ToLower();
            // Common category mappings
            var categoryMap = new Dictionary<string, BuiltInCategory>(StringComparer.OrdinalIgnoreCase)
            {
                ["Walls"] = BuiltInCategory.OST_Walls,
                ["Floors"] = BuiltInCategory.OST_Floors,
                ["Roofs"] = BuiltInCategory.OST_Roofs,
                ["Doors"] = BuiltInCategory.OST_Doors,
                ["Windows"] = BuiltInCategory.OST_Windows,
                ["Columns"] = BuiltInCategory.OST_Columns,
                ["StructuralColumns"] = BuiltInCategory.OST_StructuralColumns,
                ["StructuralFraming"] = BuiltInCategory.OST_StructuralFraming,
                ["Beams"] = BuiltInCategory.OST_StructuralFraming,
                ["StructuralFoundation"] = BuiltInCategory.OST_StructuralFoundation,
                ["Pipes"] = BuiltInCategory.OST_PipeCurves,
                ["Ducts"] = BuiltInCategory.OST_DuctCurves,
                ["CableTray"] = BuiltInCategory.OST_CableTray,
                ["Conduit"] = BuiltInCategory.OST_Conduit,
                ["MechanicalEquipment"] = BuiltInCategory.OST_MechanicalEquipment,
                ["ElectricalEquipment"] = BuiltInCategory.OST_ElectricalEquipment,
                ["PlumbingFixtures"] = BuiltInCategory.OST_PlumbingFixtures,
                ["Furniture"] = BuiltInCategory.OST_Furniture,
                ["Rooms"] = BuiltInCategory.OST_Rooms,
                ["Ceilings"] = BuiltInCategory.OST_Ceilings,
                ["Stairs"] = BuiltInCategory.OST_Stairs,
                ["Railings"] = BuiltInCategory.OST_StairsRailing,
                ["GenericModels"] = BuiltInCategory.OST_GenericModel
            };

            if (categoryMap.TryGetValue(name, out var bic))
                return bic;

            return null;
        }

        private ElementId ToElementId(long id)
        {
#if REVIT2024_OR_GREATER
            return new ElementId(id);
#else
            return new ElementId((int)id);
#endif
        }

        public string GetName() => "Clash Detection";
    }
}
