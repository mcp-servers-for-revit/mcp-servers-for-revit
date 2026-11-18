using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCPCommandSet.Models.Common;
using RevitMCPSDK.API.Interfaces;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace RevitMCPCommandSet.Services.DataExtraction
{
    public class CheckFamilyHealthEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        public string[] Categories { get; set; } = new string[0];
        public bool IncludeSystemFamilies { get; set; } = false;
        public bool IncludeFileSize { get; set; } = false;
        public string SortBy { get; set; } = "size";

        public AIResult<object> Result { get; private set; }
        public bool TaskCompleted { get; private set; }
        private readonly ManualResetEvent _resetEvent = new ManualResetEvent(false);

        public void SetParameters() { TaskCompleted = false; _resetEvent.Reset(); }
        public bool WaitForCompletion(int timeoutMilliseconds = 60000) { return _resetEvent.WaitOne(timeoutMilliseconds); }

        private class FamilyHealthInfo
        {
            public string FamilyName { get; set; }
            public string Category { get; set; }
            public int InstanceCount { get; set; }
            public int TypeCount { get; set; }
            public long FileSizeKB { get; set; }
            public bool IsEditable { get; set; }
            public bool IsInPlace { get; set; }
        }

        public void Execute(UIApplication app)
        {
            try
            {
                var doc = app.ActiveUIDocument.Document;

                // Collect all Family elements
                var families = new FilteredElementCollector(doc)
                    .OfClass(typeof(Family))
                    .Cast<Family>()
                    .ToList();

                // Filter by categories if provided
                if (Categories != null && Categories.Length > 0)
                {
                    var categorySet = new HashSet<string>(Categories, StringComparer.OrdinalIgnoreCase);
                    families = families
                        .Where(f => f.FamilyCategory != null && categorySet.Contains(f.FamilyCategory.Name))
                        .ToList();
                }

                // Filter out system families unless requested
                if (!IncludeSystemFamilies)
                {
                    families = families.Where(f => f.IsEditable).ToList();
                }

                // Pre-collect all instances and group by family ID for efficient lookup
                var allInstances = new FilteredElementCollector(doc)
                    .OfClass(typeof(FamilyInstance))
                    .Cast<FamilyInstance>()
                    .ToList();

                var instanceCountByFamily = allInstances
                    .GroupBy(fi => fi.Symbol.Family.Id)
                    .ToDictionary(
#if REVIT2024_OR_GREATER
                        g => g.Key.Value,
#else
                        g => (long)g.Key.IntegerValue,
#endif
                        g => g.Count());

                // Build results
                var results = new List<FamilyHealthInfo>();

                foreach (var family in families)
                {
#if REVIT2024_OR_GREATER
                    long familyId = family.Id.Value;
#else
                    long familyId = family.Id.IntegerValue;
#endif
                    int instanceCount = instanceCountByFamily.ContainsKey(familyId)
                        ? instanceCountByFamily[familyId]
                        : 0;

                    int typeCount = family.GetFamilySymbolIds()?.Count ?? 0;

                    long fileSizeKB = -1;

                    if (IncludeFileSize && family.IsEditable && !family.IsInPlace)
                    {
                        try
                        {
                            var familyDoc = doc.EditFamily(family);
                            if (familyDoc != null)
                            {
                                // Save to a temp file to measure size
                                var tempPath = Path.Combine(Path.GetTempPath(), $"_mcpFamilySize_{Guid.NewGuid()}.rfa");
                                try
                                {
                                    var saveOpts = new SaveAsOptions { OverwriteExistingFile = true };
                                    familyDoc.SaveAs(tempPath, saveOpts);
                                    var fileInfo = new FileInfo(tempPath);
                                    fileSizeKB = fileInfo.Length / 1024;
                                }
                                finally
                                {
                                    familyDoc.Close(false);
                                    try { File.Delete(tempPath); } catch { }
                                }
                            }
                        }
                        catch
                        {
                            // Could not open family for size measurement; leave as -1
                        }
                    }

                    results.Add(new FamilyHealthInfo
                    {
                        FamilyName = family.Name,
                        Category = family.FamilyCategory?.Name ?? "Unknown",
                        InstanceCount = instanceCount,
                        TypeCount = typeCount,
                        FileSizeKB = fileSizeKB,
                        IsEditable = family.IsEditable,
                        IsInPlace = family.IsInPlace
                    });
                }

                // Sort results
                switch (SortBy)
                {
                    case "name":
                        results = results.OrderBy(r => r.FamilyName).ToList();
                        break;
                    case "instance_count":
                        results = results.OrderByDescending(r => r.InstanceCount).ToList();
                        break;
                    case "size":
                    default:
                        results = results.OrderByDescending(r => r.FileSizeKB).ToList();
                        break;
                }

                Result = new AIResult<object>
                {
                    Success = true,
                    Message = $"Analyzed {results.Count} families.",
                    Response = new
                    {
                        totalFamilies = results.Count,
                        totalInstances = results.Sum(r => r.InstanceCount),
                        inPlaceCount = results.Count(r => r.IsInPlace),
                        nonEditableCount = results.Count(r => !r.IsEditable),
                        families = results.Select(r => new
                        {
                            familyName = r.FamilyName,
                            category = r.Category,
                            instanceCount = r.InstanceCount,
                            typeCount = r.TypeCount,
                            fileSizeKB = r.FileSizeKB,
                            isEditable = r.IsEditable,
                            isInPlace = r.IsInPlace
                        }).ToList()
                    }
                };
            }
            catch (Exception ex)
            {
                Result = new AIResult<object> { Success = false, Message = $"Check family health failed: {ex.Message}" };
            }
            finally
            {
                TaskCompleted = true;
                _resetEvent.Set();
            }
        }

        public string GetName() => "Check Family Health";
    }
}
