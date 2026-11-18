using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCPCommandSet.Models.Common;
using RevitMCPCommandSet.Utils;
using RevitMCPSDK.API.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace RevitMCPCommandSet.Services.DataExtraction
{
    public class AuditFamiliesEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        public bool IncludeUnused { get; set; } = true;
        public string CategoryFilter { get; set; } = "";

        public AIResult<object> Result { get; private set; }
        public bool TaskCompleted { get; private set; }
        private readonly ManualResetEvent _resetEvent = new ManualResetEvent(false);

        public void SetParameters() { TaskCompleted = false; _resetEvent.Reset(); }
        public bool WaitForCompletion(int timeoutMilliseconds = 60000) { return _resetEvent.WaitOne(timeoutMilliseconds); }

        private class FamilyDetail
        {
            public long FamilyId { get; set; }
            public string FamilyName { get; set; }
            public string Category { get; set; }
            public bool IsInPlace { get; set; }
            public bool IsEditable { get; set; }
            public int InstanceCount { get; set; }
            public int TypeCount { get; set; }
            public bool IsUnused { get; set; }
            public List<object> Types { get; set; }
        }

        public void Execute(UIApplication app)
        {
            try
            {
                var doc = app.ActiveUIDocument.Document;

                // Get all families
                var families = new FilteredElementCollector(doc)
                    .OfClass(typeof(Family))
                    .Cast<Family>()
                    .ToList();

                if (!string.IsNullOrEmpty(CategoryFilter))
                {
                    var targetCatId = CategoryResolver.ResolveToId(doc, CategoryFilter);
                    families = families.Where(f => f.FamilyCategory != null &&
                        f.FamilyCategory.Id.Equals(targetCatId)).ToList();
                }

                // Count instances per family
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

                var inPlaceFamilies = families.Where(f => f.IsInPlace).ToList();

                var cadImports = new FilteredElementCollector(doc)
                    .OfClass(typeof(ImportInstance))
                    .ToList();

                // Build family details
                var familyDetails = new List<FamilyDetail>();
                int unusedCount = 0;
                int inPlaceCount = inPlaceFamilies.Count;

                foreach (var family in families)
                {
#if REVIT2024_OR_GREATER
                    long familyId = family.Id.Value;
#else
                    long familyId = family.Id.IntegerValue;
#endif
                    int instanceCount = instanceCountByFamily.ContainsKey(familyId) ? instanceCountByFamily[familyId] : 0;
                    bool isUnused = instanceCount == 0;
                    if (isUnused) unusedCount++;

                    if (!IncludeUnused && isUnused) continue;

                    var typeIds = family.GetFamilySymbolIds();
                    int typeCount = typeIds?.Count ?? 0;

                    var types = new List<object>();
                    if (typeIds != null)
                    {
                        foreach (var typeId in typeIds)
                        {
                            var symbol = doc.GetElement(typeId) as FamilySymbol;
                            if (symbol == null) continue;

                            int typeInstanceCount = allInstances.Count(fi => fi.Symbol.Id == typeId);
                            types.Add(new
                            {
#if REVIT2024_OR_GREATER
                                typeId = typeId.Value,
#else
                                typeId = typeId.IntegerValue,
#endif
                                typeName = symbol.Name,
                                instanceCount = typeInstanceCount,
                                isUnused = typeInstanceCount == 0
                            });
                        }
                    }

                    familyDetails.Add(new FamilyDetail
                    {
                        FamilyId = familyId,
                        FamilyName = family.Name,
                        Category = family.FamilyCategory?.Name ?? "Unknown",
                        IsInPlace = family.IsInPlace,
                        IsEditable = family.IsEditable,
                        InstanceCount = instanceCount,
                        TypeCount = typeCount,
                        IsUnused = isUnused,
                        Types = types
                    });
                }

                // Summary by category
                var categoryBreakdown = familyDetails
                    .GroupBy(f => f.Category)
                    .Select(g => new
                    {
                        category = g.Key,
                        familyCount = g.Count(),
                        totalInstances = g.Sum(f => f.InstanceCount),
                        unusedFamilies = g.Count(f => f.IsUnused),
                        inPlaceFamilies = g.Count(f => f.IsInPlace)
                    })
                    .OrderByDescending(c => c.totalInstances)
                    .ToList();

                // Health score
                int totalFamilies = families.Count;
                double unusedRatio = totalFamilies > 0 ? (double)unusedCount / totalFamilies : 0;

                int healthScore = 100;
                healthScore -= (int)(unusedRatio * 30);
                healthScore -= Math.Min(inPlaceCount * 5, 30);
                healthScore -= Math.Min(cadImports.Count * 2, 20);
                healthScore = Math.Max(0, Math.Min(100, healthScore));

                string grade = healthScore >= 90 ? "A"
                             : healthScore >= 75 ? "B"
                             : healthScore >= 60 ? "C"
                             : healthScore >= 40 ? "D"
                             : "F";

                var recommendations = new List<string>();
                if (unusedCount > 0)
                    recommendations.Add($"Purge {unusedCount} unused families to reduce file size");
                if (inPlaceCount > 0)
                    recommendations.Add($"Convert {inPlaceCount} in-place families to loadable families for reusability");
                if (cadImports.Count > 0)
                    recommendations.Add($"Remove or clean up {cadImports.Count} CAD imports in the project");

                Result = new AIResult<object>
                {
                    Success = true,
                    Message = $"Audited {totalFamilies} families. Health: {grade} ({healthScore}/100)",
                    Response = new
                    {
                        healthScore,
                        grade,
                        summary = new
                        {
                            totalFamilies,
                            totalInstances = allInstances.Count,
                            unusedFamilies = unusedCount,
                            inPlaceFamilies = inPlaceCount,
                            cadImports = cadImports.Count,
                            categories = categoryBreakdown.Count
                        },
                        recommendations,
                        categoryBreakdown,
                        families = familyDetails
                            .OrderByDescending(f => f.InstanceCount)
                            .Take(100)
                            .Select(f => new
                            {
                                f.FamilyId,
                                f.FamilyName,
                                f.Category,
                                f.IsInPlace,
                                f.IsEditable,
                                f.InstanceCount,
                                f.TypeCount,
                                f.IsUnused,
                                f.Types
                            })
                            .ToList()
                    }
                };
            }
            catch (Exception ex)
            {
                Result = new AIResult<object> { Success = false, Message = $"Audit families failed: {ex.Message}" };
            }
            finally
            {
                TaskCompleted = true;
                _resetEvent.Set();
            }
        }

        public string GetName() => "Audit Families";
    }
}
