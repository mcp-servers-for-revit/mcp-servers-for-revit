using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using RevitMCPSDK.API.Interfaces;

namespace RevitMCPCommandSet.Services.Workflow
{
    public class WorkflowModelAuditEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        private readonly ManualResetEvent _resetEvent = new ManualResetEvent(false);

        public bool IncludeWarnings { get; set; } = true;
        public bool IncludeFamilies { get; set; } = true;
        public int MaxWarnings { get; set; } = 50;
        public object Result { get; private set; }

        public void SetParameters(bool includeWarnings, bool includeFamilies, int maxWarnings)
        {
            IncludeWarnings = includeWarnings;
            IncludeFamilies = includeFamilies;
            MaxWarnings = maxWarnings;
            _resetEvent.Reset();
        }

        public bool WaitForCompletion(int timeoutMilliseconds = 120000)
        {
            return _resetEvent.WaitOne(timeoutMilliseconds);
        }

        public void Execute(UIApplication app)
        {
            try
            {
                var doc = app.ActiveUIDocument.Document;
                var results = new Dictionary<string, object>();

                // 1. Health Score
                int score = 100;
                var deductions = new List<string>();

                // 2. Warnings
                var warnings = doc.GetWarnings();
                int warningCount = warnings.Count;
                results["warningCount"] = warningCount;

                if (warningCount > 50) { score -= 20; deductions.Add($"-20: {warningCount} warnings (>50)"); }
                else if (warningCount > 20) { score -= 10; deductions.Add($"-10: {warningCount} warnings (>20)"); }
                else if (warningCount > 0) { score -= 5; deductions.Add($"-5: {warningCount} warnings"); }

                if (IncludeWarnings)
                {
                    var topWarnings = warnings
                        .Take(MaxWarnings)
                        .Select(w => new
                        {
                            description = w.GetDescriptionText(),
#if REVIT2024_OR_GREATER
                            elementIds = w.GetFailingElements().Select(id => id.Value).ToList()
#else
                            elementIds = w.GetFailingElements().Select(id => id.IntegerValue).ToList()
#endif
                        }).ToList();
                    results["warnings"] = topWarnings;
                }

                // 3. In-place families
                var inPlaceFamilies = new FilteredElementCollector(doc)
                    .OfClass(typeof(FamilyInstance))
                    .Cast<FamilyInstance>()
                    .Where(fi => fi.Symbol?.Family?.IsInPlace == true)
                    .Select(fi => fi.Symbol.Family.Name)
                    .Distinct().ToList();
                results["inPlaceFamilyCount"] = inPlaceFamilies.Count;
                results["inPlaceFamilies"] = inPlaceFamilies;
                if (inPlaceFamilies.Count > 10) { score -= 15; deductions.Add($"-15: {inPlaceFamilies.Count} in-place families"); }
                else if (inPlaceFamilies.Count > 0) { score -= 5; deductions.Add($"-5: {inPlaceFamilies.Count} in-place families"); }

                // 4. CAD imports
                var cadImports = new FilteredElementCollector(doc)
                    .OfClass(typeof(ImportInstance))
                    .GetElementCount();
                results["cadImportCount"] = cadImports;
                if (cadImports > 5) { score -= 15; deductions.Add($"-15: {cadImports} CAD imports (>5)"); }
                else if (cadImports > 0) { score -= 5; deductions.Add($"-5: {cadImports} CAD imports"); }

                // 5. Unused families (count only)
                if (IncludeFamilies)
                {
                    var allFamilySymbols = new FilteredElementCollector(doc)
                        .OfClass(typeof(FamilySymbol))
                        .Cast<FamilySymbol>().ToList();

                    int unusedCount = 0;
                    foreach (var fs in allFamilySymbols)
                    {
                        var instances = new FilteredElementCollector(doc)
                            .OfClass(typeof(FamilyInstance))
                            .Cast<FamilyInstance>()
                            .Where(fi => fi.GetTypeId() == fs.Id);
                        if (!instances.Any()) unusedCount++;
                    }
                    results["unusedFamilyTypeCount"] = unusedCount;
                    results["totalFamilyTypeCount"] = allFamilySymbols.Count;
                    if (unusedCount > 50) { score -= 10; deductions.Add($"-10: {unusedCount} unused family types"); }
                }

                // 6. Unplaced rooms
                var unplacedRooms = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_Rooms)
                    .WhereElementIsNotElementType()
                    .Cast<Room>()
                    .Where(r => r.Area == 0).Count();
                results["unplacedRoomCount"] = unplacedRooms;
                if (unplacedRooms > 0) { score -= 5; deductions.Add($"-5: {unplacedRooms} unplaced rooms"); }

                // Final score
                score = Math.Max(0, score);
                string grade = score >= 90 ? "A" : score >= 75 ? "B" : score >= 60 ? "C" : score >= 40 ? "D" : "F";

                results["healthScore"] = score;
                results["grade"] = grade;
                results["deductions"] = deductions;
                results["summary"] = $"Model health: {grade} ({score}/100). {warningCount} warnings, " +
                    $"{inPlaceFamilies.Count} in-place families, {cadImports} CAD imports, {unplacedRooms} unplaced rooms.";

                Result = new { success = true, data = results };
            }
            catch (Exception ex)
            {
                Result = new { success = false, error = ex.Message };
            }
            finally
            {
                _resetEvent.Set();
            }
        }

        public string GetName() => "Workflow Model Audit";
    }
}
