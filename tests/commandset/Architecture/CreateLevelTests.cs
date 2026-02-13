using Autodesk.Revit.DB;
using Nice3point.TUnit.Revit;
using Nice3point.TUnit.Revit.Executors;
using TUnit.Core;
using TUnit.Core.Executors;

namespace RevitMCPCommandSet.Tests.Architecture;

public class CreateLevelTests : RevitApiTest
{
    private static Document _doc;

    [Before(HookType.Class)]
    [HookExecutor<RevitThreadExecutor>]
    public static void Setup()
    {
        _doc = Application.NewProjectDocument(UnitSystem.Imperial);
    }

    [After(HookType.Class)]
    [HookExecutor<RevitThreadExecutor>]
    public static void Cleanup()
    {
        _doc?.Close(false);
    }

    [Test]
    public async Task CreateLevel_AtElevation_LevelExistsWithCorrectElevation()
    {
        double elevationMm = 3000;
        double elevationFeet = elevationMm / 304.8;

        using var tx = new Transaction(_doc, "Create Level");
        tx.Start();

        var level = Level.Create(_doc, elevationFeet);

        tx.Commit();

        await Assert.That(level).IsNotNull();
        await Assert.That(level.Elevation).IsEqualTo(elevationFeet).Within(0.001);
    }

    [Test]
    public async Task CreateLevel_SetName_NameIsApplied()
    {
        using var tx = new Transaction(_doc, "Create Level");
        tx.Start();

        var level = Level.Create(_doc, 20.0);
        level.Name = "Test Level Custom";

        tx.Commit();

        await Assert.That(level.Name).IsEqualTo("Test Level Custom");
    }

    [Test]
    public async Task CreateLevel_WithFloorPlan_ViewPlanCreated()
    {
        using var tx = new Transaction(_doc, "Create Level With Floor Plan");
        tx.Start();

        var level = Level.Create(_doc, 30.0);
        level.Name = "Floor Plan Level";

        var floorPlanType = new FilteredElementCollector(_doc)
            .OfClass(typeof(ViewFamilyType))
            .Cast<ViewFamilyType>()
            .FirstOrDefault(vft => vft.ViewFamily == ViewFamily.FloorPlan);

        ViewPlan floorPlan = null;
        if (floorPlanType != null)
        {
            floorPlan = ViewPlan.Create(_doc, floorPlanType.Id, level.Id);
        }

        tx.Commit();

        await Assert.That(floorPlan).IsNotNull();
        await Assert.That(floorPlan.ViewType).IsEqualTo(ViewType.FloorPlan);
    }

    [Test]
    public async Task CreateLevel_WithCeilingPlan_ViewPlanCreated()
    {
        using var tx = new Transaction(_doc, "Create Level With Ceiling Plan");
        tx.Start();

        var level = Level.Create(_doc, 40.0);
        level.Name = "Ceiling Plan Level";

        var ceilingPlanType = new FilteredElementCollector(_doc)
            .OfClass(typeof(ViewFamilyType))
            .Cast<ViewFamilyType>()
            .FirstOrDefault(vft => vft.ViewFamily == ViewFamily.CeilingPlan);

        ViewPlan ceilingPlan = null;
        if (ceilingPlanType != null)
        {
            ceilingPlan = ViewPlan.Create(_doc, ceilingPlanType.Id, level.Id);
        }

        tx.Commit();

        await Assert.That(ceilingPlan).IsNotNull();
        await Assert.That(ceilingPlan.ViewType).IsEqualTo(ViewType.CeilingPlan);
    }

    [Test]
    public async Task CreateLevel_DuplicateName_ExistingLevelFound()
    {
        using (var tx = new Transaction(_doc, "Create Original Level"))
        {
            tx.Start();
            var original = Level.Create(_doc, 50.0);
            original.Name = "Duplicate Test Level";
            tx.Commit();
        }

        var existingLevels = new FilteredElementCollector(_doc)
            .OfClass(typeof(Level))
            .Cast<Level>()
            .ToList();

        var existing = existingLevels.FirstOrDefault(
            l => l.Name.Equals("Duplicate Test Level", StringComparison.OrdinalIgnoreCase));

        await Assert.That(existing).IsNotNull();
        await Assert.That(existing.Name).IsEqualTo("Duplicate Test Level");
    }

    [Test]
    public async Task CreateLevel_SetIsBuildingStory_ParameterValueSet()
    {
        using var tx = new Transaction(_doc, "Create Level Building Story");
        tx.Start();

        var level = Level.Create(_doc, 60.0);
        level.Name = "Building Story Level";

        var param = level.get_Parameter(BuiltInParameter.LEVEL_IS_BUILDING_STORY);
        if (param != null && !param.IsReadOnly)
        {
            param.Set(0); // Set to NOT a building story
        }

        tx.Commit();

        var readParam = level.get_Parameter(BuiltInParameter.LEVEL_IS_BUILDING_STORY);
        await Assert.That(readParam).IsNotNull();
        await Assert.That(readParam.AsInteger()).IsEqualTo(0);
    }

    [Test]
    public async Task CreateLevel_MultipleLevels_AllCreatedAtCorrectElevations()
    {
        var elevationsMm = new[] { 0.0, 3000.0, 6000.0, 9000.0 };
        var createdLevels = new List<Level>();

        using var tx = new Transaction(_doc, "Create Multiple Levels");
        tx.Start();

        for (int i = 0; i < elevationsMm.Length; i++)
        {
            double elevFeet = elevationsMm[i] / 304.8;
            var level = Level.Create(_doc, elevFeet);
            level.Name = $"Batch Level {i}";
            createdLevels.Add(level);
        }

        tx.Commit();

        await Assert.That(createdLevels.Count).IsEqualTo(4);
        for (int i = 0; i < elevationsMm.Length; i++)
        {
            double expectedFeet = elevationsMm[i] / 304.8;
            await Assert.That(createdLevels[i].Elevation).IsEqualTo(expectedFeet).Within(0.001);
        }
    }

    [Test]
    public async Task CreateLevel_ElevationConversion_MmToFeetAccurate()
    {
        double elevationMm = 3048.0; // Exactly 10 feet
        double elevationFeet = elevationMm / 304.8;

        await Assert.That(elevationFeet).IsEqualTo(10.0).Within(0.0001);

        double roundTrip = elevationFeet * 304.8;
        await Assert.That(roundTrip).IsEqualTo(elevationMm).Within(0.0001);
    }

    [Test]
    public async Task CreateLevel_RollbackOnFailure_LevelNotPersisted()
    {
        int levelCountBefore = new FilteredElementCollector(_doc)
            .OfClass(typeof(Level))
            .GetElementCount();

        using (var tx = new Transaction(_doc, "Create Level Rollback"))
        {
            tx.Start();
            Level.Create(_doc, 70.0);
            tx.RollBack();
        }

        int levelCountAfter = new FilteredElementCollector(_doc)
            .OfClass(typeof(Level))
            .GetElementCount();

        await Assert.That(levelCountAfter).IsEqualTo(levelCountBefore);
    }
}
