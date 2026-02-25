using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Nice3point.TUnit.Revit;
using Nice3point.TUnit.Revit.Executors;
using TUnit.Core;
using TUnit.Core.Executors;

namespace RevitMCPCommandSet.Tests.Architecture;

public class CreateRoomTests : RevitApiTest
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

        using var tx = new Transaction(_doc, "Setup Room Test Environment");
        tx.Start();

        _level = Level.Create(_doc, 0.0);
        _level.Name = "Room Test Level";

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

        // Create secondary enclosure (20,0)-(30,10) for rollback test
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
    public async Task CreateRoom_AtValidLocation_RoomExistsWithArea()
    {
        await Assert.That(_room).IsNotNull();
        await Assert.That(_room.Area).IsGreaterThan(0);
    }

    [Test]
    public async Task CreateRoom_SetName_RoomNameParameterSet()
    {
        using var tx = new Transaction(_doc, "Set Room Name");
        tx.Start();

        var nameParam = _room.get_Parameter(BuiltInParameter.ROOM_NAME);
        if (nameParam != null && !nameParam.IsReadOnly)
        {
            nameParam.Set("Conference Room");
        }

        tx.Commit();

        var readParam = _room.get_Parameter(BuiltInParameter.ROOM_NAME);
        await Assert.That(readParam?.AsString()).IsEqualTo("Conference Room");
    }

    [Test]
    public async Task CreateRoom_SetNumber_RoomNumberParameterSet()
    {
        using var tx = new Transaction(_doc, "Set Room Number");
        SuppressDuplicateNumberWarnings(tx);
        tx.Start();

        var numberParam = _room.get_Parameter(BuiltInParameter.ROOM_NUMBER);
        if (numberParam != null && !numberParam.IsReadOnly)
        {
            numberParam.Set("101");
        }

        tx.Commit();

        var readParam = _room.get_Parameter(BuiltInParameter.ROOM_NUMBER);
        await Assert.That(readParam?.AsString()).IsEqualTo("101");
    }

    [Test]
    public async Task CreateRoom_SetDepartmentAndComments_ParametersSet()
    {
        using var tx = new Transaction(_doc, "Set Room Dept");
        tx.Start();

        var deptParam = _room.get_Parameter(BuiltInParameter.ROOM_DEPARTMENT);
        if (deptParam != null && !deptParam.IsReadOnly)
        {
            deptParam.Set("Engineering");
        }

        var commentsParam = _room.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
        if (commentsParam != null && !commentsParam.IsReadOnly)
        {
            commentsParam.Set("Test comment");
        }

        tx.Commit();

        await Assert.That(_room.get_Parameter(BuiltInParameter.ROOM_DEPARTMENT)?.AsString())
            .IsEqualTo("Engineering");
        await Assert.That(_room.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.AsString())
            .IsEqualTo("Test comment");
    }

    [Test]
    public async Task CreateRoom_DuplicateNumber_UniqueNumberGenerated()
    {
        var existingNumbers = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "200", "201", "202" };
        string uniqueNumber = GetUniqueRoomNumber("200", existingNumbers);

        await Assert.That(uniqueNumber).IsNotEqualTo("200");
        await Assert.That(existingNumbers.Contains(uniqueNumber)).IsFalse();
    }

    [Test]
    public async Task CreateRoom_MultipleRooms_AllGetUniqueNumbers()
    {
        var existingNumbers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var assignedNumbers = new List<string>();

        for (int i = 0; i < 5; i++)
        {
            string number = GetNextAvailableRoomNumber(existingNumbers);
            existingNumbers.Add(number);
            assignedNumbers.Add(number);
        }

        // All numbers should be unique
        await Assert.That(assignedNumbers.Distinct().Count()).IsEqualTo(5);
    }

    [Test]
    public async Task CreateRoom_RollbackOnFailure_RoomNotPersisted()
    {
        int roomCountBefore = new FilteredElementCollector(_doc)
            .OfCategory(BuiltInCategory.OST_Rooms)
            .WhereElementIsNotElementType()
            .GetElementCount();

        using (var tx = new Transaction(_doc, "Create Room Rollback"))
        {
            tx.Start();
            // Use secondary enclosure at (25, 5) so it doesn't conflict with _room
            _doc.Create.NewRoom(_level, new UV(25.0, 5.0));
            tx.RollBack();
        }

        int roomCountAfter = new FilteredElementCollector(_doc)
            .OfCategory(BuiltInCategory.OST_Rooms)
            .WhereElementIsNotElementType()
            .GetElementCount();

        await Assert.That(roomCountAfter).IsEqualTo(roomCountBefore);
    }

    [Test]
    public async Task CreateRoom_UpperLimitAndOffset_ParametersSet()
    {
        using var tx = new Transaction(_doc, "Set Room Offset");
        tx.Start();

        double offsetMm = 3000;
        double offsetFeet = offsetMm / 304.8;

        var limitOffsetParam = _room.get_Parameter(BuiltInParameter.ROOM_UPPER_OFFSET);
        if (limitOffsetParam != null && !limitOffsetParam.IsReadOnly)
        {
            limitOffsetParam.Set(offsetFeet);
        }

        tx.Commit();

        var readParam = _room.get_Parameter(BuiltInParameter.ROOM_UPPER_OFFSET);
        await Assert.That(readParam).IsNotNull();
        await Assert.That(readParam.AsDouble()).IsEqualTo(3000.0 / 304.8).Within(0.001);
    }

    #region Helper Methods

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

    private static void SuppressDuplicateNumberWarnings(Transaction tx)
    {
        var failureOptions = tx.GetFailureHandlingOptions();
        failureOptions.SetClearAfterRollback(true);
        failureOptions.SetDelayedMiniWarnings(false);
        tx.SetFailureHandlingOptions(failureOptions);
    }

    private static string GetUniqueRoomNumber(string baseNumber, HashSet<string> existingNumbers)
    {
        if (string.IsNullOrEmpty(baseNumber))
            baseNumber = "1";

        if (!existingNumbers.Contains(baseNumber))
            return baseNumber;

        int lastDigitEnd = -1;
        int lastDigitStart = -1;
        for (int i = baseNumber.Length - 1; i >= 0; i--)
        {
            if (char.IsDigit(baseNumber[i]))
            {
                if (lastDigitEnd == -1) lastDigitEnd = i;
                lastDigitStart = i;
            }
            else if (lastDigitEnd != -1)
                break;
        }

        if (lastDigitStart != -1)
        {
            string prefix = baseNumber.Substring(0, lastDigitStart);
            string numericPart = baseNumber.Substring(lastDigitStart, lastDigitEnd - lastDigitStart + 1);
            string suffix = baseNumber.Substring(lastDigitEnd + 1);

            if (int.TryParse(numericPart, out int num))
            {
                for (int i = 1; i <= 1000; i++)
                {
                    string candidate = prefix + (num + i).ToString().PadLeft(numericPart.Length, '0') + suffix;
                    if (!existingNumbers.Contains(candidate))
                        return candidate;
                }
            }
        }

        for (char c = 'A'; c <= 'Z'; c++)
        {
            string candidate = baseNumber + c;
            if (!existingNumbers.Contains(candidate))
                return candidate;
        }

        return baseNumber + "-" + Guid.NewGuid().ToString().Substring(0, 4);
    }

    private static string GetNextAvailableRoomNumber(HashSet<string> existingNumbers)
    {
        int maxNumber = 0;
        foreach (string num in existingNumbers)
        {
            if (int.TryParse(num, out int parsed))
            {
                if (parsed > maxNumber) maxNumber = parsed;
            }
        }

        for (int i = maxNumber + 1; i < maxNumber + 10000; i++)
        {
            string candidate = i.ToString();
            if (!existingNumbers.Contains(candidate))
                return candidate;
        }

        return (maxNumber + 1).ToString();
    }

    #endregion
}
