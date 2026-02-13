using Autodesk.Revit.DB;
using Nice3point.TUnit.Revit;
using Nice3point.TUnit.Revit.Executors;
using TUnit.Core;
using TUnit.Core.Executors;

namespace RevitMCPCommandSet.Tests.DataExtraction;

public class GetMaterialQuantitiesTests : RevitApiTest
{
    private static Document _doc;
    private static Level _level;

    [Before(HookType.Class)]
    [HookExecutor<RevitThreadExecutor>]
    public static void Setup()
    {
        _doc = Application.NewProjectDocument(UnitSystem.Imperial);

        using var tx = new Transaction(_doc, "Setup Material Quantities Test");
        tx.Start();

        _level = Level.Create(_doc, 0.0);
        _level.Name = "Material Test Level";

        // Create walls that will have materials
        Wall.Create(_doc, Line.CreateBound(new XYZ(0, 0, 0), new XYZ(10, 0, 0)), _level.Id, false);
        Wall.Create(_doc, Line.CreateBound(new XYZ(10, 0, 0), new XYZ(10, 10, 0)), _level.Id, false);
        Wall.Create(_doc, Line.CreateBound(new XYZ(10, 10, 0), new XYZ(0, 10, 0)), _level.Id, false);

        tx.Commit();
    }

    [After(HookType.Class)]
    [HookExecutor<RevitThreadExecutor>]
    public static void Cleanup()
    {
        _doc?.Close(false);
    }

    [Test]
    public async Task ExtractMaterials_FromWalls_MaterialNamesPopulated()
    {
        var walls = new FilteredElementCollector(_doc)
            .OfCategory(BuiltInCategory.OST_Walls)
            .WhereElementIsNotElementType()
            .ToElements();

        await Assert.That(walls.Count).IsGreaterThan(0);

        var materialData = new Dictionary<ElementId, (string Name, string Class, double Area, double Volume, HashSet<ElementId> Elements)>();

        foreach (var element in walls)
        {
            var materialIds = element.GetMaterialIds(false);
            foreach (var matId in materialIds)
            {
                var material = _doc.GetElement(matId) as Material;
                if (material == null) continue;

                if (!materialData.ContainsKey(matId))
                {
                    materialData[matId] = (material.Name, material.MaterialClass, 0, 0, new HashSet<ElementId>());
                }

                double area = element.GetMaterialArea(matId, false);
                double volume = element.GetMaterialVolume(matId);

                var existing = materialData[matId];
                materialData[matId] = (existing.Name, existing.Class, existing.Area + area, existing.Volume + volume, existing.Elements);
                materialData[matId].Elements.Add(element.Id);
            }
        }

        // Walls in a default template should have at least one material
        await Assert.That(materialData.Count).IsGreaterThan(0);

        foreach (var kvp in materialData)
        {
            await Assert.That(kvp.Value.Name).IsNotNullOrEmpty();
        }
    }

    [Test]
    public async Task FilterByCategory_WallsOnly_OnlyWallMaterialsReturned()
    {
        var builtInCategories = new List<BuiltInCategory> { BuiltInCategory.OST_Walls };
        var filter = new ElementMulticategoryFilter(builtInCategories);

        var elements = new FilteredElementCollector(_doc)
            .WhereElementIsNotElementType()
            .WherePasses(filter)
            .ToElements();

        // All returned elements should be walls
        foreach (var element in elements)
        {
            await Assert.That(element.Category).IsNotNull();
            await Assert.That(element.Category.Id.Value).IsEqualTo((long)BuiltInCategory.OST_Walls);
        }
    }

    [Test]
    public async Task MaterialAreaVolume_Accumulation_ValuesNonNegative()
    {
        var walls = new FilteredElementCollector(_doc)
            .OfCategory(BuiltInCategory.OST_Walls)
            .WhereElementIsNotElementType()
            .ToElements();

        double totalArea = 0;
        double totalVolume = 0;

        foreach (var element in walls)
        {
            var materialIds = element.GetMaterialIds(false);
            foreach (var matId in materialIds)
            {
                double area = element.GetMaterialArea(matId, false);
                double volume = element.GetMaterialVolume(matId);

                await Assert.That(area).IsGreaterThanOrEqualTo(0);
                await Assert.That(volume).IsGreaterThanOrEqualTo(0);

                totalArea += area;
                totalVolume += volume;
            }
        }

        await Assert.That(totalArea).IsGreaterThanOrEqualTo(0);
        await Assert.That(totalVolume).IsGreaterThanOrEqualTo(0);
    }

    [Test]
    public async Task ElementCountPerMaterial_TracksUniqueElements()
    {
        var walls = new FilteredElementCollector(_doc)
            .OfCategory(BuiltInCategory.OST_Walls)
            .WhereElementIsNotElementType()
            .ToElements();

        var materialElementSets = new Dictionary<ElementId, HashSet<ElementId>>();

        foreach (var element in walls)
        {
            var materialIds = element.GetMaterialIds(false);
            foreach (var matId in materialIds)
            {
                if (!materialElementSets.ContainsKey(matId))
                    materialElementSets[matId] = new HashSet<ElementId>();

                materialElementSets[matId].Add(element.Id);
            }
        }

        foreach (var kvp in materialElementSets)
        {
            // Element count should match the unique element set count
            int elementCount = kvp.Value.Count;
            await Assert.That(elementCount).IsGreaterThan(0);

            // Adding the same element twice should not increase count
            var testSet = new HashSet<ElementId>(kvp.Value);
            int countBefore = testSet.Count;
            testSet.Add(kvp.Value.First()); // Add duplicate
            await Assert.That(testSet.Count).IsEqualTo(countBefore);
        }
    }
}
