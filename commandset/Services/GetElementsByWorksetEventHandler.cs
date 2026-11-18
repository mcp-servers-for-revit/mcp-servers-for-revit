using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCPCommandSet.Models.Common;
using RevitMCPSDK.API.Interfaces;

namespace RevitMCPCommandSet.Services
{
    public class GetElementsByWorksetEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        private readonly ManualResetEvent _resetEvent = new ManualResetEvent(false);

        public string WorksetName { get; set; }
        public List<string> CategoryFilter { get; set; }
        public int MaxElements { get; set; } = 500;
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

                if (!doc.IsWorkshared)
                {
                    Result = new AIResult<object>
                    {
                        Success = false,
                        Message = "Project is not workshared"
                    };
                    return;
                }

                // Find workset by name
                Workset targetWorkset = null;
                foreach (var ws in new FilteredWorksetCollector(doc).OfKind(WorksetKind.UserWorkset))
                {
                    if (ws.Name.Equals(WorksetName, StringComparison.OrdinalIgnoreCase))
                    {
                        targetWorkset = ws;
                        break;
                    }
                }

                if (targetWorkset == null)
                {
                    Result = new AIResult<object>
                    {
                        Success = false,
                        Message = $"Workset '{WorksetName}' not found"
                    };
                    return;
                }

                // Use ElementWorksetFilter (quick filter) for performance
                var collector = new FilteredElementCollector(doc)
                    .WherePasses(new ElementWorksetFilter(targetWorkset.Id, false))
                    .WhereElementIsNotElementType();

                var elements = new List<object>();
                var categoryCounts = new Dictionary<string, int>();
                int totalCount = 0;

                // Build category filter set if provided
                HashSet<string> allowedCategories = null;
                if (CategoryFilter != null && CategoryFilter.Count > 0)
                {
                    allowedCategories = new HashSet<string>(CategoryFilter, StringComparer.OrdinalIgnoreCase);
                }

                foreach (var elem in collector)
                {
                    var categoryName = elem.Category?.Name;
                    if (categoryName == null) continue;

                    // Track category counts regardless of limit
                    if (categoryCounts.ContainsKey(categoryName))
                        categoryCounts[categoryName]++;
                    else
                        categoryCounts[categoryName] = 1;

                    totalCount++;

                    // Apply category filter
                    if (allowedCategories != null && !allowedCategories.Contains(categoryName))
                        continue;

                    if (elements.Count < MaxElements)
                    {
                        elements.Add(new
                        {
#if REVIT2024_OR_GREATER
                            elementId = elem.Id.Value,
#else
                            elementId = (long)elem.Id.IntegerValue,
#endif
                            name = elem.Name,
                            category = categoryName,
                            familyName = (elem is FamilyInstance fi) ? fi.Symbol?.Family?.Name : null,
                            typeName = elem.GetTypeId() != ElementId.InvalidElementId
                                ? doc.GetElement(elem.GetTypeId())?.Name
                                : null,
#if REVIT2024_OR_GREATER
                            levelId = elem.LevelId?.Value,
#else
                            levelId = elem.LevelId != null ? (long?)elem.LevelId.IntegerValue : null,
#endif
                            levelName = elem.LevelId != null && elem.LevelId != ElementId.InvalidElementId
                                ? doc.GetElement(elem.LevelId)?.Name
                                : null
                        });
                    }
                }

                // Sort category counts descending
                var sortedCategories = categoryCounts
                    .OrderByDescending(kv => kv.Value)
                    .Select(kv => new { category = kv.Key, count = kv.Value })
                    .ToList();

                Result = new AIResult<object>
                {
                    Success = true,
                    Message = $"Found {totalCount} element(s) in workset '{WorksetName}'" +
                              (elements.Count < totalCount ? $" (showing {elements.Count})" : ""),
                    Response = new
                    {
                        worksetId = targetWorkset.Id.IntegerValue,
                        worksetName = targetWorkset.Name,
                        totalElements = totalCount,
                        returnedElements = elements.Count,
                        categorySummary = sortedCategories,
                        elements = elements
                    }
                };
            }
            catch (Exception ex)
            {
                Result = new AIResult<object>
                {
                    Success = false,
                    Message = $"Failed to get elements by workset: {ex.Message}"
                };
            }
            finally
            {
                _resetEvent.Set();
            }
        }

        public string GetName() => "Get Elements By Workset";
    }
}
