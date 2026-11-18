using Autodesk.Revit.DB;
using Nice3point.TUnit.Revit;
using Nice3point.TUnit.Revit.Executors;
using TUnit.Core;
using TUnit.Core.Executors;

namespace RevitMCPCommandSet.Tests;

public class CopyElementsTests : RevitApiTest
{
    private static Document _doc;
    private static Level _level;

    [Before(HookType.Class)]
    [HookExecutor<RevitThreadExecutor>]
    public static void Setup()
    {
        _doc = Application.NewProjectDocument(UnitSystem.Imperial);

        using var tx = new Transaction(_doc, "Setup Copy Tests");
        tx.Start();

        _level = Level.Create(_doc, 0.0);
        _level.Name = "Copy Test Level";

        tx.Commit();
    }

    [After(HookType.Class)]
    [HookExecutor<RevitThreadExecutor>]
    public static void Cleanup()
    {
        _doc?.Close(false);
    }

    [Test]
    public async Task CopyElement_SingleWall_NewElementCreated()
    {
        Wall wall;
        using (var tx = new Transaction(_doc, "Create Wall"))
        {
            tx.Start();
            wall = Wall.Create(_doc, Line.CreateBound(new XYZ(0, 0, 0), new XYZ(10, 0, 0)), _level.Id, false);
            tx.Commit();
        }

        ICollection<ElementId> copiedIds;
        using (var tx = new Transaction(_doc, "Copy Wall"))
        {
            tx.Start();
            copiedIds = ElementTransformUtils.CopyElement(_doc, wall.Id, new XYZ(0, 10, 0));
            tx.Commit();
        }

        await Assert.That(copiedIds.Count).IsGreaterThan(0);

        var copiedElement = _doc.GetElement(copiedIds.First());
        await Assert.That(copiedElement).IsNotNull();
        await Assert.That(copiedElement.Category.BuiltInCategory).IsEqualTo(BuiltInCategory.OST_Walls);
    }

    [Test]
    public async Task CopyElements_MultipleWalls_AllCopied()
    {
        List<Wall> walls = new();
        using (var tx = new Transaction(_doc, "Create Walls"))
        {
            tx.Start();
            walls.Add(Wall.Create(_doc, Line.CreateBound(new XYZ(30, 0, 0), new XYZ(40, 0, 0)), _level.Id, false));
            walls.Add(Wall.Create(_doc, Line.CreateBound(new XYZ(30, 0, 0), new XYZ(30, 10, 0)), _level.Id, false));
            tx.Commit();
        }

        var ids = walls.Select(w => w.Id).ToList();
        ICollection<ElementId> copiedIds;

        using (var tx = new Transaction(_doc, "Copy Multiple"))
        {
            tx.Start();
            copiedIds = ElementTransformUtils.CopyElements(_doc, ids, new XYZ(20, 0, 0));
            tx.Commit();
        }

        await Assert.That(copiedIds.Count).IsEqualTo(2);
    }

    [Test]
    public async Task CopyElement_WithOffset_LocationShifted()
    {
        Wall wall;
        using (var tx = new Transaction(_doc, "Create Wall For Offset"))
        {
            tx.Start();
            wall = Wall.Create(_doc, Line.CreateBound(new XYZ(60, 0, 0), new XYZ(70, 0, 0)), _level.Id, false);
            tx.Commit();
        }

        var originalStart = ((LocationCurve)wall.Location).Curve.GetEndPoint(0);

        XYZ offset = new XYZ(0, 15, 0);
        ICollection<ElementId> copiedIds;

        using (var tx = new Transaction(_doc, "Copy With Offset"))
        {
            tx.Start();
            copiedIds = ElementTransformUtils.CopyElement(_doc, wall.Id, offset);
            tx.Commit();
        }

        var copiedWall = _doc.GetElement(copiedIds.First()) as Wall;
        var copiedStart = ((LocationCurve)copiedWall.Location).Curve.GetEndPoint(0);

        await Assert.That(copiedStart.Y).IsEqualTo(originalStart.Y + offset.Y).Within(0.001);
    }

