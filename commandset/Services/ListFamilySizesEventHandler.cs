using Autodesk.Revit.UI;
using RevitMCPSDK.API.Interfaces;

namespace RevitMCPCommandSet.Services
{
    public class ListFamilySizesEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        public int Limit { get; set; } = 50;
        public string SortBy { get; set; } = "instanceCount";
        public List<string> Categories { get; set; }

        public object Result { get; private set; }
        public string ErrorMessage { get; private set; }
        public bool TaskCompleted { get; private set; }
        private readonly ManualResetEvent _resetEvent = new ManualResetEvent(false);

        public bool WaitForCompletion(int timeoutMilliseconds = 10000)
        {
            return _resetEvent.WaitOne(timeoutMilliseconds);
        }

        public void Execute(UIApplication app)
        {
            try
            {
                _resetEvent.Reset();
                TaskCompleted = false;
                ErrorMessage = null;

                var doc = app.ActiveUIDocument.Document;

                // Get all families in the document
                var families = new FilteredElementCollector(doc)
                    .OfClass(typeof(Family))
                    .Cast<Family>()
                    .ToList();

                // Filter by categories if specified (language-independent via BuiltInCategory)
                if (Categories != null && Categories.Count > 0)
                {
                    var catIds = new HashSet<long>();
                    foreach (var cat in Categories)
                    {
                        if (Enum.TryParse(cat, out BuiltInCategory bic))
                        {
#if REVIT2024_OR_GREATER
                            catIds.Add(new ElementId(bic).Value);
#else
                            catIds.Add((long)new ElementId(bic).IntegerValue);
#endif
                        }
                    }
                    if (catIds.Count > 0)
                    {
                        families = families.Where(f =>
                        {
                            if (f.FamilyCategory == null) return false;
#if REVIT2024_OR_GREATER
                            return catIds.Contains(f.FamilyCategory.Id.Value);
#else
                            return catIds.Contains(f.FamilyCategory.Id.IntegerValue);
#endif
                        }).ToList();
                    }
                }

                // Build instance count lookup in a single pass: typeId -> count
                var instanceCountByTypeId = new Dictionary<long, int>();
                foreach (var fi in new FilteredElementCollector(doc)
                    .OfClass(typeof(FamilyInstance))
                    .WhereElementIsNotElementType()
                    .Cast<FamilyInstance>())
                {
                    if (fi.Symbol == null) continue;
#if REVIT2024_OR_GREATER
                    long typeIdValue = fi.Symbol.Id.Value;
#else
                    long typeIdValue = fi.Symbol.Id.IntegerValue;
#endif
                    if (instanceCountByTypeId.ContainsKey(typeIdValue))
                        instanceCountByTypeId[typeIdValue]++;
                    else
                        instanceCountByTypeId[typeIdValue] = 1;
                }

                // Build family info using the lookup
                var familyInfos = new List<FamilyInfo>();

                foreach (var family in families)
                {
                    var typeIds = family.GetFamilySymbolIds();
                    int typeCount = typeIds.Count;
                    int instanceCount = 0;

                    foreach (var typeId in typeIds)
                    {
#if REVIT2024_OR_GREATER
                        long key = typeId.Value;
#else
                        long key = typeId.IntegerValue;
#endif
                        if (instanceCountByTypeId.TryGetValue(key, out int count))
                            instanceCount += count;
                    }

                    familyInfos.Add(new FamilyInfo
                    {
#if REVIT2024_OR_GREATER
                        FamilyId = family.Id.Value,
#else
                        FamilyId = family.Id.IntegerValue,
#endif
                        FamilyName = family.Name,
                        CategoryName = family.FamilyCategory?.Name ?? "Unknown",
                        TypeCount = typeCount,
                        InstanceCount = instanceCount,
                        IsEditable = family.IsEditable,
                        IsInPlace = family.IsInPlace
                    });
                }

                // Sort
                switch (SortBy.ToLowerInvariant())
                {
                    case "typecount":
                        familyInfos = familyInfos.OrderByDescending(f => f.TypeCount).ToList();
                        break;
                    case "name":
                        familyInfos = familyInfos.OrderBy(f => f.FamilyName).ToList();
                        break;
                    case "instancecount":
                    default:
                        familyInfos = familyInfos.OrderByDescending(f => f.InstanceCount).ToList();
                        break;
                }

                // Apply limit
                var limited = familyInfos.Take(Limit).ToList();

                Result = new
                {
                    totalFamilies = familyInfos.Count,
                    totalInstances = familyInfos.Sum(f => f.InstanceCount),
                    returnedCount = limited.Count,
                    truncated = familyInfos.Count > Limit,
                    sortedBy = SortBy,
                    families = limited.Select(f => new
                    {
                        familyId = f.FamilyId,
                        familyName = f.FamilyName,
                        category = f.CategoryName,
                        typeCount = f.TypeCount,
                        instanceCount = f.InstanceCount,
                        isInPlace = f.IsInPlace,
                        isEditable = f.IsEditable
                    })
                };
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Failed to list family sizes: {ex.Message}";
            }
            finally
            {
                TaskCompleted = true;
                _resetEvent.Set();
            }
        }

        public string GetName() => "List Family Sizes";

        private class FamilyInfo
        {
            public long FamilyId { get; set; }
            public string FamilyName { get; set; }
            public string CategoryName { get; set; }
            public int TypeCount { get; set; }
            public int InstanceCount { get; set; }
            public bool IsEditable { get; set; }
            public bool IsInPlace { get; set; }
        }
    }
}
