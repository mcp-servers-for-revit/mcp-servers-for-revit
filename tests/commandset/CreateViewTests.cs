using Autodesk.Revit.DB;
using Nice3point.TUnit.Revit;
using Nice3point.TUnit.Revit.Executors;
using TUnit.Core;
using TUnit.Core.Executors;

namespace RevitMCPCommandSet.Tests;

public class CreateViewTests : RevitApiTest
{
    private static Document _doc;
    private static Level _level;

    [Before(HookType.Class)]
    [HookExecutor<RevitThreadExecutor>]
    public static void Setup()
    {
        _doc = Application.NewProjectDocument(UnitSystem.Imperial);

        using var tx = new Transaction(_doc, "Setup View Tests");
        tx.Start();

        _level = Level.Create(_doc, 0.0);
        _level.Name = "View Test Level";

        tx.Commit();
    }

    [After(HookType.Class)]
    [HookExecutor<RevitThreadExecutor>]
    public static void Cleanup()
    {
        _doc?.Close(false);
    }

    [Test]
    public async Task CreateFloorPlanView_OnLevel_ViewCreated()
    {
        var floorPlanType = new FilteredElementCollector(_doc)
            .OfClass(typeof(ViewFamilyType))
            .Cast<ViewFamilyType>()
            .FirstOrDefault(vft => vft.ViewFamily == ViewFamily.FloorPlan);

        await Assert.That(floorPlanType).IsNotNull();

        ViewPlan view;
        using (var tx = new Transaction(_doc, "Create Floor Plan"))
        {
            tx.Start();
            view = ViewPlan.Create(_doc, floorPlanType.Id, _level.Id);
            view.Name = "Test Floor Plan";
            tx.Commit();
        }

        await Assert.That(view).IsNotNull();
        await Assert.That(view.ViewType).IsEqualTo(ViewType.FloorPlan);
        await Assert.That(view.Name).IsEqualTo("Test Floor Plan");
    }

    [Test]
    public async Task CreateCeilingPlanView_OnLevel_ViewCreated()
    {
        var ceilingPlanType = new FilteredElementCollector(_doc)
            .OfClass(typeof(ViewFamilyType))
            .Cast<ViewFamilyType>()
            .FirstOrDefault(vft => vft.ViewFamily == ViewFamily.CeilingPlan);

        await Assert.That(ceilingPlanType).IsNotNull();

        ViewPlan view;
        using (var tx = new Transaction(_doc, "Create Ceiling Plan"))
        {
            tx.Start();
            view = ViewPlan.Create(_doc, ceilingPlanType.Id, _level.Id);
            view.Name = "Test Ceiling Plan";
            tx.Commit();
        }

        await Assert.That(view).IsNotNull();
        await Assert.That(view.ViewType).IsEqualTo(ViewType.CeilingPlan);
        await Assert.That(view.Name).IsEqualTo("Test Ceiling Plan");
    }

    [Test]
    public async Task Create3DView_Isometric_ViewCreated()
    {
        var threeDType = new FilteredElementCollector(_doc)
            .OfClass(typeof(ViewFamilyType))
            .Cast<ViewFamilyType>()
            .FirstOrDefault(vft => vft.ViewFamily == ViewFamily.ThreeDimensional);

        await Assert.That(threeDType).IsNotNull();

        View3D view;
        using (var tx = new Transaction(_doc, "Create 3D View"))
        {
            tx.Start();
            view = View3D.CreateIsometric(_doc, threeDType.Id);
            view.Name = "Test 3D View";
            tx.Commit();
        }

        await Assert.That(view).IsNotNull();
        await Assert.That(view.Name).IsEqualTo("Test 3D View");
    }

