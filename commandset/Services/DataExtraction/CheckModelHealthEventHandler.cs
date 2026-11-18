using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using RevitMCPCommandSet.Models.Common;
using RevitMCPSDK.API.Interfaces;

namespace RevitMCPCommandSet.Services.DataExtraction
{
    public class CheckModelHealthEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        public AIResult<object> Result { get; private set; }
        public bool TaskCompleted { get; private set; }
        private readonly ManualResetEvent _resetEvent = new ManualResetEvent(false);

        public void SetParameters()
        {
            TaskCompleted = false;
            _resetEvent.Reset();
        }

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

                // 1. Warnings
                var warnings = doc.GetWarnings();
                var warningGroups = warnings
                    .GroupBy(w => w.GetDescriptionText())
                    .OrderByDescending(g => g.Count())
                    .Take(10)
                    .Select(g => new { description = g.Key, count = g.Count() })
                    .ToList();

                // 2. In-place families
                int inPlaceFamilyCount = new FilteredElementCollector(doc)
                    .OfClass(typeof(FamilyInstance))
                    .Cast<FamilyInstance>()
                    .Count(fi => fi.Symbol?.Family?.IsInPlace == true);

                // 3. Imported CAD
                int importedCadCount = new FilteredElementCollector(doc)
                    .OfClass(typeof(ImportInstance))
                    .GetElementCount();

                // 4. Unplaced rooms
                int unplacedRoomCount = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_Rooms)
                    .WhereElementIsNotElementType()
                    .Cast<Room>()
                    .Count(r => r.Area <= 0);

                // 5. Views not on sheets
                var allViews = new FilteredElementCollector(doc)
                    .OfClass(typeof(View))
                    .Cast<View>()
                    .Where(v => !v.IsTemplate && v.CanBePrinted)
                    .ToList();

                var viewsOnSheets = new FilteredElementCollector(doc)
                    .OfClass(typeof(Viewport))
                    .Cast<Viewport>()
                    .Select(vp => vp.ViewId)
                    .ToHashSet();

                int unusedViewCount = allViews.Count(v => !viewsOnSheets.Contains(v.Id));

                // 6. Total elements
                int totalElements = new FilteredElementCollector(doc)
                    .WhereElementIsNotElementType()
                    .GetElementCount();

                // 7. Detail lines
                int detailLineCount = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_Lines)
                    .WhereElementIsNotElementType()
                    .GetElementCount();

                // 8. Calculate health score (0-100)
                int score = 100;
                score -= Math.Min(30, warnings.Count / 10);
                score -= Math.Min(15, inPlaceFamilyCount * 3);
                score -= Math.Min(10, importedCadCount * 2);
                score -= Math.Min(10, unplacedRoomCount * 2);
                score -= Math.Min(10, unusedViewCount / 5);
                score = Math.Max(0, score);

                string grade = score >= 90 ? "A" : score >= 75 ? "B" : score >= 60 ? "C" : score >= 40 ? "D" : "F";

                var recommendations = new List<string>();
                if (warnings.Count > 50) recommendations.Add($"Resolve {warnings.Count} warnings to improve model stability");
                if (inPlaceFamilyCount > 0) recommendations.Add($"Convert {inPlaceFamilyCount} in-place families to loadable families");
                if (importedCadCount > 0) recommendations.Add($"Remove or link {importedCadCount} imported CAD instances");
                if (unplacedRoomCount > 0) recommendations.Add($"Place or delete {unplacedRoomCount} unplaced rooms");
                if (unusedViewCount > 10) recommendations.Add($"Delete {unusedViewCount} unused views to reduce file size");
                if (recommendations.Count == 0) recommendations.Add("Model is in good health!");

                Result = new AIResult<object>
                {
                    Success = true,
                    Message = $"Model health check complete. Score: {score}/100 (Grade: {grade})",
                    Response = new
                    {
                        score,
                        grade,
                        totalElements,
                        warnings = new { total = warnings.Count, top10 = warningGroups },
                        inPlaceFamilies = inPlaceFamilyCount,
                        importedCad = importedCadCount,
                        unplacedRooms = unplacedRoomCount,
                        unusedViews = unusedViewCount,
                        detailLines = detailLineCount,
                        recommendations
                    }
                };
            }
            catch (Exception ex)
            {
                Result = new AIResult<object> { Success = false, Message = $"Model health check failed: {ex.Message}" };
            }
            finally
            {
                TaskCompleted = true;
                _resetEvent.Set();
            }
        }

        public string GetName() => "Check Model Health";
    }
}
