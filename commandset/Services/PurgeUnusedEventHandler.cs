using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCPCommandSet.Models.Common;
using RevitMCPSDK.API.Interfaces;

namespace RevitMCPCommandSet.Services
{
    public class PurgeUnusedEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        private readonly ManualResetEvent _resetEvent = new ManualResetEvent(false);

        public bool DryRun { get; set; } = true;
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

                // Revit 2024+ has GetUnusedElements
                var allIds = new HashSet<ElementId>();
                var purgeableIds = doc.GetUnusedElements(allIds);
                var purgeableList = purgeableIds.ToList();

                // Categorize purgeable elements
                var categories = new Dictionary<string, List<object>>();
                int count = 0;

                foreach (var id in purgeableList)
                {
                    if (count >= MaxElements) break;

                    var elem = doc.GetElement(id);
                    if (elem == null) continue;

                    var catName = elem.Category?.Name ?? "Uncategorized";
                    if (!categories.ContainsKey(catName))
                        categories[catName] = new List<object>();

                    categories[catName].Add(new
                    {
                        id = id.Value,
                        name = elem.Name ?? "(unnamed)",
                        typeName = elem.GetType().Name
                    });

                    count++;
                }

                // Build summary
                var summary = new List<object>();
                foreach (var kvp in categories.OrderByDescending(c => c.Value.Count))
                {
                    summary.Add(new
                    {
                        category = kvp.Key,
                        count = kvp.Value.Count,
                        elements = kvp.Value
                    });
                }

                int deletedCount = 0;
                if (!DryRun && purgeableList.Count > 0)
                {
                    var idsToDelete = purgeableList.Take(MaxElements).ToList();
                    using (var transaction = new Transaction(doc, "Purge Unused Elements"))
                    {
                        transaction.Start();
                        foreach (var id in idsToDelete)
                        {
                            try
                            {
                                doc.Delete(id);
                                deletedCount++;
                            }
                            catch { /* skip elements that can't be deleted */ }
                        }
                        transaction.Commit();
                    }
                }

                Result = new AIResult<object>
                {
                    Success = true,
                    Message = DryRun
                        ? $"Found {purgeableList.Count} purgeable elements (dry run - nothing deleted)"
                        : $"Purged {deletedCount} of {purgeableList.Count} unused elements",
                    Response = new
                    {
                        totalPurgeable = purgeableList.Count,
                        dryRun = DryRun,
                        deletedCount = deletedCount,
                        categorySummary = summary
                    }
                };
            }
            catch (Exception ex)
            {
                Result = new AIResult<object>
                {
                    Success = false,
                    Message = $"Failed to purge unused: {ex.Message}"
                };
            }
            finally
            {
                _resetEvent.Set();
            }
        }

        private List<ElementId> GetPurgeableElementsFallback(Document doc)
        {
            var purgeableIds = new List<ElementId>();

            // Use PerformanceAdviser to find purgeable elements
            var adviser = PerformanceAdviser.GetPerformanceAdviser();
            var allRules = adviser.GetAllRuleIds();

            foreach (var ruleId in allRules)
            {
                var name = adviser.GetRuleName(ruleId);
                // "Project contains unused families and types" rule
                if (name.Contains("unused", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("purg", StringComparison.OrdinalIgnoreCase))
                {
                    var failures = adviser.ExecuteRules(doc, new List<PerformanceAdviserRuleId> { ruleId });
                    foreach (var failure in failures)
                    {
                        var failingIds = failure.GetFailingElements();
                        purgeableIds.AddRange(failingIds);
                    }
                }
            }

            return purgeableIds.Distinct().ToList();
        }

        public string GetName() => "Purge Unused";
    }
}
