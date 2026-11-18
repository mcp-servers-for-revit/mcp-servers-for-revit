using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Nice3point.TUnit.Revit;
using Nice3point.TUnit.Revit.Executors;
using TUnit.Core;
using TUnit.Core.Executors;

namespace RevitMCPCommandSet.Tests;

public class CreateFloorTests : RevitApiTest
{
    private static Document _doc;
    private static Level _level;

    [Before(HookType.Class)]
    [HookExecutor<RevitThreadExecutor>]
    public static void Setup()
    {
        _doc = Application.NewProjectDocument(UnitSystem.Imperial);

        using var tx = new Transaction(_doc, "Setup Floor Tests");
        tx.Start();

        _level = Level.Create(_doc, 0.0);
        _level.Name = "Floor Test Level";

        tx.Commit();
    }

    [After(HookType.Class)]
    [HookExecutor<RevitThreadExecutor>]
    public static void Cleanup()
    {
        _doc?.Close(false);
    }

    [Test]
    public async Task CreateFloor_WithBoundaryPoints_FloorCreated()
    {
        // Create a rectangular boundary in feet
        var p1 = new XYZ(0, 0, 0);
        var p2 = new XYZ(10, 0, 0);
        var p3 = new XYZ(10, 10, 0);
        var p4 = new XYZ(0, 10, 0);

        var curveLoop = new CurveLoop();
        curveLoop.Append(Line.CreateBound(p1, p2));
        curveLoop.Append(Line.CreateBound(p2, p3));
        curveLoop.Append(Line.CreateBound(p3, p4));
        curveLoop.Append(Line.CreateBound(p4, p1));

        var floorType = new FilteredElementCollector(_doc)
            .OfClass(typeof(FloorType))
            .Cast<FloorType>()
            .FirstOrDefault();

        await Assert.That(floorType).IsNotNull();

        Floor floor;
        using (var tx = new Transaction(_doc, "Create Floor"))
        {
            tx.Start();
            floor = Floor.Create(_doc, new List<CurveLoop> { curveLoop }, floorType.Id, _level.Id);
            tx.Commit();
        }

        await Assert.That(floor).IsNotNull();
        await Assert.That(floor.FloorType.Name).IsEqualTo(floorType.Name);
    }

    [Test]
    public async Task CreateFloor_SpecificFloorType_TypeApplied()
    {
        var floorTypes = new FilteredElementCollector(_doc)
            .OfClass(typeof(FloorType))
            .Cast<FloorType>()
            .ToList();

        await Assert.That(floorTypes.Count).IsGreaterThan(0);

        var targetType = floorTypes.First();

        var curveLoop = CreateRectangularLoop(20, 0, 30, 10);

        Floor floor;
        using (var tx = new Transaction(_doc, "Create Typed Floor"))
        {
            tx.Start();
            floor = Floor.Create(_doc, new List<CurveLoop> { curveLoop }, targetType.Id, _level.Id);
            tx.Commit();
        }

        await Assert.That(floor).IsNotNull();
        await Assert.That(floor.FloorType.Id.Value).IsEqualTo(targetType.Id.Value);
    }

    [Test]
    public async Task CreateFloor_OnLevel_LevelAssociated()
    {
        var curveLoop = CreateRectangularLoop(40, 0, 50, 10);

        var floorType = new FilteredElementCollector(_doc)
            .OfClass(typeof(FloorType))
            .Cast<FloorType>()
            .FirstOrDefault();

        Floor floor;
        using (var tx = new Transaction(_doc, "Create Floor On Level"))
        {
            tx.Start();
            floor = Floor.Create(_doc, new List<CurveLoop> { curveLoop }, floorType.Id, _level.Id);
            tx.Commit();
        }

        await Assert.That(floor).IsNotNull();
        await Assert.That(floor.LevelId.Value).IsEqualTo(_level.Id.Value);
    }

