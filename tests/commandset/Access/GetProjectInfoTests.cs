using Autodesk.Revit.DB;
using Nice3point.TUnit.Revit;
using Nice3point.TUnit.Revit.Executors;
using TUnit.Core;
using TUnit.Core.Executors;

namespace RevitMCPCommandSet.Tests.Access;

public class GetProjectInfoTests : RevitApiTest
{
    private static Document _doc;

    [Before(HookType.Class)]
    [HookExecutor<RevitThreadExecutor>]
    public static void Setup()
    {
        _doc = Application.NewProjectDocument(UnitSystem.Imperial);

        using var tx = new Transaction(_doc, "Setup Project Info");
        tx.Start();

        // Create levels for testing
        var level1 = Level.Create(_doc, 0.0);
        level1.Name = "Ground Floor";

        var level2 = Level.Create(_doc, 3000.0 / 304.8);
        level2.Name = "First Floor";

        tx.Commit();
    }

    [After(HookType.Class)]
    [HookExecutor<RevitThreadExecutor>]
    public static void Cleanup()
    {
        _doc?.Close(false);
    }

    [Test]
    public async Task ProjectInformation_Exists_NotNull()
    {
        var projectInfo = _doc.ProjectInformation;
        await Assert.That(projectInfo).IsNotNull();
    }

    [Test]
    public async Task ProjectInformation_SetName_NameApplied()
    {
        using (var tx = new Transaction(_doc, "Set Project Name"))
        {
            tx.Start();
            _doc.ProjectInformation.Name = "Test MCP Project";
            tx.Commit();
        }

        await Assert.That(_doc.ProjectInformation.Name).IsEqualTo("Test MCP Project");
    }

    [Test]
    public async Task ProjectInformation_SetNumber_NumberApplied()
    {
        using (var tx = new Transaction(_doc, "Set Project Number"))
        {
            tx.Start();
            _doc.ProjectInformation.Number = "PRJ-2024-001";
            tx.Commit();
        }

        await Assert.That(_doc.ProjectInformation.Number).IsEqualTo("PRJ-2024-001");
    }

    [Test]
    public async Task ProjectInformation_SetAuthor_AuthorApplied()
    {
        using (var tx = new Transaction(_doc, "Set Author"))
        {
            tx.Start();
            _doc.ProjectInformation.Author = "Test Architect";
            tx.Commit();
        }

        await Assert.That(_doc.ProjectInformation.Author).IsEqualTo("Test Architect");
    }

    [Test]
    public async Task ProjectInformation_SetAddress_AddressApplied()
    {
        using (var tx = new Transaction(_doc, "Set Address"))
        {
            tx.Start();
            _doc.ProjectInformation.Address = "123 Test Street";
            tx.Commit();
        }

        await Assert.That(_doc.ProjectInformation.Address).IsEqualTo("123 Test Street");
    }

    [Test]
    public async Task Phases_InNewProject_AtLeastOnePhase()
    {
        var phases = _doc.Phases;
        await Assert.That(phases.Size).IsGreaterThan(0);

        foreach (Phase phase in phases)
        {
            await Assert.That(phase.Name).IsNotNullOrEmpty();
        }
    }

    [Test]
    public async Task Levels_OrderedByElevation_CorrectOrder()
    {
        var levels = new FilteredElementCollector(_doc)
            .OfClass(typeof(Level))
            .Cast<Level>()
            .OrderBy(l => l.Elevation)
            .ToList();

        await Assert.That(levels.Count).IsGreaterThanOrEqualTo(2);

        for (int i = 1; i < levels.Count; i++)
        {
            await Assert.That(levels[i].Elevation)
                .IsGreaterThanOrEqualTo(levels[i - 1].Elevation);
        }
    }

    [Test]
    public async Task Levels_ElevationConversion_FeetToMm()
    {
        var levels = new FilteredElementCollector(_doc)
            .OfClass(typeof(Level))
            .Cast<Level>()
            .ToList();

        foreach (var level in levels)
        {
            double elevationMm = level.Elevation * 304.8;
            // Verify round-trip
            double backToFeet = elevationMm / 304.8;
            await Assert.That(backToFeet).IsEqualTo(level.Elevation).Within(0.0001);
        }
    }

    [Test]
    public async Task IsWorkshared_NewProject_IsFalse()
    {
        await Assert.That(_doc.IsWorkshared).IsFalse();
    }

    [Test]
    public async Task RevitLinks_NewProject_NoLinks()
    {
        var links = new FilteredElementCollector(_doc)
            .OfClass(typeof(RevitLinkInstance))
            .ToList();

        await Assert.That(links.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Views_InNewProject_ViewsExist()
    {
        var views = new FilteredElementCollector(_doc)
            .OfClass(typeof(View))
            .Cast<View>()
            .Where(v => !v.IsTemplate)
            .ToList();

        await Assert.That(views.Count).IsGreaterThan(0);
    }
}
