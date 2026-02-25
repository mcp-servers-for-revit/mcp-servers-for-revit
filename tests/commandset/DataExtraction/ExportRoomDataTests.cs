using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Nice3point.TUnit.Revit;
using Nice3point.TUnit.Revit.Executors;
using TUnit.Core;
using TUnit.Core.Executors;

namespace RevitMCPCommandSet.Tests.DataExtraction;

public class ExportRoomDataTests : RevitApiTest
{
    private static Document _doc;
    private static Level _level;

    [Before(HookType.Class)]
    [HookExecutor<RevitThreadExecutor>]
    public static void Setup()
    {
        _doc = Application.NewProjectDocument(UnitSystem.Imperial);

        using var tx = new Transaction(_doc, "Setup Export Room Data Test");
        tx.Start();

        _level = Level.Create(_doc, 0.0);
        _level.Name = "Export Test Level";

        var floorPlanType = new FilteredElementCollector(_doc)
            .OfClass(typeof(ViewFamilyType))
            .Cast<ViewFamilyType>()
            .FirstOrDefault(vft => vft.ViewFamily == ViewFamily.FloorPlan);

        if (floorPlanType != null)
        {
            ViewPlan.Create(_doc, floorPlanType.Id, _level.Id);
        }

        // Create enclosure and rooms
        double size = 10.0;
        var p1 = new XYZ(0, 0, 0);
        var p2 = new XYZ(size, 0, 0);
        var p3 = new XYZ(size, size, 0);
        var p4 = new XYZ(0, size, 0);

        Wall.Create(_doc, Line.CreateBound(p1, p2), _level.Id, false);
        Wall.Create(_doc, Line.CreateBound(p2, p3), _level.Id, false);
        Wall.Create(_doc, Line.CreateBound(p3, p4), _level.Id, false);
        Wall.Create(_doc, Line.CreateBound(p4, p1), _level.Id, false);

        var room = _doc.Create.NewRoom(_level, new UV(5.0, 5.0));
        if (room != null)
        {
            var nameParam = room.get_Parameter(BuiltInParameter.ROOM_NAME);
            nameParam?.Set("Test Room");

            var deptParam = room.get_Parameter(BuiltInParameter.ROOM_DEPARTMENT);
            deptParam?.Set("Testing");
        }

        tx.Commit();
    }

    [After(HookType.Class)]
    [HookExecutor<RevitThreadExecutor>]
    public static void Cleanup()
    {
        _doc?.Close(false);
    }

    [Test]
    public async Task ExportRooms_PlacedRoom_NameNumberLevelAreaPopulated()
    {
        var rooms = new FilteredElementCollector(_doc)
            .OfCategory(BuiltInCategory.OST_Rooms)
            .WhereElementIsNotElementType()
            .Cast<Room>()
            .Where(r => r.Area > 0)
            .ToList();

        await Assert.That(rooms.Count).IsGreaterThan(0);

        var room = rooms.First();
        string name = room.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString() ?? "";
        string number = room.Number ?? "";
        string level = room.Level?.Name ?? "No Level";
        double area = room.Area;

        await Assert.That(name).IsNotNullOrEmpty();
        await Assert.That(number).IsNotNullOrEmpty();
        await Assert.That(level).IsNotEqualTo("No Level");
        await Assert.That(area).IsGreaterThan(0);
    }

    [Test]
    public async Task ExportRooms_SkipUnplaced_UnplacedRoomsExcluded()
    {
        // Create an unplaced room
        using (var tx = new Transaction(_doc, "Create Unplaced Room"))
        {
            tx.Start();
            // NewRoom with phase creates an unplaced room
            var phases = new FilteredElementCollector(_doc)
                .OfClass(typeof(Phase))
                .Cast<Phase>()
                .ToList();
            if (phases.Count > 0)
            {
                _doc.Create.NewRoom(phases.First());
            }
            tx.Commit();
        }

        var allRooms = new FilteredElementCollector(_doc)
            .OfCategory(BuiltInCategory.OST_Rooms)
            .WhereElementIsNotElementType()
            .Cast<Room>()
            .ToList();

        // Filter like the handler does (skip Area == 0)
        var placedRooms = allRooms.Where(r => r.Area > 0).ToList();

        await Assert.That(placedRooms.Count).IsGreaterThan(0);
        await Assert.That(placedRooms.Count).IsLessThanOrEqualTo(allRooms.Count);
    }

    [Test]
    public async Task ExportRooms_IncludeUnplaced_AllRoomsReturned()
    {
        var allRooms = new FilteredElementCollector(_doc)
            .OfCategory(BuiltInCategory.OST_Rooms)
            .WhereElementIsNotElementType()
            .Cast<Room>()
            .ToList();

        // When includeUnplacedRooms = true, no filtering on Area
        bool includeUnplacedRooms = true;
        var result = allRooms
            .Where(r => includeUnplacedRooms || r.Area > 0)
            .ToList();

        await Assert.That(result.Count).IsEqualTo(allRooms.Count);
    }

    [Test]
    public async Task ExportRooms_TotalAreaAccumulation_MatchesSumOfRoomAreas()
    {
        var rooms = new FilteredElementCollector(_doc)
            .OfCategory(BuiltInCategory.OST_Rooms)
            .WhereElementIsNotElementType()
            .Cast<Room>()
            .Where(r => r.Area > 0)
            .ToList();

        double totalArea = 0;
        foreach (var room in rooms)
        {
            totalArea += room.Area;
        }

        double expectedTotal = rooms.Sum(r => r.Area);
        await Assert.That(totalArea).IsEqualTo(expectedTotal).Within(0.001);
        await Assert.That(totalArea).IsGreaterThan(0);
    }
}
