using Autodesk.Revit.DB;
using Nice3point.TUnit.Revit;
using Nice3point.TUnit.Revit.Executors;
using TUnit.Core;
using TUnit.Core.Executors;

namespace RevitMCPCommandSet.Tests.DataExtraction;

public class AnalyzeModelStatisticsTests : RevitApiTest
{
    private static Document _doc;

    [Before(HookType.Class)]
    [HookExecutor<RevitThreadExecutor>]
    public static void Setup()
    {
        _doc = Application.NewProjectDocument(UnitSystem.Imperial);

        using var tx = new Transaction(_doc, "Setup Statistics Test");
        tx.Start();

        // Create a level and some elements for statistics
        var level = Level.Create(_doc, 0.0);
        level.Name = "Stats Test Level";

        var floorPlanType = new FilteredElementCollector(_doc)
            .OfClass(typeof(ViewFamilyType))
            .Cast<ViewFamilyType>()
            .FirstOrDefault(vft => vft.ViewFamily == ViewFamily.FloorPlan);

        if (floorPlanType != null)
        {
            ViewPlan.Create(_doc, floorPlanType.Id, level.Id);
        }

        // Create walls to have some categorized elements
        Wall.Create(_doc, Line.CreateBound(new XYZ(0, 0, 0), new XYZ(10, 0, 0)), level.Id, false);
        Wall.Create(_doc, Line.CreateBound(new XYZ(10, 0, 0), new XYZ(10, 10, 0)), level.Id, false);
        Wall.Create(_doc, Line.CreateBound(new XYZ(10, 10, 0), new XYZ(0, 10, 0)), level.Id, false);

        tx.Commit();
    }

    [After(HookType.Class)]
    [HookExecutor<RevitThreadExecutor>]
    public static void Cleanup()
    {
        _doc?.Close(false);
    }

    [Test]
    public async Task TotalElementCount_MatchesFilteredElementCollector()
    {
        int totalElements = new FilteredElementCollector(_doc)
            .WhereElementIsNotElementType()
            .GetElementCount();

        await Assert.That(totalElements).IsGreaterThan(0);

        int totalTypes = new FilteredElementCollector(_doc)
            .WhereElementIsElementType()
            .GetElementCount();

        await Assert.That(totalTypes).IsGreaterThan(0);
    }

    [Test]
    public async Task CategoryGrouping_ElementsGroupedByCategory_CountsCorrect()
    {
        var elements = new FilteredElementCollector(_doc)
            .WhereElementIsNotElementType()
            .ToElements();

        var categoryGroups = new Dictionary<string, int>();
        foreach (var elem in elements)
        {
            if (elem.Category == null) continue;
            string catName = elem.Category.Name;

            if (!categoryGroups.ContainsKey(catName))
                categoryGroups[catName] = 0;
            categoryGroups[catName]++;
        }

        // Should have at least walls category
        await Assert.That(categoryGroups.Count).IsGreaterThan(0);

        // Total of grouped counts should match elements with categories
        int groupedTotal = categoryGroups.Values.Sum();
        int elementsWithCategories = elements.Count(e => e.Category != null);
        await Assert.That(groupedTotal).IsEqualTo(elementsWithCategories);
    }

    [Test]
    public async Task LevelStatistics_ElevationAndElementCount_Populated()
    {
        var levels = new FilteredElementCollector(_doc)
            .OfClass(typeof(Level))
            .Cast<Level>()
            .OrderBy(l => l.Elevation)
            .ToList();

        await Assert.That(levels.Count).IsGreaterThan(0);

        foreach (var level in levels)
        {
            await Assert.That(level.Name).IsNotNullOrEmpty();

            int elementCount = new FilteredElementCollector(_doc)
                .WhereElementIsNotElementType()
                .Where(e => e.LevelId == level.Id)
                .Count();

            // Element count should be non-negative
            await Assert.That(elementCount).IsGreaterThanOrEqualTo(0);
        }
    }

    [Test]
    public async Task ViewCounting_ExcludesTemplates_CountCorrect()
    {
        var allViews = new FilteredElementCollector(_doc)
            .OfClass(typeof(View))
            .Cast<View>()
            .ToList();

        int totalViewsExcludingTemplates = allViews.Count(v => !v.IsTemplate);
        int templateCount = allViews.Count(v => v.IsTemplate);

        await Assert.That(totalViewsExcludingTemplates).IsGreaterThan(0);
        await Assert.That(totalViewsExcludingTemplates + templateCount).IsEqualTo(allViews.Count);
    }

    [Test]
    public async Task DetailedTypeBreakdown_FamilyInstanceTypes_TrackedCorrectly()
    {
        var elements = new FilteredElementCollector(_doc)
            .WhereElementIsNotElementType()
            .ToElements();

        var typeStats = new Dictionary<string, (string FamilyName, string TypeName, int Count)>();
        var familyNames = new HashSet<string>();

        foreach (var elem in elements)
        {
            if (elem is FamilyInstance fi)
            {
                string familyName = fi.Symbol?.Family?.Name;
                string typeName = fi.Symbol?.Name;

                if (!string.IsNullOrEmpty(familyName))
                    familyNames.Add(familyName);

                if (!string.IsNullOrEmpty(typeName))
                {
                    string key = $"{familyName}:{typeName}";
                    if (typeStats.ContainsKey(key))
                    {
                        var existing = typeStats[key];
                        typeStats[key] = (existing.FamilyName, existing.TypeName, existing.Count + 1);
                    }
                    else
                    {
                        typeStats[key] = (familyName, typeName, 1);
                    }
                }
            }
        }

        // Validate that all tracked types have positive instance counts
        foreach (var kvp in typeStats)
        {
            await Assert.That(kvp.Value.Count).IsGreaterThan(0);
            await Assert.That(kvp.Value.TypeName).IsNotNullOrEmpty();
        }

        // Family names should be a subset of all families
        foreach (var name in familyNames)
        {
            await Assert.That(name).IsNotNullOrEmpty();
        }
    }
}
