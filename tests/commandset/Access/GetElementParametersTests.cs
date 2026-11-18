using Autodesk.Revit.DB;
using Nice3point.TUnit.Revit;
using Nice3point.TUnit.Revit.Executors;
using TUnit.Core;
using TUnit.Core.Executors;

namespace RevitMCPCommandSet.Tests.Access;

public class GetElementParametersTests : RevitApiTest
{
    private static Document _doc;
    private static Level _level;
    private static Wall _wall;

    [Before(HookType.Class)]
    [HookExecutor<RevitThreadExecutor>]
    public static void Setup()
    {
        _doc = Application.NewProjectDocument(UnitSystem.Imperial);

        using var tx = new Transaction(_doc, "Setup Parameter Tests");
        tx.Start();

        _level = Level.Create(_doc, 0.0);
        _level.Name = "Param Test Level";

        _wall = Wall.Create(_doc, Line.CreateBound(new XYZ(0, 0, 0), new XYZ(20, 0, 0)), _level.Id, false);

        tx.Commit();
    }

    [After(HookType.Class)]
    [HookExecutor<RevitThreadExecutor>]
    public static void Cleanup()
    {
        _doc?.Close(false);
    }

    [Test]
    public async Task Wall_InstanceParameters_NotEmpty()
    {
        var parameters = new List<Parameter>();
        foreach (Parameter param in _wall.Parameters)
        {
            parameters.Add(param);
        }

        await Assert.That(parameters.Count).IsGreaterThan(0);
    }

    [Test]
    public async Task Wall_TypeParameters_NotEmpty()
    {
        var typeId = _wall.GetTypeId();
        await Assert.That(typeId).IsNotEqualTo(ElementId.InvalidElementId);

        var typeElement = _doc.GetElement(typeId);
        await Assert.That(typeElement).IsNotNull();

        var typeParams = new List<Parameter>();
        foreach (Parameter param in typeElement.Parameters)
        {
            typeParams.Add(param);
        }

        await Assert.That(typeParams.Count).IsGreaterThan(0);
    }

    [Test]
    public async Task Parameter_ExtractInfo_NameAndStorageType()
    {
        foreach (Parameter param in _wall.Parameters)
        {
            string name = param.Definition?.Name ?? "Unknown";
            string storageType = param.StorageType.ToString();

            await Assert.That(name).IsNotNull();
            await Assert.That(storageType).IsNotNull();
            break; // Just check first parameter
        }
    }

    [Test]
    public async Task Parameter_StringValue_Readable()
    {
        var stringParams = new List<Parameter>();
        foreach (Parameter param in _wall.Parameters)
        {
            if (param.StorageType == StorageType.String && param.HasValue)
            {
                stringParams.Add(param);
            }
        }

        foreach (var param in stringParams)
        {
            string value = param.AsString();
            // String value can be null or empty, just verify no exception
            await Assert.That(value == null || value is string).IsTrue();
        }
    }

    [Test]
    public async Task Parameter_DoubleValue_Readable()
    {
        var doubleParams = new List<Parameter>();
        foreach (Parameter param in _wall.Parameters)
        {
            if (param.StorageType == StorageType.Double && param.HasValue)
            {
                doubleParams.Add(param);
            }
        }

        foreach (var param in doubleParams)
        {
            double value = param.AsDouble();
            // Just verify we can read without exception (value type always has a value)
            await Assert.That(value).IsNotEqualTo(double.NaN);
            break;
        }
    }

    [Test]
    public async Task Parameter_IntegerValue_Readable()
    {
        var intParams = new List<Parameter>();
        foreach (Parameter param in _wall.Parameters)
        {
            if (param.StorageType == StorageType.Integer && param.HasValue)
            {
                intParams.Add(param);
            }
        }

        foreach (var param in intParams)
        {
            int value = param.AsInteger();
            // Value type always has a value, just verify read succeeds
            await Assert.That(value).IsGreaterThanOrEqualTo(int.MinValue);
            break;
        }
    }

    [Test]
    public async Task Wall_LookupParameter_CommentsWritable()
    {
        var commentsParam = _wall.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);

        if (commentsParam != null)
        {
            await Assert.That(commentsParam.IsReadOnly).IsFalse();
            await Assert.That(commentsParam.StorageType).IsEqualTo(StorageType.String);

            using (var tx = new Transaction(_doc, "Set Comments"))
            {
                tx.Start();
                commentsParam.Set("Test Comment");
                tx.Commit();
            }

            await Assert.That(commentsParam.AsString()).IsEqualTo("Test Comment");
        }
    }

    [Test]
    public async Task Wall_SetParameter_Rollback_ValueUnchanged()
    {
        var commentsParam = _wall.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
        if (commentsParam == null) return;

        using (var tx = new Transaction(_doc, "Set Initial Comment"))
        {
            tx.Start();
            commentsParam.Set("Initial");
            tx.Commit();
        }

        using (var tx = new Transaction(_doc, "Rollback Comment"))
        {
            tx.Start();
            commentsParam.Set("Changed");
            tx.RollBack();
        }

        await Assert.That(commentsParam.AsString()).IsEqualTo("Initial");
    }

    [Test]
    public async Task Level_Parameters_HasName()
    {
        var nameParam = _level.get_Parameter(BuiltInParameter.DATUM_TEXT);

        if (nameParam != null)
        {
            await Assert.That(nameParam.HasValue).IsTrue();
            await Assert.That(nameParam.AsString()).IsEqualTo("Param Test Level");
        }
    }

    [Test]
    public async Task Element_Category_NotNull()
    {
        await Assert.That(_wall.Category).IsNotNull();
        await Assert.That(_wall.Category.Name).IsNotNullOrEmpty();
    }
}
