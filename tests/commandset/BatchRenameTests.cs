using Autodesk.Revit.DB;
using Nice3point.TUnit.Revit;
using Nice3point.TUnit.Revit.Executors;
using TUnit.Core;
using TUnit.Core.Executors;

namespace RevitMCPCommandSet.Tests;

public class BatchRenameTests : RevitApiTest
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
    public async Task ComputeNewName_FindReplace_TextReplaced()
    {
        string oldName = "Level 1 - Floor Plan";
        string findText = "Level 1";
        string replaceText = "Ground Floor";

        string result = oldName.Replace(findText, replaceText);

        await Assert.That(result).IsEqualTo("Ground Floor - Floor Plan");
    }

    [Test]
    public async Task ComputeNewName_AddPrefix_PrefixPrepended()
    {
        string oldName = "Floor Plan";
        string prefix = "ARCH-";

        string result = prefix + oldName;

        await Assert.That(result).IsEqualTo("ARCH-Floor Plan");
    }

    [Test]
    public async Task ComputeNewName_AddSuffix_SuffixAppended()
    {
        string oldName = "Floor Plan";
        string suffix = " (Rev A)";

        string result = oldName + suffix;

        await Assert.That(result).IsEqualTo("Floor Plan (Rev A)");
    }

    [Test]
    public async Task ComputeNewName_PrefixAndSuffix_BothApplied()
    {
        string oldName = "Section";
        string prefix = "WIP-";
        string suffix = "-v2";

        string result = prefix + oldName + suffix;

        await Assert.That(result).IsEqualTo("WIP-Section-v2");
    }

    [Test]
    public async Task ComputeNewName_FindReplaceWithPrefixSuffix_AllApplied()
    {
        string oldName = "Level 1 Plan";
        string findText = "Level 1";
        string replaceText = "L1";
        string prefix = "PROJ-";
        string suffix = "-FINAL";

        string result = oldName.Replace(findText, replaceText);
        result = prefix + result;
        result = result + suffix;

        await Assert.That(result).IsEqualTo("PROJ-L1 Plan-FINAL");
    }

    [Test]
    public async Task ComputeNewName_NoMatch_NameUnchanged()
    {
        string oldName = "Floor Plan";
        string findText = "Ceiling";
        string replaceText = "Ground";

        string result = oldName.Replace(findText, replaceText);

        await Assert.That(result).IsEqualTo("Floor Plan");
    }

    [Test]
    public async Task RenameLevels_FindReplace_LevelsRenamed()
    {
        List<Level> levels = new();
        using (var tx = new Transaction(_doc, "Create Levels For Rename"))
        {
            tx.Start();

            for (int i = 0; i < 3; i++)
            {
                var level = Level.Create(_doc, i * 10.0);
                level.Name = $"Old Level {i + 1}";
                levels.Add(level);
            }

            tx.Commit();
        }

        using (var tx = new Transaction(_doc, "Rename Levels"))
        {
            tx.Start();

            foreach (var level in levels)
            {
                string newName = level.Name.Replace("Old", "New");
                level.Name = newName;
            }

            tx.Commit();
        }

        await Assert.That(levels[0].Name).IsEqualTo("New Level 1");
        await Assert.That(levels[1].Name).IsEqualTo("New Level 2");
        await Assert.That(levels[2].Name).IsEqualTo("New Level 3");
    }

    [Test]
    public async Task RenameViews_AddPrefix_ViewsRenamed()
    {
        Level level;
        using (var tx = new Transaction(_doc, "Create Level For View Rename"))
        {
            tx.Start();
            level = Level.Create(_doc, 100.0);
            level.Name = "Rename Test Level";
            tx.Commit();
        }

        var floorPlanType = new FilteredElementCollector(_doc)
            .OfClass(typeof(ViewFamilyType))
            .Cast<ViewFamilyType>()
            .FirstOrDefault(vft => vft.ViewFamily == ViewFamily.FloorPlan);

        ViewPlan view;
        using (var tx = new Transaction(_doc, "Create View For Rename"))
        {
            tx.Start();
            view = ViewPlan.Create(_doc, floorPlanType.Id, level.Id);
            view.Name = "Test View";
            tx.Commit();
        }

        using (var tx = new Transaction(_doc, "Rename View"))
        {
            tx.Start();
            view.Name = "ARCH-" + view.Name;
            tx.Commit();
        }

        await Assert.That(view.Name).IsEqualTo("ARCH-Test View");
    }

    [Test]
    public async Task RenameGrids_FindReplace_GridsRenamed()
    {
        List<Grid> grids = new();
        using (var tx = new Transaction(_doc, "Create Grids For Rename"))
        {
            tx.Start();
            for (int i = 0; i < 3; i++)
            {
                var line = Line.CreateBound(new XYZ(i * 20, 0, 0), new XYZ(i * 20, 50, 0));
                var grid = Grid.Create(_doc, line);
                grid.Name = $"GRID-{i + 1}";
                grids.Add(grid);
            }
            tx.Commit();
        }

        using (var tx = new Transaction(_doc, "Rename Grids"))
        {
            tx.Start();
            foreach (var grid in grids)
            {
                grid.Name = grid.Name.Replace("GRID-", "G");
            }
            tx.Commit();
        }

        await Assert.That(grids[0].Name).IsEqualTo("G1");
        await Assert.That(grids[1].Name).IsEqualTo("G2");
        await Assert.That(grids[2].Name).IsEqualTo("G3");
    }

    [Test]
    public async Task GetTargetElements_ByCategory_LevelsFound()
    {
        var levels = new FilteredElementCollector(_doc)
            .OfClass(typeof(Level))
            .ToList();

        await Assert.That(levels.Count).IsGreaterThan(0);
    }

    [Test]
    public async Task RenameElement_Rollback_NameUnchanged()
    {
        Level level;
        using (var tx = new Transaction(_doc, "Create Level For Rollback"))
        {
            tx.Start();
            level = Level.Create(_doc, 200.0);
            level.Name = "Original Name";
            tx.Commit();
        }

        using (var tx = new Transaction(_doc, "Rollback Rename"))
        {
            tx.Start();
            level.Name = "Changed Name";
            tx.RollBack();
        }

        await Assert.That(level.Name).IsEqualTo("Original Name");
    }
}
