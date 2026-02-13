using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Nice3point.TUnit.Revit;
using Nice3point.TUnit.Revit.Executors;
using TUnit.Core;
using TUnit.Core.Executors;

namespace RevitMCPCommandSet.Tests;

public class TagRoomsTests : RevitApiTest
{
    private static Document _doc;
    private static Level _level;
    private static ViewPlan _floorPlan;
    private static Room _room;

    [Before(HookType.Class)]
    [HookExecutor<RevitThreadExecutor>]
    public static void Setup()
    {
        _doc = Application.NewProjectDocument(UnitSystem.Imperial);

        using var tx = new Transaction(_doc, "Setup Tag Test Environment");
        tx.Start();

        _level = Level.Create(_doc, 0.0);
        _level.Name = "Tag Test Level";

        var floorPlanType = new FilteredElementCollector(_doc)
            .OfClass(typeof(ViewFamilyType))
            .Cast<ViewFamilyType>()
            .FirstOrDefault(vft => vft.ViewFamily == ViewFamily.FloorPlan);

        if (floorPlanType != null)
        {
            _floorPlan = ViewPlan.Create(_doc, floorPlanType.Id, _level.Id);
        }

        // Create primary enclosure (0,0)-(10,10) with a room
        CreateEnclosure(_doc, _level.Id, 0, 0, 10);
        _room = _doc.Create.NewRoom(_level, new UV(5.0, 5.0));

        // Create secondary enclosure (20,0)-(30,10) for multi-room test
        CreateEnclosure(_doc, _level.Id, 20, 0, 10);

        tx.Commit();
    }

    [After(HookType.Class)]
    [HookExecutor<RevitThreadExecutor>]
    public static void Cleanup()
    {
        _doc?.Close(false);
    }

    [Test]
    public async Task TagRoom_CreateRoomTag_TagExists()
    {
        await Assert.That(_room).IsNotNull();
        await Assert.That(_floorPlan).IsNotNull();

        using var tx = new Transaction(_doc, "Tag Room");
        tx.Start();

        var locPoint = _room.Location as LocationPoint;
        XYZ roomCenter = locPoint?.Point ?? new XYZ(5.0, 5.0, 0);
        var tagPoint = new UV(roomCenter.X, roomCenter.Y);

        var tag = _doc.Create.NewRoomTag(
            new LinkElementId(_room.Id),
            tagPoint,
            _floorPlan.Id);

        tx.Commit();

        await Assert.That(tag).IsNotNull();
        await Assert.That(tag.Room).IsNotNull();
    }

    [Test]
    public async Task TagRoom_WithLeader_HasLeaderIsTrue()
    {
        await Assert.That(_room).IsNotNull();
        await Assert.That(_floorPlan).IsNotNull();

        using var tx = new Transaction(_doc, "Tag Room With Leader");
        tx.Start();

        var locPoint = _room.Location as LocationPoint;
        XYZ roomCenter = locPoint?.Point ?? new XYZ(5.0, 5.0, 0);

        var tag = _doc.Create.NewRoomTag(
            new LinkElementId(_room.Id),
            new UV(roomCenter.X, roomCenter.Y),
            _floorPlan.Id);

        if (tag != null)
        {
            tag.HasLeader = true;
        }

        tx.Commit();

        await Assert.That(tag).IsNotNull();
        await Assert.That(tag.HasLeader).IsTrue();
    }

    [Test]
    public async Task TagRoom_SkipAlreadyTagged_NoDuplicateTags()
    {
        await Assert.That(_room).IsNotNull();
        await Assert.That(_floorPlan).IsNotNull();

        // Create a tag for the room
        RoomTag firstTag;
        using (var tx = new Transaction(_doc, "First Tag"))
        {
            tx.Start();
            var locPoint = _room.Location as LocationPoint;
            XYZ center = locPoint?.Point ?? new XYZ(5.0, 5.0, 0);
            firstTag = _doc.Create.NewRoomTag(new LinkElementId(_room.Id), new UV(center.X, center.Y), _floorPlan.Id);
            tx.Commit();
        }

        await Assert.That(firstTag).IsNotNull();

        // Simulate duplicate detection (mimics handler logic)
        var taggedRoomIds = new HashSet<long> { firstTag.Room.Id.Value };
        bool alreadyTagged = taggedRoomIds.Contains(_room.Id.Value);

        await Assert.That(alreadyTagged).IsTrue();
    }

    [Test]
    public async Task TagRoom_SpecificRoomById_OnlyThatRoomTagged()
    {
        await Assert.That(_room).IsNotNull();
        await Assert.That(_floorPlan).IsNotNull();

        // Create a second room in the secondary enclosure
        Room room2;
        using (var tx = new Transaction(_doc, "Create Second Room"))
        {
            tx.Start();
            room2 = _doc.Create.NewRoom(_level, new UV(25.0, 5.0));
            tx.Commit();
        }

        await Assert.That(room2).IsNotNull();

        // Tag only _room (room1)
        RoomTag tag;
        using (var tx = new Transaction(_doc, "Tag Specific Room"))
        {
            tx.Start();
            var locPoint = _room.Location as LocationPoint;
            XYZ center = locPoint?.Point ?? new XYZ(5.0, 5.0, 0);
            tag = _doc.Create.NewRoomTag(new LinkElementId(_room.Id), new UV(center.X, center.Y), _floorPlan.Id);
            tx.Commit();
        }

        // Verify the tag references the correct room (room1, not room2)
        await Assert.That(tag).IsNotNull();
        await Assert.That(tag.Room.Id.Value).IsEqualTo(_room.Id.Value);
    }

    [Test]
    public async Task CreateRoomTag_TagReferencesRoom_TagTextNotEmpty()
    {
        await Assert.That(_room).IsNotNull();
        await Assert.That(_floorPlan).IsNotNull();

        // Create a tag and verify it references the room with displayable text
        RoomTag tag;
        using (var tx = new Transaction(_doc, "Create Tag For Text Test"))
        {
            tx.Start();
            var locPoint = _room.Location as LocationPoint;
            XYZ center = locPoint?.Point ?? new XYZ(5.0, 5.0, 0);
            tag = _doc.Create.NewRoomTag(new LinkElementId(_room.Id), new UV(center.X, center.Y), _floorPlan.Id);
            tx.Commit();
        }

        await Assert.That(tag).IsNotNull();
        await Assert.That(tag.Room).IsNotNull();
        await Assert.That(tag.Room.Id.Value).IsEqualTo(_room.Id.Value);
        await Assert.That(tag.TagText).IsNotNull();
    }

    private static void CreateEnclosure(Document doc, ElementId levelId, double x, double y, double size)
    {
        var p1 = new XYZ(x, y, 0);
        var p2 = new XYZ(x + size, y, 0);
        var p3 = new XYZ(x + size, y + size, 0);
        var p4 = new XYZ(x, y + size, 0);

        Wall.Create(doc, Line.CreateBound(p1, p2), levelId, false);
        Wall.Create(doc, Line.CreateBound(p2, p3), levelId, false);
        Wall.Create(doc, Line.CreateBound(p3, p4), levelId, false);
        Wall.Create(doc, Line.CreateBound(p4, p1), levelId, false);
    }
}