    [Test]
    public async Task CreateSectionView_WithBoundingBox_ViewCreated()
    {
        var sectionType = new FilteredElementCollector(_doc)
            .OfClass(typeof(ViewFamilyType))
            .Cast<ViewFamilyType>()
            .FirstOrDefault(vft => vft.ViewFamily == ViewFamily.Section);

        await Assert.That(sectionType).IsNotNull();

        ViewSection view;
        using (var tx = new Transaction(_doc, "Create Section View"))
        {
            tx.Start();

            var sectionBox = new BoundingBoxXYZ();
            sectionBox.Min = new XYZ(-10, -5, 0);
            sectionBox.Max = new XYZ(10, 5, 20);

            view = ViewSection.CreateSection(_doc, sectionType.Id, sectionBox);
            view.Name = "Test Section";

            tx.Commit();
        }

        await Assert.That(view).IsNotNull();
        await Assert.That(view.Name).IsEqualTo("Test Section");
    }

    [Test]
    public async Task CreateView_SetScale_ScaleApplied()
    {
        var floorPlanType = new FilteredElementCollector(_doc)
            .OfClass(typeof(ViewFamilyType))
            .Cast<ViewFamilyType>()
            .FirstOrDefault(vft => vft.ViewFamily == ViewFamily.FloorPlan);

        ViewPlan view;
        using (var tx = new Transaction(_doc, "Create Scaled View"))
        {
            tx.Start();
            view = ViewPlan.Create(_doc, floorPlanType.Id, _level.Id);
            view.Name = "Scaled View";
            view.Scale = 50;
            tx.Commit();
        }

        await Assert.That(view.Scale).IsEqualTo(50);
    }

    [Test]
    public async Task CreateView_SetDetailLevel_DetailLevelApplied()
    {
        var floorPlanType = new FilteredElementCollector(_doc)
            .OfClass(typeof(ViewFamilyType))
            .Cast<ViewFamilyType>()
            .FirstOrDefault(vft => vft.ViewFamily == ViewFamily.FloorPlan);

        ViewPlan view;
        using (var tx = new Transaction(_doc, "Create Fine Detail View"))
        {
            tx.Start();
            view = ViewPlan.Create(_doc, floorPlanType.Id, _level.Id);
            view.Name = "Fine Detail View";
            view.DetailLevel = ViewDetailLevel.Fine;
            tx.Commit();
        }

        await Assert.That(view.DetailLevel).IsEqualTo(ViewDetailLevel.Fine);
    }

    [Test]
    public async Task CreateView_Rollback_ViewNotPersisted()
    {
        int viewCountBefore = new FilteredElementCollector(_doc)
            .OfClass(typeof(View))
            .Cast<View>()
            .Count(v => !v.IsTemplate);

        var floorPlanType = new FilteredElementCollector(_doc)
            .OfClass(typeof(ViewFamilyType))
            .Cast<ViewFamilyType>()
            .FirstOrDefault(vft => vft.ViewFamily == ViewFamily.FloorPlan);

        using (var tx = new Transaction(_doc, "Rollback View"))
        {
            tx.Start();
            ViewPlan.Create(_doc, floorPlanType.Id, _level.Id);
            tx.RollBack();
        }

        int viewCountAfter = new FilteredElementCollector(_doc)
            .OfClass(typeof(View))
            .Cast<View>()
            .Count(v => !v.IsTemplate);

        await Assert.That(viewCountAfter).IsEqualTo(viewCountBefore);
    }

    [Test]
    public async Task FindViewFamilyType_AllFamilies_TypesExist()
    {
        var viewFamilyTypes = new FilteredElementCollector(_doc)
            .OfClass(typeof(ViewFamilyType))
            .Cast<ViewFamilyType>()
            .ToList();

        // A new project should have at least FloorPlan, CeilingPlan, Section, 3D
        var families = viewFamilyTypes.Select(vft => vft.ViewFamily).Distinct().ToList();
        await Assert.That(families).Contains(ViewFamily.FloorPlan);
        await Assert.That(families).Contains(ViewFamily.CeilingPlan);
        await Assert.That(families).Contains(ViewFamily.Section);
        await Assert.That(families).Contains(ViewFamily.ThreeDimensional);
    }
}
