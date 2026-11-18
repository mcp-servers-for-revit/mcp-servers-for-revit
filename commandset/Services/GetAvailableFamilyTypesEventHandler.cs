using Autodesk.Revit.UI;
using RevitMCPCommandSet.Models.Common;
using RevitMCPSDK.API.Interfaces;

namespace RevitMCPCommandSet.Services
{
    public class GetAvailableFamilyTypesEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        // Execution result
        public List<FamilyTypeInfo> ResultFamilyTypes { get; private set; }

        // Error message if execution fails
        public string ErrorMessage { get; private set; }

        // State synchronization object
        public bool TaskCompleted { get; private set; }
        private readonly ManualResetEvent _resetEvent = new ManualResetEvent(false);

        // Filter criteria
        public List<string> CategoryList { get; set; }
        public string FamilyNameFilter { get; set; }
        public int? Limit { get; set; }

        public void SetParameters(List<string> categoryList, string familyNameFilter, int? limit)
        {
            CategoryList = categoryList;
            FamilyNameFilter = familyNameFilter;
            Limit = limit;
            TaskCompleted = false;
            _resetEvent.Reset();
        }

        // Execution time, slightly shorter than the call timeout
        public bool WaitForCompletion(int timeoutMilliseconds = 12500)
        {
            return _resetEvent.WaitOne(timeoutMilliseconds);
        }

        public void Execute(UIApplication app)
        {
            try
            {
                var doc = app.ActiveUIDocument.Document;

                // Loadable families
                var familySymbols = new FilteredElementCollector(doc)
                    .OfClass(typeof(FamilySymbol))
                    .Cast<FamilySymbol>();
                // System family types (walls, floors, etc.)
                var systemTypes = new List<ElementType>();
                systemTypes.AddRange(new FilteredElementCollector(doc).OfClass(typeof(WallType)).Cast<ElementType>());
                systemTypes.AddRange(new FilteredElementCollector(doc).OfClass(typeof(FloorType)).Cast<ElementType>());
                systemTypes.AddRange(new FilteredElementCollector(doc).OfClass(typeof(RoofType)).Cast<ElementType>());
                systemTypes.AddRange(new FilteredElementCollector(doc).OfClass(typeof(CeilingType)).Cast<ElementType>());
                systemTypes.AddRange(new FilteredElementCollector(doc).OfClass(typeof(CurtainSystemType)).Cast<ElementType>());
                // Merge results
                var allElements = familySymbols
                    .Cast<ElementType>()
                    .Concat(systemTypes)
                    .ToList();

                IEnumerable<ElementType> filteredElements = allElements;

                // Category filter
                if (CategoryList != null && CategoryList.Any())
                {
                    var validCategoryIds = new List<int>();
                    foreach (var categoryName in CategoryList)
                    {
                        if (Enum.TryParse(categoryName, out BuiltInCategory bic))
                        {
                            validCategoryIds.Add((int)bic);
                        }
                    }

                    if (validCategoryIds.Any())
                    {
                        filteredElements = filteredElements.Where(et =>
                        {
#if REVIT2024_OR_GREATER
                            var categoryId = et.Category?.Id.Value;
#else
                            var categoryId = et.Category?.Id.IntegerValue;
#endif
                            return categoryId != null && validCategoryIds.Contains((int)categoryId.Value);
                        });
                    }
                }

                // Fuzzy name matching (matches both family name and type name)
                if (!string.IsNullOrEmpty(FamilyNameFilter))
                {
                    filteredElements = filteredElements.Where(et =>
                    {
                        string familyName = et is FamilySymbol fs ? fs.FamilyName : et.get_Parameter(
                            BuiltInParameter.SYMBOL_FAMILY_NAME_PARAM)?.AsString() ?? "";

                        return familyName?.IndexOf(FamilyNameFilter, StringComparison.OrdinalIgnoreCase) >= 0 ||
                               et.Name.IndexOf(FamilyNameFilter, StringComparison.OrdinalIgnoreCase) >= 0;
                    });
                }

                // Limit return count
                if (Limit.HasValue && Limit.Value > 0)
                {
                    filteredElements = filteredElements.Take(Limit.Value);
                }

                // Convert to FamilyTypeInfo list
                ResultFamilyTypes = filteredElements.Select(et =>
                {
                    string familyName;
                    if (et is FamilySymbol fs)
                    {
                        familyName = fs.FamilyName;
                    }
                    else
                    {
                        Parameter param = et.get_Parameter(BuiltInParameter.SYMBOL_FAMILY_NAME_PARAM);
                        familyName = param?.AsString() ?? et.GetType().Name.Replace("Type", "");
                    }
                    return new FamilyTypeInfo
                    {
#if REVIT2024_OR_GREATER
                        FamilyTypeId = et.Id.Value,
#else
                        FamilyTypeId = et.Id.IntegerValue,
#endif
                        UniqueId = et.UniqueId,
                        FamilyName = familyName,
                        TypeName = et.Name,
                        Category = et.Category?.Name
                    };
                }).ToList();
            }
            catch (Exception ex)
            {
                ResultFamilyTypes = new List<FamilyTypeInfo>();
                ErrorMessage = "Failed to get family types: " + ex.Message;
            }
            finally
            {
                TaskCompleted = true;
                _resetEvent.Set();
            }
        }

        public string GetName()
        {
            return "GetAvailableFamilyTypes";
        }
    }
}