    [Test]
    public async Task CopyElements_BetweenViews_ElementsCopied()
    {
        // Create two floor plan views
        var floorPlanType = new FilteredElementCollector(_doc)
            .OfClass(typeof(ViewFamilyType))
            .Cast<ViewFamilyType>()
            .FirstOrDefault(vft => vft.ViewFamily == ViewFamily.FloorPlan);

        Level level2;
        ViewPlan sourceView, targetView;

        using (var tx = new Transaction(_doc, "Create Views"))
        {
            tx.Start();

            sourceView = ViewPlan.Create(_doc, floorPlanType.Id, _level.Id);
            sourceView.Name = "Source View";

            level2 = Level.Create(_doc, 10.0);
            level2.Name = "Copy Target Level";

            targetView = ViewPlan.Create(_doc, floorPlanType.Id, level2.Id);
            targetView.Name = "Target View";

            tx.Commit();
        }

        await Assert.That(sourceView).IsNotNull();
        await Assert.That(targetView).IsNotNull();

        // Create a wall visible in source view
        Wall wall;
        using (var tx = new Transaction(_doc, "Create Wall In Source"))
        {
            tx.Start();
            wall = Wall.Create(_doc, Line.CreateBound(new XYZ(80, 0, 0), new XYZ(90, 0, 0)), _level.Id, false);
            tx.Commit();
        }

        // Copy elements between views
        ICollection<ElementId> copiedIds;
        using (var tx = new Transaction(_doc, "Copy Between Views"))
        {
            tx.Start();
            copiedIds = ElementTransformUtils.CopyElements(
                sourceView,
                new List<ElementId> { wall.Id },
                targetView,
                Transform.Identity,
                new CopyPasteOptions());
            tx.Commit();
        }

        await Assert.That(copiedIds.Count).IsGreaterThan(0);
    }

    [Test]
    public async Task CopyElement_OffsetConversion_MmToFeet()
    {
        double offsetXMm = 5000;
        double offsetYMm = 3000;
        double offsetZMm = 0;

        var offset = new XYZ(offsetXMm / 304.8, offsetYMm / 304.8, offsetZMm / 304.8);

        await Assert.That(offset.X * 304.8).IsEqualTo(offsetXMm).Within(0.001);
        await Assert.That(offset.Y * 304.8).IsEqualTo(offsetYMm).Within(0.001);
    }

    [Test]
    public async Task CopyElement_Rollback_NoCopy()
    {
        Wall wall;
        using (var tx = new Transaction(_doc, "Create Wall For Rollback"))
        {
            tx.Start();
            wall = Wall.Create(_doc, Line.CreateBound(new XYZ(100, 0, 0), new XYZ(110, 0, 0)), _level.Id, false);
            tx.Commit();
        }

        int countBefore = new FilteredElementCollector(_doc)
            .OfClass(typeof(Wall))
            .GetElementCount();

        using (var tx = new Transaction(_doc, "Rollback Copy"))
        {
            tx.Start();
            ElementTransformUtils.CopyElement(_doc, wall.Id, new XYZ(0, 20, 0));
            tx.RollBack();
        }

        int countAfter = new FilteredElementCollector(_doc)
            .OfClass(typeof(Wall))
            .GetElementCount();

        await Assert.That(countAfter).IsEqualTo(countBefore);
    }

    [Test]
    public async Task CopiedElement_HasSameType_AsOriginal()
    {
        Wall wall;
        using (var tx = new Transaction(_doc, "Create Typed Wall"))
        {
            tx.Start();
            wall = Wall.Create(_doc, Line.CreateBound(new XYZ(120, 0, 0), new XYZ(130, 0, 0)), _level.Id, false);
            tx.Commit();
        }

        ICollection<ElementId> copiedIds;
        using (var tx = new Transaction(_doc, "Copy Typed Wall"))
        {
            tx.Start();
            copiedIds = ElementTransformUtils.CopyElement(_doc, wall.Id, new XYZ(0, 10, 0));
            tx.Commit();
        }

        var copiedWall = _doc.GetElement(copiedIds.First()) as Wall;
        await Assert.That(copiedWall.WallType.Id.Value).IsEqualTo(wall.WallType.Id.Value);
    }
}
