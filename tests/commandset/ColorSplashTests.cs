using Autodesk.Revit.DB;
using Nice3point.TUnit.Revit;
using Nice3point.TUnit.Revit.Executors;
using TUnit.Core;
using TUnit.Core.Executors;

namespace RevitMCPCommandSet.Tests;

public class ColorSplashTests : RevitApiTest
{
    private static Document _doc;
    private static Level _level;
    private static ViewPlan _floorPlan;

    [Before(HookType.Class)]
    [HookExecutor<RevitThreadExecutor>]
    public static void Setup()
    {
        _doc = Application.NewProjectDocument(UnitSystem.Imperial);

        using var tx = new Transaction(_doc, "Setup Color Splash Test Environment");
        tx.Start();

        _level = Level.Create(_doc, 0.0);
        _level.Name = "Color Test Level";

        var floorPlanType = new FilteredElementCollector(_doc)
            .OfClass(typeof(ViewFamilyType))
            .Cast<ViewFamilyType>()
            .FirstOrDefault(vft => vft.ViewFamily == ViewFamily.FloorPlan);

        if (floorPlanType != null)
        {
            _floorPlan = ViewPlan.Create(_doc, floorPlanType.Id, _level.Id);
        }

        // Create walls with different types to test parameter grouping
        var p1 = new XYZ(0, 0, 0);
        var p2 = new XYZ(10, 0, 0);
        var p3 = new XYZ(20, 0, 0);
        var p4 = new XYZ(30, 0, 0);

        Wall.Create(_doc, Line.CreateBound(p1, p2), _level.Id, false);
        Wall.Create(_doc, Line.CreateBound(p2, p3), _level.Id, false);
        Wall.Create(_doc, Line.CreateBound(p3, p4), _level.Id, false);

        tx.Commit();
    }

    [After(HookType.Class)]
    [HookExecutor<RevitThreadExecutor>]
    public static void Cleanup()
    {
        _doc?.Close(false);
    }

    [Test]
    public async Task GroupElementsByParameter_WallComments_GroupsCorrectly()
    {
        // Set comments on walls to create groups
        var walls = new FilteredElementCollector(_doc)
            .OfCategory(BuiltInCategory.OST_Walls)
            .WhereElementIsNotElementType()
            .ToElements();

        await Assert.That(walls.Count).IsGreaterThanOrEqualTo(3);

        using (var tx = new Transaction(_doc, "Set Wall Comments"))
        {
            tx.Start();
            int i = 0;
            foreach (var wall in walls)
            {
                var param = wall.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
                if (param != null && !param.IsReadOnly)
                {
                    param.Set(i < 2 ? "Group A" : "Group B");
                }
                i++;
            }
            tx.Commit();
        }

        // Group by parameter value (mimics handler logic)
        var groups = new Dictionary<string, List<ElementId>>();
        foreach (var wall in walls)
        {
            var param = wall.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
            string value = param?.HasValue == true ? param.AsString() ?? "None" : "None";

            if (!groups.ContainsKey(value))
                groups[value] = new List<ElementId>();
            groups[value].Add(wall.Id);
        }

        await Assert.That(groups.ContainsKey("Group A")).IsTrue();
        await Assert.That(groups.ContainsKey("Group B")).IsTrue();
        await Assert.That(groups["Group A"].Count).IsEqualTo(2);
    }

    [Test]
    public async Task ApplyGraphicOverrides_SetColor_OverrideApplied()
    {
        await Assert.That(_floorPlan).IsNotNull();

        var walls = new FilteredElementCollector(_doc)
            .OfCategory(BuiltInCategory.OST_Walls)
            .WhereElementIsNotElementType()
            .ToElements();

        await Assert.That(walls.Count).IsGreaterThan(0);

        using var tx = new Transaction(_doc, "Apply Overrides");
        tx.Start();

        var color = new Color(255, 0, 0);
        var overrides = new OverrideGraphicSettings();
        overrides.SetProjectionLineColor(color);
        overrides.SetSurfaceForegroundPatternColor(color);

        var targetId = walls.First().Id;
        _floorPlan.SetElementOverrides(targetId, overrides);

        tx.Commit();

        var applied = _floorPlan.GetElementOverrides(targetId);
        await Assert.That((int)applied.ProjectionLineColor.Red).IsEqualTo(255);
        await Assert.That((int)applied.ProjectionLineColor.Green).IsEqualTo(0);
        await Assert.That((int)applied.ProjectionLineColor.Blue).IsEqualTo(0);
    }

    [Test]
    public async Task FindSolidFillPattern_InDocument_PatternFound()
    {
        var solidFillId = ElementId.InvalidElementId;

        var patterns = new FilteredElementCollector(_doc)
            .OfClass(typeof(FillPatternElement))
            .Cast<FillPatternElement>();

        foreach (var patternElement in patterns)
        {
            var pattern = patternElement.GetFillPattern();
            if (pattern.IsSolidFill)
            {
                solidFillId = patternElement.Id;
                break;
            }
        }

        await Assert.That(solidFillId).IsNotEqualTo(ElementId.InvalidElementId);
    }

    [Test]
    public async Task CustomColorMapping_ArrayOfColors_MapsCorrectly()
    {
        var paramValues = new List<string> { "Value A", "Value B", "Value C" };
        var customColors = new List<int[]>
        {
            new[] { 255, 0, 0 },
            new[] { 0, 255, 0 },
            new[] { 0, 0, 255 }
        };

        var colorMap = new Dictionary<string, int[]>();
        for (int i = 0; i < paramValues.Count; i++)
        {
            if (i < customColors.Count)
                colorMap[paramValues[i]] = customColors[i];
        }

        await Assert.That(colorMap["Value A"].SequenceEqual(new[] { 255, 0, 0 })).IsTrue();
        await Assert.That(colorMap["Value B"].SequenceEqual(new[] { 0, 255, 0 })).IsTrue();
        await Assert.That(colorMap["Value C"].SequenceEqual(new[] { 0, 0, 255 })).IsTrue();
    }

    [Test]
    public async Task GradientColorGeneration_BlueToRed_InterpolatesCorrectly()
    {
        var paramValues = new List<string> { "Low", "Mid", "High" };
        int[] startColor = { 0, 0, 180 };
        int[] endColor = { 180, 0, 0 };

        var colorMap = new Dictionary<string, int[]>();
        for (int i = 0; i < paramValues.Count; i++)
        {
            double ratio = (double)i / (paramValues.Count - 1);
            int[] color =
            {
                (int)(startColor[0] + (endColor[0] - startColor[0]) * ratio),
                (int)(startColor[1] + (endColor[1] - startColor[1]) * ratio),
                (int)(startColor[2] + (endColor[2] - startColor[2]) * ratio)
            };
            colorMap[paramValues[i]] = color;
        }

        // First should be blue (0,0,180)
        await Assert.That(colorMap["Low"][0]).IsEqualTo(0);
        await Assert.That(colorMap["Low"][2]).IsEqualTo(180);

        // Last should be red (180,0,0)
        await Assert.That(colorMap["High"][0]).IsEqualTo(180);
        await Assert.That(colorMap["High"][2]).IsEqualTo(0);

        // Mid should be interpolated (90,0,90)
        await Assert.That(colorMap["Mid"][0]).IsEqualTo(90);
        await Assert.That(colorMap["Mid"][2]).IsEqualTo(90);
    }
}
