using Autodesk.Revit.DB;
using Nice3point.TUnit.Revit;
using Nice3point.TUnit.Revit.Executors;
using TUnit.Core;
using TUnit.Core.Executors;

namespace RevitMCPCommandSet.Tests.DataExtraction;

public class GetWarningsTests : RevitApiTest
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
    public async Task GetWarnings_NewProject_ReturnsWarningsList()
    {
        var warnings = _doc.GetWarnings();
        await Assert.That(warnings).IsNotNull();
    }

    [Test]
    public async Task GetWarnings_NewProject_CountIsNonNegative()
    {
        var warnings = _doc.GetWarnings();
        await Assert.That(warnings.Count).IsGreaterThanOrEqualTo(0);
    }

    [Test]
    public async Task GetWarnings_WithOverlappingWalls_MayProduceWarnings()
    {
        // Create overlapping walls that may generate warnings
        using (var tx = new Transaction(_doc, "Create Overlapping Walls"))
        {
            tx.Start();

            var level = Level.Create(_doc, 0.0);
            level.Name = "Warning Test Level";

            // Create two walls on the same line (may generate overlap warning)
            Wall.Create(_doc, Line.CreateBound(new XYZ(0, 0, 0), new XYZ(10, 0, 0)), level.Id, false);
            Wall.Create(_doc, Line.CreateBound(new XYZ(0, 0, 0), new XYZ(10, 0, 0)), level.Id, false);

            tx.Commit();
        }

        var warnings = _doc.GetWarnings();
        // Overlapping walls typically generate warnings
        // The count depends on Revit's internal logic
        await Assert.That(warnings).IsNotNull();
    }

    [Test]
    public async Task Warning_Properties_DescriptionNotEmpty()
    {
        var warnings = _doc.GetWarnings();

        foreach (var warning in warnings)
        {
            string description = warning.GetDescriptionText();
            await Assert.That(description).IsNotNullOrEmpty();
        }
    }

    [Test]
    public async Task Warning_FailingElements_ReturnElementIds()
    {
        var warnings = _doc.GetWarnings();

        foreach (var warning in warnings)
        {
            var failingElements = warning.GetFailingElements();
            await Assert.That(failingElements).IsNotNull();
            // Each warning should reference at least one element
            await Assert.That(failingElements.Count).IsGreaterThan(0);
        }
    }

    [Test]
    public async Task Warning_Severity_IsValid()
    {
        var warnings = _doc.GetWarnings();

        foreach (var warning in warnings)
        {
            var severity = warning.GetSeverity();
            // Severity should be Warning or Error
            bool isValidSeverity = severity == FailureSeverity.Warning || severity == FailureSeverity.Error;
            await Assert.That(isValidSeverity).IsTrue();
        }
    }

    [Test]
    public async Task Warning_AdditionalElements_ReturnsList()
    {
        var warnings = _doc.GetWarnings();

        foreach (var warning in warnings)
        {
            var additionalElements = warning.GetAdditionalElements();
            await Assert.That(additionalElements).IsNotNull();
        }
    }
}
