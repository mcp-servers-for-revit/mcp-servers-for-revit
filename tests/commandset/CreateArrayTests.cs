using Autodesk.Revit.DB;
using Nice3point.TUnit.Revit;
using Nice3point.TUnit.Revit.Executors;
using TUnit.Core;
using TUnit.Core.Executors;

namespace RevitMCPCommandSet.Tests;

public class CreateArrayTests : RevitApiTest
{
    private static Document _doc;
    private static Level _level;

    [Before(HookType.Class)]
    [HookExecutor<RevitThreadExecutor>]
    public static void Setup()
    {
        _doc = Application.NewProjectDocument(UnitSystem.Imperial);

        using var tx = new Transaction(_doc, "Setup Array Tests");
        tx.Start();

        _level = Level.Create(_doc, 0.0);
        _level.Name = "Array Test Level";

        tx.Commit();
    }

    [After(HookType.Class)]
    [HookExecutor<RevitThreadExecutor>]
    public static void Cleanup()
    {
        _doc?.Close(false);
    }

    [Test]
    public async Task LinearArray_CopyWall_ElementsCopied()
    {
        Wall wall;
        using (var tx = new Transaction(_doc, "Create Wall For Array"))
        {
            tx.Start();
            wall = Wall.Create(_doc, Line.CreateBound(new XYZ(0, 0, 0), new XYZ(10, 0, 0)), _level.Id, false);
            tx.Commit();
        }

        int wallCountBefore = new FilteredElementCollector(_doc)
            .OfClass(typeof(Wall))
            .GetElementCount();

        int copies = 3;
        var offset = new XYZ(0, 5, 0); // 5 feet spacing in Y

        using (var tx = new Transaction(_doc, "Create Linear Array"))
        {
            tx.Start();

            for (int i = 0; i < copies; i++)
            {
                var translation = offset * (i + 1);
                ElementTransformUtils.CopyElements(_doc, new List<ElementId> { wall.Id }, translation);
            }

            tx.Commit();
        }

        int wallCountAfter = new FilteredElementCollector(_doc)
            .OfClass(typeof(Wall))
            .GetElementCount();

        await Assert.That(wallCountAfter).IsEqualTo(wallCountBefore + copies);
    }

    [Test]
    public async Task LinearArray_MultipleElements_AllCopied()
    {
        List<Wall> walls = new();
        using (var tx = new Transaction(_doc, "Create Walls For Multi Array"))
        {
            tx.Start();
            walls.Add(Wall.Create(_doc, Line.CreateBound(new XYZ(50, 0, 0), new XYZ(60, 0, 0)), _level.Id, false));
            walls.Add(Wall.Create(_doc, Line.CreateBound(new XYZ(50, 0, 0), new XYZ(50, 10, 0)), _level.Id, false));
            tx.Commit();
        }

        var ids = walls.Select(w => w.Id).ToList();
        var offset = new XYZ(20, 0, 0);
        int copies = 2;

        var allCopiedIds = new List<ElementId>();
        using (var tx = new Transaction(_doc, "Array Multiple"))
        {
            tx.Start();
            for (int i = 0; i < copies; i++)
            {
                var translation = offset * (i + 1);
                var copiedIds = ElementTransformUtils.CopyElements(_doc, ids, translation);
                allCopiedIds.AddRange(copiedIds);
            }
            tx.Commit();
        }

        // 2 walls * 2 copies = 4 new elements
        await Assert.That(allCopiedIds.Count).IsEqualTo(4);
    }

