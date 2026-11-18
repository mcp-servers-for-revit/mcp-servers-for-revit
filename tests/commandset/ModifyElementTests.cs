using Autodesk.Revit.DB;
using Nice3point.TUnit.Revit;
using Nice3point.TUnit.Revit.Executors;
using TUnit.Core;
using TUnit.Core.Executors;

namespace RevitMCPCommandSet.Tests;

public class ModifyElementTests : RevitApiTest
{
    private static Document _doc;
    private static Level _level;

    [Before(HookType.Class)]
    [HookExecutor<RevitThreadExecutor>]
    public static void Setup()
    {
        _doc = Application.NewProjectDocument(UnitSystem.Imperial);

        using var tx = new Transaction(_doc, "Setup Modify Tests");
        tx.Start();

        _level = Level.Create(_doc, 0.0);
        _level.Name = "Modify Test Level";

        tx.Commit();
    }

    [After(HookType.Class)]
    [HookExecutor<RevitThreadExecutor>]
    public static void Cleanup()
    {
        _doc?.Close(false);
    }

    [Test]
    public async Task MoveElement_ByTranslation_LocationChanged()
    {
        Wall wall;
        using (var tx = new Transaction(_doc, "Create Wall For Move"))
        {
            tx.Start();
            wall = Wall.Create(_doc, Line.CreateBound(new XYZ(0, 0, 0), new XYZ(10, 0, 0)), _level.Id, false);
            tx.Commit();
        }

        await Assert.That(wall).IsNotNull();

        var locationBefore = (wall.Location as LocationCurve)?.Curve.GetEndPoint(0);

        using (var tx = new Transaction(_doc, "Move Wall"))
        {
            tx.Start();
            XYZ translation = new XYZ(5, 5, 0);
            ElementTransformUtils.MoveElement(_doc, wall.Id, translation);
            tx.Commit();
        }

        var locationAfter = (wall.Location as LocationCurve)?.Curve.GetEndPoint(0);

        await Assert.That(locationAfter).IsNotNull();
        await Assert.That(locationAfter.X).IsEqualTo(locationBefore.X + 5).Within(0.001);
        await Assert.That(locationAfter.Y).IsEqualTo(locationBefore.Y + 5).Within(0.001);
    }

    [Test]
    public async Task MoveElements_Multiple_AllMoved()
    {
        List<Wall> walls = new();
        using (var tx = new Transaction(_doc, "Create Walls For Batch Move"))
        {
            tx.Start();
            walls.Add(Wall.Create(_doc, Line.CreateBound(new XYZ(0, 20, 0), new XYZ(10, 20, 0)), _level.Id, false));
            walls.Add(Wall.Create(_doc, Line.CreateBound(new XYZ(0, 25, 0), new XYZ(10, 25, 0)), _level.Id, false));
            tx.Commit();
        }

        var idList = walls.Select(w => w.Id).ToList();

        using (var tx = new Transaction(_doc, "Move Walls"))
        {
            tx.Start();
            ElementTransformUtils.MoveElements(_doc, idList, new XYZ(0, 10, 0));
            tx.Commit();
        }

        foreach (var wall in walls)
        {
            var loc = (wall.Location as LocationCurve)?.Curve.GetEndPoint(0);
            await Assert.That(loc).IsNotNull();
            // Y should be shifted by 10
            await Assert.That(loc.Y).IsGreaterThanOrEqualTo(30 - 0.01);
        }
    }

    [Test]
    public async Task RotateElement_ByAngle_OrientationChanged()
    {
        Wall wall;
        using (var tx = new Transaction(_doc, "Create Wall For Rotate"))
        {
            tx.Start();
            wall = Wall.Create(_doc, Line.CreateBound(new XYZ(0, 40, 0), new XYZ(10, 40, 0)), _level.Id, false);
            tx.Commit();
        }

        XYZ center = new XYZ(5, 40, 0);
        double angleRadians = Math.PI / 2; // 90 degrees

        using (var tx = new Transaction(_doc, "Rotate Wall"))
        {
            tx.Start();
            Line axis = Line.CreateBound(center, center + XYZ.BasisZ);
            ElementTransformUtils.RotateElement(_doc, wall.Id, axis, angleRadians);
            tx.Commit();
        }

        var curve = (wall.Location as LocationCurve)?.Curve;
        await Assert.That(curve).IsNotNull();

        // After 90-degree rotation around (5,40), a horizontal wall becomes vertical
        var start = curve.GetEndPoint(0);
        var end = curve.GetEndPoint(1);
        double dx = Math.Abs(end.X - start.X);
        double dy = Math.Abs(end.Y - start.Y);

        // Wall should now be mostly vertical (dy >> dx)
        await Assert.That(dy).IsGreaterThan(dx);
    }

