using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCPCommandSet.Models.DataExtraction;
using RevitMCPSDK.API.Interfaces;

namespace RevitMCPCommandSet.Services.DataExtraction
{
    public class AnalyzeModelStatisticsEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        private bool _includeDetailedTypes;

        public AnalyzeModelStatisticsResult ResultInfo { get; private set; }
        public bool TaskCompleted { get; private set; }
        private readonly ManualResetEvent _resetEvent = new ManualResetEvent(false);

        public void SetParameters(bool includeDetailedTypes = true)
        {
            _includeDetailedTypes = includeDetailedTypes;
            TaskCompleted = false;
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

                // Get project name
                string projectName = doc.Title;

                // Count total elements
                int totalElements = new FilteredElementCollector(doc)
                    .WhereElementIsNotElementType()
                    .GetElementCount();

                // Count total types
                int totalTypes = new FilteredElementCollector(doc)
                    .WhereElementIsElementType()
                    .GetElementCount();

                // Count views
                int totalViews = new FilteredElementCollector(doc)
                    .OfClass(typeof(View))
                    .Where(v => !(v as View).IsTemplate)
                    .Count();

                // Count sheets
                int totalSheets = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewSheet))
                    .GetElementCount();

                // Analyze by category
                var categoryStats = new Dictionary<string, CategoryStatistics>();
                var familyNames = new HashSet<string>();

                var elements = new FilteredElementCollector(doc)
                    .WhereElementIsNotElementType()
                    .ToElements();

                foreach (Element elem in elements)
                {
                    if (elem.Category == null) continue;

                    string catName = elem.Category.Name;

                    if (!categoryStats.ContainsKey(catName))
                    {
                        categoryStats[catName] = new CategoryStatistics
                        {
                            CategoryName = catName
                        };
                    }

                    categoryStats[catName].ElementCount++;

                    // Track type information
                    if (elem is FamilyInstance fi)
                    {
                        string familyName = fi.Symbol?.Family?.Name;
                        string typeName = fi.Symbol?.Name;

                        if (!string.IsNullOrEmpty(familyName))
                        {
                            familyNames.Add(familyName);
                        }

                        if (_includeDetailedTypes && !string.IsNullOrEmpty(typeName))
                        {
                            var existingType = categoryStats[catName].Types
                                .FirstOrDefault(t => t.TypeName == typeName && t.FamilyName == familyName);

                            if (existingType != null)
                            {
                                existingType.InstanceCount++;
                            }
                            else
                            {
                                categoryStats[catName].Types.Add(new TypeStatistics
                                {
                                    TypeName = typeName,
                                    FamilyName = familyName,
                                    InstanceCount = 1
                                });
                            }
                        }
                    }
                }

                // Calculate type and family counts per category
                foreach (var stat in categoryStats.Values)
                {
                    stat.TypeCount = stat.Types.Select(t => t.TypeName).Distinct().Count();
                    stat.FamilyCount = stat.Types.Select(t => t.FamilyName).Distinct().Count();
                }

                // Analyze by level
                var levelStats = new List<LevelStatistics>();
                var levels = new FilteredElementCollector(doc)
                    .OfClass(typeof(Level))
                    .Cast<Level>()
                    .OrderBy(l => l.Elevation);

                foreach (Level level in levels)
                {
                    int elementCount = new FilteredElementCollector(doc)
                        .WhereElementIsNotElementType()
                        .Where(e => e.LevelId == level.Id)
                        .Count();

                    levelStats.Add(new LevelStatistics
                    {
                        LevelName = level.Name,
                        Elevation = level.Elevation,
                        ElementCount = elementCount
                    });
                }

                ResultInfo = new AnalyzeModelStatisticsResult
                {
                    ProjectName = projectName,
                    TotalElements = totalElements,
                    TotalTypes = totalTypes,
                    TotalFamilies = familyNames.Count,
                    TotalViews = totalViews,
                    TotalSheets = totalSheets,
                    Categories = categoryStats.Values.OrderByDescending(c => c.ElementCount).ToList(),
                    Levels = levelStats,
                    Success = true,
                    Message = $"Successfully analyzed model with {totalElements} elements across {categoryStats.Count} categories"
                };
            }
            catch (Exception ex)
            {
                ResultInfo = new AnalyzeModelStatisticsResult
                {
                    Success = false,
                    Message = $"Error analyzing model statistics: {ex.Message}"
                };
            }
            finally
            {
                TaskCompleted = true;
                _resetEvent.Set();
            }
        }

        public string GetName()
        {
            return "Analyze Model Statistics";
        }
    }
}