    [Test]
    public async Task RadialArray_CopyAndRotate_ElementsAtAngles()
    {
        Wall wall;
        using (var tx = new Transaction(_doc, "Create Wall For Radial"))
        {
            tx.Start();
            wall = Wall.Create(_doc, Line.CreateBound(new XYZ(100, 0, 0), new XYZ(110, 0, 0)), _level.Id, false);
            tx.Commit();
        }

        var center = new XYZ(105, 0, 0);
        var axis = Line.CreateBound(center, center + XYZ.BasisZ);
        int copies = 3;
        double totalAngle = 360.0;
        double angleStep = (totalAngle / (copies + 1)) * Math.PI / 180.0;

        var allCopiedIds = new List<ElementId>();

        using (var tx = new Transaction(_doc, "Create Radial Array"))
        {
            tx.Start();

            for (int i = 0; i < copies; i++)
            {
                double angle = angleStep * (i + 1);
                var copiedIds = ElementTransformUtils.CopyElements(
                    _doc, new List<ElementId> { wall.Id }, XYZ.Zero);

                foreach (var copiedId in copiedIds)
                {
                    ElementTransformUtils.RotateElement(_doc, copiedId, axis, angle);
                }

                allCopiedIds.AddRange(copiedIds);
            }

            tx.Commit();
        }

        await Assert.That(allCopiedIds.Count).IsEqualTo(copies);
    }

    [Test]
    public async Task LinearArray_SpacingConversion_MmToFeet()
    {
        double spacingXMm = 3000;
        double spacingYMm = 2000;
        double spacingZMm = 0;

        var offset = new XYZ(spacingXMm / 304.8, spacingYMm / 304.8, spacingZMm / 304.8);

        await Assert.That(offset.X * 304.8).IsEqualTo(spacingXMm).Within(0.001);
        await Assert.That(offset.Y * 304.8).IsEqualTo(spacingYMm).Within(0.001);
    }

    [Test]
    public async Task LinearArray_ZeroCount_NoCopies()
    {
        Wall wall;
        using (var tx = new Transaction(_doc, "Create Wall For Zero"))
        {
            tx.Start();
            wall = Wall.Create(_doc, Line.CreateBound(new XYZ(150, 0, 0), new XYZ(160, 0, 0)), _level.Id, false);
            tx.Commit();
        }

        int wallCountBefore = new FilteredElementCollector(_doc)
            .OfClass(typeof(Wall))
            .GetElementCount();

        // Zero copies - no array operation
        int copies = 0;

        using (var tx = new Transaction(_doc, "Zero Array"))
        {
            tx.Start();
            for (int i = 0; i < copies; i++)
            {
                ElementTransformUtils.CopyElements(_doc, new List<ElementId> { wall.Id }, new XYZ(0, 5, 0) * (i + 1));
            }
            tx.Commit();
        }

        int wallCountAfter = new FilteredElementCollector(_doc)
            .OfClass(typeof(Wall))
            .GetElementCount();

        await Assert.That(wallCountAfter).IsEqualTo(wallCountBefore);
    }

    [Test]
    public async Task RadialArray_AngleCalculation_StepsCorrect()
    {
        int copies = 4;
        double totalAngle = 360.0;
        double angleStep = totalAngle / (copies + 1);

        await Assert.That(angleStep).IsEqualTo(72.0).Within(0.001);

        // Verify angles for each copy
        for (int i = 0; i < copies; i++)
        {
            double expectedAngle = angleStep * (i + 1);
            await Assert.That(expectedAngle).IsGreaterThan(0);
            await Assert.That(expectedAngle).IsLessThan(360.0);
        }
    }

    [Test]
    public async Task Array_Rollback_NoElementsCreated()
    {
        Wall wall;
        using (var tx = new Transaction(_doc, "Create Wall For Rollback"))
        {
            tx.Start();
            wall = Wall.Create(_doc, Line.CreateBound(new XYZ(200, 0, 0), new XYZ(210, 0, 0)), _level.Id, false);
            tx.Commit();
        }

        int wallCountBefore = new FilteredElementCollector(_doc)
            .OfClass(typeof(Wall))
            .GetElementCount();

        using (var tx = new Transaction(_doc, "Rollback Array"))
        {
            tx.Start();
            for (int i = 0; i < 5; i++)
            {
                ElementTransformUtils.CopyElements(_doc, new List<ElementId> { wall.Id }, new XYZ(0, 5, 0) * (i + 1));
            }
            tx.RollBack();
        }

        int wallCountAfter = new FilteredElementCollector(_doc)
            .OfClass(typeof(Wall))
            .GetElementCount();

        await Assert.That(wallCountAfter).IsEqualTo(wallCountBefore);
    }
}