    [Test]
    public async Task CopyElement_ByOffset_NewElementCreated()
    {
        Wall wall;
        using (var tx = new Transaction(_doc, "Create Wall For Copy"))
        {
            tx.Start();
            wall = Wall.Create(_doc, Line.CreateBound(new XYZ(0, 60, 0), new XYZ(10, 60, 0)), _level.Id, false);
            tx.Commit();
        }

        int wallCountBefore = new FilteredElementCollector(_doc)
            .OfClass(typeof(Wall))
            .GetElementCount();

        ICollection<ElementId> copiedIds;
        using (var tx = new Transaction(_doc, "Copy Wall"))
        {
            tx.Start();
            copiedIds = ElementTransformUtils.CopyElement(_doc, wall.Id, new XYZ(0, 10, 0));
            tx.Commit();
        }

        int wallCountAfter = new FilteredElementCollector(_doc)
            .OfClass(typeof(Wall))
            .GetElementCount();

        await Assert.That(copiedIds.Count).IsGreaterThan(0);
        await Assert.That(wallCountAfter).IsGreaterThan(wallCountBefore);
    }

    [Test]
    public async Task MirrorElement_AcrossPlane_ElementMirrored()
    {
        Wall wall;
        using (var tx = new Transaction(_doc, "Create Wall For Mirror"))
        {
            tx.Start();
            wall = Wall.Create(_doc, Line.CreateBound(new XYZ(2, 80, 0), new XYZ(8, 80, 0)), _level.Id, false);
            tx.Commit();
        }

        int wallCountBefore = new FilteredElementCollector(_doc)
            .OfClass(typeof(Wall))
            .GetElementCount();

        // Mirror across the Y-axis plane at x=0
        Plane mirrorPlane = Plane.CreateByNormalAndOrigin(XYZ.BasisX, XYZ.Zero);

        using (var tx = new Transaction(_doc, "Mirror Wall"))
        {
            tx.Start();
            // false = don't delete original
            ElementTransformUtils.MirrorElement(_doc, wall.Id, mirrorPlane);
            tx.Commit();
        }

        int wallCountAfter = new FilteredElementCollector(_doc)
            .OfClass(typeof(Wall))
            .GetElementCount();

        // Mirror with delete=false creates a new element
        await Assert.That(wallCountAfter).IsGreaterThan(wallCountBefore);
    }

    [Test]
    public async Task DeleteElement_SingleWall_ElementRemoved()
    {
        Wall wall;
        using (var tx = new Transaction(_doc, "Create Wall For Delete"))
        {
            tx.Start();
            wall = Wall.Create(_doc, Line.CreateBound(new XYZ(0, 100, 0), new XYZ(10, 100, 0)), _level.Id, false);
            tx.Commit();
        }

        ElementId wallId = wall.Id;

        using (var tx = new Transaction(_doc, "Delete Wall"))
        {
            tx.Start();
            _doc.Delete(wallId);
            tx.Commit();
        }

        var deleted = _doc.GetElement(wallId);
        await Assert.That(deleted).IsNull();
    }

    [Test]
    public async Task ModifyElement_RollbackMove_LocationUnchanged()
    {
        Wall wall;
        using (var tx = new Transaction(_doc, "Create Wall For Rollback"))
        {
            tx.Start();
            wall = Wall.Create(_doc, Line.CreateBound(new XYZ(0, 120, 0), new XYZ(10, 120, 0)), _level.Id, false);
            tx.Commit();
        }

        var locationBefore = (wall.Location as LocationCurve)?.Curve.GetEndPoint(0);

        using (var tx = new Transaction(_doc, "Rollback Move"))
        {
            tx.Start();
            ElementTransformUtils.MoveElement(_doc, wall.Id, new XYZ(50, 50, 0));
            tx.RollBack();
        }

        var locationAfter = (wall.Location as LocationCurve)?.Curve.GetEndPoint(0);

        await Assert.That(locationAfter.X).IsEqualTo(locationBefore.X).Within(0.001);
        await Assert.That(locationAfter.Y).IsEqualTo(locationBefore.Y).Within(0.001);
    }
}
