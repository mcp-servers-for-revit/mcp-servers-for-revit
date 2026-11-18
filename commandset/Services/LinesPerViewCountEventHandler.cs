using Autodesk.Revit.UI;
using RevitMCPSDK.API.Interfaces;

namespace RevitMCPCommandSet.Services
{
    public class LinesPerViewCountEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        public int Threshold { get; set; }
        public bool IncludeDetailLines { get; set; } = true;
        public bool IncludeModelLines { get; set; } = true;
        public int Limit { get; set; } = 200;

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

                // Get all views that can own elements (floor plans, sections, elevations, drafting views)
                var views = new FilteredElementCollector(doc)
                    .OfClass(typeof(View))
                    .WhereElementIsNotElementType()
                    .Cast<View>()
                    .Where(v => !v.IsTemplate && v.CanBePrinted)
                    .ToList();

                var viewStats = new List<(int total, object data)>();
                int totalLines = 0;
                int skippedViews = 0;

                foreach (var view in views)
                {
                    try
                    {
                        int detailLineCount = 0;
                        int modelLineCount = 0;

                        if (IncludeDetailLines)
                        {
                            detailLineCount = new FilteredElementCollector(doc, view.Id)
                                .OfCategory(BuiltInCategory.OST_Lines)
                                .WhereElementIsNotElementType()
                                .GetElementCount();
                        }

                        if (IncludeModelLines)
                        {
                            modelLineCount = new FilteredElementCollector(doc, view.Id)
                                .OfCategory(BuiltInCategory.OST_GenericLines)
                                .WhereElementIsNotElementType()
                                .GetElementCount();
                        }

                        int total = detailLineCount + modelLineCount;

                        if (total >= Threshold)
                        {
                            viewStats.Add((total, (object)new
                            {
                                viewId =
#if REVIT2024_OR_GREATER
                                    view.Id.Value,
#else
                                    view.Id.IntegerValue,
#endif
                                viewName = view.Name,
                                viewType = view.ViewType.ToString(),
                                detailLines = detailLineCount,
                                modelLines = modelLineCount,
                                totalLines = total
                            }));
                        }

                        totalLines += total;
                    }
                    catch
                    {
                        skippedViews++;
                    }
                }

                // Sort by total lines descending, apply limit
                var sortedStats = viewStats
                    .OrderByDescending(v => v.total)
                    .Select(v => v.data)
                    .ToList();

                var limited = sortedStats.Take(Limit).ToList();

                Result = new
                {
                    totalViewsScanned = views.Count,
                    totalLinesInProject = totalLines,
                    viewsAboveThreshold = sortedStats.Count,
                    returnedCount = limited.Count,
                    truncated = sortedStats.Count > Limit,
                    threshold = Threshold,
                    skippedViews = skippedViews,
                    views = limited
                };
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Failed to count lines per view: {ex.Message}";
            }
            finally
            {
                TaskCompleted = true;
                _resetEvent.Set();
            }
        }

        public string GetName() => "Lines Per View Count";
    }
}
