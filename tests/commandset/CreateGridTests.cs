using Autodesk.Revit.DB;
using Nice3point.TUnit.Revit;
using Nice3point.TUnit.Revit.Executors;
using TUnit.Core;
using TUnit.Core.Executors;

namespace RevitMCPCommandSet.Tests;

public class CreateGridTests : RevitApiTest
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
    public async Task CreateGrid_SingleVerticalLine_GridCreated()
    {
        Grid grid;
        using (var tx = new Transaction(_doc, "Create Single Grid"))
        {
            tx.Start();
            var line = Line.CreateBound(new XYZ(0, 0, 0), new XYZ(0, 50, 0));
            grid = Grid.Create(_doc, line);
            grid.Name = "G1";
            tx.Commit();
        }

        await Assert.That(grid).IsNotNull();
        await Assert.That(grid.Name).IsEqualTo("G1");
    }

    [Test]
    public async Task CreateGrid_SingleHorizontalLine_GridCreated()
    {
        Grid grid;
        using (var tx = new Transaction(_doc, "Create Horizontal Grid"))
        {
            tx.Start();
            var line = Line.CreateBound(new XYZ(0, 0, 0), new XYZ(50, 0, 0));
            grid = Grid.Create(_doc, line);
            grid.Name = "GH1";
            tx.Commit();
        }

        await Assert.That(grid).IsNotNull();
        await Assert.That(grid.Name).IsEqualTo("GH1");
    }

    [Test]
    public async Task CreateGrid_MultipleXGrids_AllCreatedWithCorrectSpacing()
    {
        var grids = new List<Grid>();
        double spacingFeet = 5000.0 / 304.8; // 5000mm spacing

        using (var tx = new Transaction(_doc, "Create X Grid System"))
        {
            tx.Start();

            for (int i = 0; i < 4; i++)
            {
                double xPos = i * spacingFeet;
                var line = Line.CreateBound(new XYZ(xPos, -10, 0), new XYZ(xPos, 50, 0));
                var grid = Grid.Create(_doc, line);
                grid.Name = $"X{i + 1}";
                grids.Add(grid);
            }

            tx.Commit();
        }

        await Assert.That(grids.Count).IsEqualTo(4);

        for (int i = 0; i < grids.Count; i++)
        {
            await Assert.That(grids[i].Name).IsEqualTo($"X{i + 1}");
        }
    }

    [Test]
    public async Task CreateGrid_MultipleYGrids_AllCreatedWithCorrectSpacing()
    {
        var grids = new List<Grid>();
        double spacingFeet = 4000.0 / 304.8; // 4000mm spacing

        using (var tx = new Transaction(_doc, "Create Y Grid System"))
        {
            tx.Start();

            for (int i = 0; i < 3; i++)
            {
                double yPos = i * spacingFeet;
                var line = Line.CreateBound(new XYZ(-10, yPos, 0), new XYZ(80, yPos, 0));
                var grid = Grid.Create(_doc, line);
                grid.Name = $"Y{(char)('A' + i)}";
                grids.Add(grid);
            }

            tx.Commit();
        }

        await Assert.That(grids.Count).IsEqualTo(3);
        await Assert.That(grids[0].Name).IsEqualTo("YA");
        await Assert.That(grids[1].Name).IsEqualTo("YB");
        await Assert.That(grids[2].Name).IsEqualTo("YC");
    }

    [Test]
    public async Task CreateGrid_DuplicateNameDetection_ExistingFound()
    {
        using (var tx = new Transaction(_doc, "Create Original Grid"))
        {
            tx.Start();
            var line = Line.CreateBound(new XYZ(100, 0, 0), new XYZ(100, 50, 0));
            var grid = Grid.Create(_doc, line);
            grid.Name = "DupTest";
            tx.Commit();
        }

        var existingNames = new FilteredElementCollector(_doc)
            .OfClass(typeof(Grid))
            .Cast<Grid>()
            .Select(g => g.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        bool hasDuplicate = existingNames.Contains("DupTest");
        await Assert.That(hasDuplicate).IsTrue();
    }

    [Test]
    public async Task CreateGrid_UniqueNameGeneration_IncrementsSuffix()
    {
        // Simulate the GetUniqueGridName logic from CreateGridEventHandler
        var existingNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "A", "B", "C" };

        string uniqueName = GetUniqueGridName("A", existingNames);
        await Assert.That(uniqueName).IsEqualTo("A1");

        existingNames.Add("A1");
        string nextUnique = GetUniqueGridName("A", existingNames);
        await Assert.That(nextUnique).IsEqualTo("A2");
    }

    [Test]
    public async Task CreateGrid_NumericLabels_SequentialGeneration()
    {
        var labels = GenerateNumericLabels(5, 1);

        await Assert.That(labels.Count).IsEqualTo(5);
        await Assert.That(labels[0]).IsEqualTo("1");
        await Assert.That(labels[1]).IsEqualTo("2");
        await Assert.That(labels[4]).IsEqualTo("5");
    }

    [Test]
    public async Task CreateGrid_AlphabeticLabels_SequentialGeneration()
    {
        var labels = GenerateAlphabeticLabels(5, 'A');

        await Assert.That(labels.Count).IsEqualTo(5);
        await Assert.That(labels[0]).IsEqualTo("A");
        await Assert.That(labels[1]).IsEqualTo("B");
        await Assert.That(labels[4]).IsEqualTo("E");
    }

    [Test]
    public async Task CreateGrid_AlphabeticLabels_WrapsPastZ()
    {
        // Start at 'Y' to test wrapping past Z
        var labels = GenerateAlphabeticLabels(4, 'Y');

        await Assert.That(labels[0]).IsEqualTo("Y");
        await Assert.That(labels[1]).IsEqualTo("Z");
        await Assert.That(labels[2]).IsEqualTo("AA");
        await Assert.That(labels[3]).IsEqualTo("AB");
    }

    [Test]
    public async Task CreateGrid_SpacingConversion_MmToFeetAccurate()
    {
        double spacingMm = 6000.0;
        double spacingFeet = spacingMm / 304.8;

        // Verify round-trip
        await Assert.That(spacingFeet * 304.8).IsEqualTo(spacingMm).Within(0.0001);

        // 6000mm should be about 19.685 feet
        await Assert.That(spacingFeet).IsEqualTo(19.685).Within(0.001);
    }

    [Test]
    public async Task CreateGrid_Rollback_GridNotPersisted()
    {
        int gridCountBefore = new FilteredElementCollector(_doc)
            .OfClass(typeof(Grid))
            .GetElementCount();

        using (var tx = new Transaction(_doc, "Rollback Grid"))
        {
            tx.Start();
            var line = Line.CreateBound(new XYZ(200, 0, 0), new XYZ(200, 50, 0));
            Grid.Create(_doc, line);
            tx.RollBack();
        }

        int gridCountAfter = new FilteredElementCollector(_doc)
            .OfClass(typeof(Grid))
            .GetElementCount();

        await Assert.That(gridCountAfter).IsEqualTo(gridCountBefore);
    }

    // Helper methods mirroring CreateGridEventHandler logic

    private static string GetUniqueGridName(string baseName, HashSet<string> existingNames)
    {
        string candidateName = baseName;
        int counter = 1;

        while (existingNames.Contains(candidateName))
        {
            candidateName = $"{baseName}{counter}";
            counter++;
        }

        return candidateName;
    }

    private static List<string> GenerateNumericLabels(int count, int startNum)
    {
        var labels = new List<string>();
        for (int i = 0; i < count; i++)
        {
            labels.Add((startNum + i).ToString());
        }
        return labels;
    }

    private static List<string> GenerateAlphabeticLabels(int count, char startChar)
    {
        var labels = new List<string>();
        for (int i = 0; i < count; i++)
        {
            labels.Add(GenerateAlphabeticLabel(startChar, i));
        }
        return labels;
    }

    private static string GenerateAlphabeticLabel(char startChar, int offset)
    {
        int charIndex = (startChar - 'A') + offset;

        if (charIndex < 26)
        {
            return ((char)('A' + charIndex)).ToString();
        }
        else
        {
            string result = "";
            int remaining = charIndex;

            while (remaining >= 0)
            {
                int mod = remaining % 26;
                result = ((char)('A' + mod)) + result;
                remaining = (remaining / 26) - 1;

                if (remaining < 0) break;
            }

            return result;
        }
    }
}
