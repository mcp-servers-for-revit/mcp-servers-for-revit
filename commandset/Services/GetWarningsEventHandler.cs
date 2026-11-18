using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCPCommandSet.Models.Common;
using RevitMCPSDK.API.Interfaces;

namespace RevitMCPCommandSet.Services
{
    public class GetWarningsEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        private readonly ManualResetEvent _resetEvent = new ManualResetEvent(false);

        public string SeverityFilter { get; set; } = "All";
        public int MaxWarnings { get; set; } = 500;
        public string CategoryFilter { get; set; } = "";
        public AIResult<object> Result { get; private set; }

        public void SetParameters(string severityFilter = "All", int maxWarnings = 500, string categoryFilter = "")
        {
            SeverityFilter = severityFilter;
            MaxWarnings = maxWarnings;
            CategoryFilter = categoryFilter;
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
                var allWarnings = doc.GetWarnings();
                var warnings = new List<object>();
                var severityCounts = new Dictionary<string, int>
                {
                    { "Error", 0 },
                    { "Warning", 0 }
                };

                int count = 0;
                foreach (var warning in allWarnings)
                {
                    if (count >= MaxWarnings) break;

                    var severity = warning.GetSeverity().ToString();

                    if (SeverityFilter != "All" && severity != SeverityFilter)
                        continue;

                    var description = warning.GetDescriptionText();

                    if (!string.IsNullOrEmpty(CategoryFilter) &&
                        description.IndexOf(CategoryFilter, StringComparison.OrdinalIgnoreCase) < 0)
                        continue;

                    if (severityCounts.ContainsKey(severity))
                        severityCounts[severity]++;

                    var failingIds = new List<long>();
                    foreach (var id in warning.GetFailingElements())
                    {
#if REVIT2024_OR_GREATER
                        failingIds.Add(id.Value);
#else
                        failingIds.Add(id.IntegerValue);
#endif
                    }

                    var additionalIds = new List<long>();
                    foreach (var id in warning.GetAdditionalElements())
                    {
#if REVIT2024_OR_GREATER
                        additionalIds.Add(id.Value);
#else
                        additionalIds.Add(id.IntegerValue);
#endif
                    }

                    warnings.Add(new
                    {
                        severity,
                        description,
                        failingElementIds = failingIds,
                        additionalElementIds = additionalIds
                    });

                    count++;
                }

                Result = new AIResult<object>
                {
                    Success = true,
                    Message = $"Retrieved {warnings.Count} warnings (total in model: {allWarnings.Count})",
                    Response = new
                    {
                        totalWarnings = allWarnings.Count,
                        returnedWarnings = warnings.Count,
                        severityCounts,
                        warnings
                    }
                };
            }
            catch (Exception ex)
            {
                Result = new AIResult<object>
                {
                    Success = false,
                    Message = $"Failed to get warnings: {ex.Message}"
                };
            }
            finally
            {
                _resetEvent.Set();
            }
        }

        public string GetName() => "Get Warnings";
    }
}