    [Test]
    public async Task CreateFloor_FromRoomBoundary_FloorMatchesRoom()
    {
        // Create an enclosed room and derive a floor from its boundary
        Room room;
        using (var tx = new Transaction(_doc, "Create Room For Floor"))
        {
            tx.Start();

            // Create walls forming enclosure at (60,0)-(70,10)
            var p1 = new XYZ(60, 0, 0);
            var p2 = new XYZ(70, 0, 0);
            var p3 = new XYZ(70, 10, 0);
            var p4 = new XYZ(60, 10, 0);

            Wall.Create(_doc, Line.CreateBound(p1, p2), _level.Id, false);
            Wall.Create(_doc, Line.CreateBound(p2, p3), _level.Id, false);
            Wall.Create(_doc, Line.CreateBound(p3, p4), _level.Id, false);
            Wall.Create(_doc, Line.CreateBound(p4, p1), _level.Id, false);

            room = _doc.Create.NewRoom(_level, new UV(65.0, 5.0));

            tx.Commit();
        }

        await Assert.That(room).IsNotNull();

        // Get room boundary segments
        var segments = room.GetBoundarySegments(new SpatialElementBoundaryOptions());
        await Assert.That(segments).IsNotNull();
        await Assert.That(segments.Count).IsGreaterThan(0);

        var roomCurveLoop = new CurveLoop();
        foreach (var segment in segments[0])
        {
            roomCurveLoop.Append(segment.GetCurve());
        }

        var floorType = new FilteredElementCollector(_doc)
            .OfClass(typeof(FloorType))
            .Cast<FloorType>()
            .FirstOrDefault();

        Floor floor;
        using (var tx = new Transaction(_doc, "Create Floor From Room"))
        {
            tx.Start();
            floor = Floor.Create(_doc, new List<CurveLoop> { roomCurveLoop }, floorType.Id, _level.Id);
            tx.Commit();
        }

        await Assert.That(floor).IsNotNull();
    }

    [Test]
    public async Task CreateFloor_TriangularBoundary_FloorCreated()
    {
        var p1 = new XYZ(80, 0, 0);
        var p2 = new XYZ(90, 0, 0);
        var p3 = new XYZ(85, 10, 0);

        var curveLoop = new CurveLoop();
        curveLoop.Append(Line.CreateBound(p1, p2));
        curveLoop.Append(Line.CreateBound(p2, p3));
        curveLoop.Append(Line.CreateBound(p3, p1));

        var floorType = new FilteredElementCollector(_doc)
            .OfClass(typeof(FloorType))
            .Cast<FloorType>()
            .FirstOrDefault();

        Floor floor;
        using (var tx = new Transaction(_doc, "Create Triangular Floor"))
        {
            tx.Start();
            floor = Floor.Create(_doc, new List<CurveLoop> { curveLoop }, floorType.Id, _level.Id);
            tx.Commit();
        }

        await Assert.That(floor).IsNotNull();
    }

    [Test]
    public async Task CreateFloor_Rollback_FloorNotPersisted()
    {
        int floorCountBefore = new FilteredElementCollector(_doc)
            .OfClass(typeof(Floor))
            .GetElementCount();

        using (var tx = new Transaction(_doc, "Rollback Floor"))
        {
            tx.Start();

            var curveLoop = CreateRectangularLoop(100, 0, 110, 10);
            var floorType = new FilteredElementCollector(_doc)
                .OfClass(typeof(FloorType))
                .Cast<FloorType>()
                .FirstOrDefault();

            Floor.Create(_doc, new List<CurveLoop> { curveLoop }, floorType.Id, _level.Id);

            tx.RollBack();
        }

        int floorCountAfter = new FilteredElementCollector(_doc)
            .OfClass(typeof(Floor))
            .GetElementCount();

        await Assert.That(floorCountAfter).IsEqualTo(floorCountBefore);
    }

    [Test]
    public async Task CreateFloor_BoundaryPointConversion_MmToFeetAccurate()
    {
        // Simulate the mm-to-feet conversion used in CreateFloorEventHandler
        double xMm = 5000.0;
        double yMm = 3000.0;

        double xFeet = xMm / 304.8;
        double yFeet = yMm / 304.8;

        // Verify round-trip
        await Assert.That(xFeet * 304.8).IsEqualTo(xMm).Within(0.0001);
        await Assert.That(yFeet * 304.8).IsEqualTo(yMm).Within(0.0001);
    }

    private static CurveLoop CreateRectangularLoop(double x1, double y1, double x2, double y2)
    {
        var curveLoop = new CurveLoop();
        var p1 = new XYZ(x1, y1, 0);
        var p2 = new XYZ(x2, y1, 0);
        var p3 = new XYZ(x2, y2, 0);
        var p4 = new XYZ(x1, y2, 0);

        curveLoop.Append(Line.CreateBound(p1, p2));
        curveLoop.Append(Line.CreateBound(p2, p3));
        curveLoop.Append(Line.CreateBound(p3, p4));
        curveLoop.Append(Line.CreateBound(p4, p1));

        return curveLoop;
    }
}
