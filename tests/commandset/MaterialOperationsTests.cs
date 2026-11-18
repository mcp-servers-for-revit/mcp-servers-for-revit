using Autodesk.Revit.DB;
using Nice3point.TUnit.Revit;
using Nice3point.TUnit.Revit.Executors;
using RevitMCPCommandSet.Models;
using RevitMCPCommandSet.Utils;
using TUnit.Core;
using TUnit.Core.Executors;

namespace RevitMCPCommandSet.Tests;

public class MaterialOperationsTests : RevitApiTest
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

    // ---------------------------------------------------------------------------------------------
    // NAAKT-controle (zonder Revit-document)
    // ---------------------------------------------------------------------------------------------

    [Test]
    public async Task Check_ValidName_IsValid()
    {
        var result = NaaktCatalogUtils.Check("beton_gewapend_ihw");

        await Assert.That(result.Valid).IsTrue();
        await Assert.That(result.Issues.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Check_FreeTail_IsValid()
    {
        var result = NaaktCatalogUtils.Check("beton_gewapend_ihw_RAL 9010");

        await Assert.That(result.Valid).IsTrue();
        await Assert.That(result.Custom).IsEqualTo("RAL 9010");
    }

    [Test]
    public async Task Check_WrongNotationAndApplication_GivesSuggestion()
    {
        var result = NaaktCatalogUtils.Check("Hout_CLT_element");

        await Assert.That(result.Valid).IsFalse();
        await Assert.That(result.Suggestion).IsEqualTo("hout_clt_prefab-element");
    }

    [Test]
    public async Task Check_CharacteristicOfOtherGroup_SuggestsOtherGroup()
    {
        var result = NaaktCatalogUtils.Check("kunststof_resolschuim_plaat");

        await Assert.That(result.Valid).IsFalse();
        await Assert.That(result.Suggestion).IsEqualTo("isolatie_resolschuim_plaat");
    }

    [Test]
    public async Task Check_TooFewParts_IsInvalidWithoutSuggestion()
    {
        var result = NaaktCatalogUtils.Check("Beton gewapend");

        await Assert.That(result.Valid).IsFalse();
        await Assert.That(result.Suggestion).IsNull();
    }

    // ---------------------------------------------------------------------------------------------
    // options
    // ---------------------------------------------------------------------------------------------

    [Test]
    public async Task Options_WithoutMainGroup_ListsMainGroups()
    {
        var result = MaterialOperationsUtils.Execute(_doc, new ManageMaterialsRequest { Action = "options" });

        await Assert.That(result.MainGroups.Contains("beton")).IsTrue();
        await Assert.That(result.Characteristics).IsNull();
    }

    [Test]
    public async Task Options_WithMainGroup_ListsCharacteristicsAndApplications()
    {
        var result = MaterialOperationsUtils.Execute(_doc,
            new ManageMaterialsRequest { Action = "options", MainGroup = "hout" });

        await Assert.That(result.Characteristics.Contains("clt")).IsTrue();
        await Assert.That(result.Applications.Contains("prefab-element")).IsTrue();
    }

    // ---------------------------------------------------------------------------------------------
    // create
    // ---------------------------------------------------------------------------------------------

    [Test]
    public async Task Create_DryRun_DoesNotAddMaterial()
    {
        int before = CountMaterials();

        var result = MaterialOperationsUtils.Execute(_doc, new ManageMaterialsRequest
        {
            Action = "create",
            DryRun = true,
            Materials = new List<MaterialInput> { Naakt("metaal", "rvs", "plaat", "dryrun") }
        });
        int after = CountMaterials();

        await Assert.That(result.DryRun).IsEqualTo(true);
        await Assert.That(result.Results[0].Success).IsTrue();
        await Assert.That(result.Results[0].NewName).IsEqualTo("metaal_rvs_plaat_dryrun");
        await Assert.That(after).IsEqualTo(before);
    }

    [Test]
    public async Task Create_AddsMaterialWithNaaktNameAndColor()
    {
        var input = Naakt("beton", "gewapend", "ihw", "create");
        input.Color = "10,20,30";

        var result = MaterialOperationsUtils.Execute(_doc, new ManageMaterialsRequest
        {
            Action = "create",
            Materials = new List<MaterialInput> { input }
        });

        await Assert.That(result.Results[0].Success).IsTrue();
        await Assert.That(result.Results[0].Status).IsEqualTo("created");

        var material = FindMaterial("beton_gewapend_ihw_create");
        await Assert.That(material).IsNotNull();
        await Assert.That(material.Color.Red).IsEqualTo((byte)10);
        await Assert.That(material.Color.Green).IsEqualTo((byte)20);
        await Assert.That(material.Color.Blue).IsEqualTo((byte)30);
    }

    [Test]
    public async Task Create_WithTemplate_SharesAllAssetsWithoutCopies()
    {
        Material template = AddMaterialWithAssets("Sjabloon met assets");
        int assetsBefore = CountPropertySets();

        var input = Naakt("beton", "gewapend", "ihw", "sjabloon");
        input.Template = template.Name;

        var result = MaterialOperationsUtils.Execute(_doc, new ManageMaterialsRequest
        {
            Action = "create",
            Materials = new List<MaterialInput> { input }
        });

        var material = FindMaterial("beton_gewapend_ihw_sjabloon");
        await Assert.That(result.Results[0].Success).IsTrue();
        await Assert.That(result.Results[0].Message).IsNull();
        await Assert.That(material.StructuralAssetId).IsEqualTo(template.StructuralAssetId);
        await Assert.That(material.ThermalAssetId).IsEqualTo(template.ThermalAssetId);
        await Assert.That(material.MaterialClass).IsEqualTo(template.MaterialClass);
        await Assert.That(CountPropertySets()).IsEqualTo(assetsBefore);
    }

    [Test]
    public async Task Create_ExistingName_Fails()
    {
        AddMaterial("beton_gewapend_ihw_bestaat");
        int before = CountMaterials();

        var result = MaterialOperationsUtils.Execute(_doc, new ManageMaterialsRequest
        {
            Action = "create",
            Materials = new List<MaterialInput> { Naakt("beton", "gewapend", "ihw", "bestaat") }
        });
        int after = CountMaterials();

        await Assert.That(result.Results[0].Success).IsFalse();
        await Assert.That(result.Results[0].Status).IsEqualTo("failed");
        await Assert.That(after).IsEqualTo(before);
    }

    [Test]
    public async Task Create_InvalidNaakt_FailsWithIssuesAndSuggestion()
    {
        int before = CountMaterials();

        var result = MaterialOperationsUtils.Execute(_doc, new ManageMaterialsRequest
        {
            Action = "create",
            Materials = new List<MaterialInput> { Naakt("hout", "clt", "element", "ongeldig") }
        });
        int after = CountMaterials();

        await Assert.That(result.Results[0].Success).IsFalse();
        await Assert.That(result.Results[0].Issues.Count).IsGreaterThan(0);
        await Assert.That(result.Results[0].Suggestion).IsEqualTo("hout_clt_prefab-element_ongeldig");
        await Assert.That(after).IsEqualTo(before);
    }

    [Test]
    public async Task Create_UnknownFillPattern_FailsWithoutCreating()
    {
        var input = Naakt("beton", "gewapend", "ihw", "patroon");
        input.SurfaceForeground = new MaterialPatternInput { Pattern = "bestaat-niet-in-dit-project" };
        int before = CountMaterials();

        var result = MaterialOperationsUtils.Execute(_doc, new ManageMaterialsRequest
        {
            Action = "create",
            Materials = new List<MaterialInput> { input }
        });
        int after = CountMaterials();

        await Assert.That(result.Results[0].Success).IsFalse();
        await Assert.That(after).IsEqualTo(before);
    }

    [Test]
    public async Task Create_SameNameTwiceInBatch_SecondFails()
    {
        var result = MaterialOperationsUtils.Execute(_doc, new ManageMaterialsRequest
        {
            Action = "create",
            Materials = new List<MaterialInput>
            {
                Naakt("glas", "helder", "plaat", "dubbel"),
                Naakt("glas", "helder", "plaat", "dubbel")
            }
        });

        await Assert.That(result.Results[0].Success).IsTrue();
        await Assert.That(result.Results[1].Success).IsFalse();
        await Assert.That(result.Succeeded).IsEqualTo(1);
        await Assert.That(result.Failed).IsEqualTo(1);
    }

    // ---------------------------------------------------------------------------------------------
    // update
    // ---------------------------------------------------------------------------------------------

    [Test]
    public async Task Update_RenamesNonNaaktMaterialToNaakt()
    {
        ElementId id = AddMaterial("Beton gewapend update1");

        var result = MaterialOperationsUtils.Execute(_doc, new ManageMaterialsRequest
        {
            Action = "update",
            Updates = new List<MaterialInput>
            {
                new MaterialInput
                {
                    Id = IdText(id),
                    MainGroup = "beton",
                    Characteristic = "gewapend",
                    Application = "ihw",
                    Custom = "update1"
                }
            }
        });

        await Assert.That(result.Results[0].Success).IsTrue();
        await Assert.That(result.Results[0].OldName).IsEqualTo("Beton gewapend update1");
        await Assert.That(((Material)_doc.GetElement(id)).Name).IsEqualTo("beton_gewapend_ihw_update1");
    }

    [Test]
    public async Task Update_OnlyApplicationGiven_KeepsOtherPartsOfCurrentName()
    {
        ElementId id = AddMaterial("hout_clt_plaat_update2");

        var result = MaterialOperationsUtils.Execute(_doc, new ManageMaterialsRequest
        {
            Action = "update",
            Updates = new List<MaterialInput>
            {
                new MaterialInput { Id = IdText(id), Application = "prefab-element" }
            }
        });

        await Assert.That(result.Results[0].Success).IsTrue();
        await Assert.That(((Material)_doc.GetElement(id)).Name).IsEqualTo("hout_clt_prefab-element_update2");
    }

    [Test]
    public async Task Update_DryRun_DoesNotRename()
    {
        ElementId id = AddMaterial("Wand update3");

        var result = MaterialOperationsUtils.Execute(_doc, new ManageMaterialsRequest
        {
            Action = "update",
            DryRun = true,
            Updates = new List<MaterialInput>
            {
                new MaterialInput
                {
                    Id = IdText(id),
                    MainGroup = "samengesteld",
                    Characteristic = "element",
                    Application = "wand"
                }
            }
        });

        await Assert.That(result.Results[0].Success).IsTrue();
        await Assert.That(result.Results[0].NewName).IsEqualTo("samengesteld_element_wand");
        await Assert.That(((Material)_doc.GetElement(id)).Name).IsEqualTo("Wand update3");
    }

    [Test]
    public async Task Update_NameCollision_FailsAndKeepsName()
    {
        AddMaterial("beton_gewapend_ihw_botsing");
        ElementId id = AddMaterial("Beton botsing");

        var result = MaterialOperationsUtils.Execute(_doc, new ManageMaterialsRequest
        {
            Action = "update",
            Updates = new List<MaterialInput>
            {
                new MaterialInput
                {
                    Id = IdText(id),
                    MainGroup = "beton",
                    Characteristic = "gewapend",
                    Application = "ihw",
                    Custom = "botsing"
                }
            }
        });

        await Assert.That(result.Results[0].Success).IsFalse();
        await Assert.That(((Material)_doc.GetElement(id)).Name).IsEqualTo("Beton botsing");
    }

    [Test]
    public async Task Update_OneItemFails_OthersStillApplied()
    {
        ElementId good = AddMaterial("Metaal update5");

        var result = MaterialOperationsUtils.Execute(_doc, new ManageMaterialsRequest
        {
            Action = "update",
            Updates = new List<MaterialInput>
            {
                new MaterialInput { Id = "999999999", MainGroup = "beton", Characteristic = "gewapend", Application = "ihw" },
                new MaterialInput
                {
                    Id = IdText(good),
                    MainGroup = "metaal",
                    Characteristic = "staal",
                    Application = "profiel",
                    Custom = "update5"
                }
            }
        });

        await Assert.That(result.Results[0].Success).IsFalse();
        await Assert.That(result.Results[1].Success).IsTrue();
        await Assert.That(((Material)_doc.GetElement(good)).Name).IsEqualTo("metaal_staal_profiel_update5");
    }

    [Test]
    public async Task Update_NoChangesGiven_Fails()
    {
        ElementId id = AddMaterial("Leeg update6");

        var result = MaterialOperationsUtils.Execute(_doc, new ManageMaterialsRequest
        {
            Action = "update",
            Updates = new List<MaterialInput> { new MaterialInput { Id = IdText(id) } }
        });

        await Assert.That(result.Results[0].Success).IsFalse();
    }

    // ---------------------------------------------------------------------------------------------
    // list
    // ---------------------------------------------------------------------------------------------

    [Test]
    public async Task List_Filters_SplitValidAndInvalid()
    {
        AddMaterial("beton_gewapend_ihw_list1");
        AddMaterial("Beton gewapend list2");

        var valid = MaterialOperationsUtils.Execute(_doc,
            new ManageMaterialsRequest { Action = "list", Filter = "valid", NameContains = "list" });
        var invalid = MaterialOperationsUtils.Execute(_doc,
            new ManageMaterialsRequest { Action = "list", Filter = "invalid", NameContains = "list" });
        var all = MaterialOperationsUtils.Execute(_doc,
            new ManageMaterialsRequest { Action = "list", NameContains = "list" });

        await Assert.That(valid.Count).IsEqualTo(1);
        await Assert.That(invalid.Count).IsEqualTo(1);
        await Assert.That(all.Total).IsEqualTo(2);
        await Assert.That(valid.Materials[0].Name).IsEqualTo("beton_gewapend_ihw_list1");
    }

    [Test]
    public async Task List_NonNaaktFilter_OnlyNamesWithoutMainGroup()
    {
        AddMaterial("Wand non1");
        AddMaterial("beton_clt_plaat_non2");

        var result = MaterialOperationsUtils.Execute(_doc,
            new ManageMaterialsRequest { Action = "list", Filter = "non-naakt", NameContains = "non" });

        await Assert.That(result.Count).IsEqualTo(1);
        await Assert.That(result.Materials[0].Name).IsEqualTo("Wand non1");
    }

    [Test]
    public async Task List_OffsetAndLimit_PageResults()
    {
        AddMaterial("beton_gewapend_ihw_pagina1");
        AddMaterial("beton_gewapend_ihw_pagina2");
        AddMaterial("beton_gewapend_ihw_pagina3");

        var result = MaterialOperationsUtils.Execute(_doc, new ManageMaterialsRequest
        {
            Action = "list",
            NameContains = "pagina",
            Offset = 1,
            Limit = 1
        });

        await Assert.That(result.Total).IsEqualTo(3);
        await Assert.That(result.Count).IsEqualTo(1);
        await Assert.That(result.Materials[0].Name).IsEqualTo("beton_gewapend_ihw_pagina2");
    }

    // ---------------------------------------------------------------------------------------------
    // Hulpmethodes
    // ---------------------------------------------------------------------------------------------

    private static MaterialInput Naakt(string mainGroup, string characteristic, string application, string custom = null)
    {
        return new MaterialInput
        {
            MainGroup = mainGroup,
            Characteristic = characteristic,
            Application = application,
            Custom = custom
        };
    }

    private static ElementId AddMaterial(string name)
    {
        using var tx = new Transaction(_doc, "Testmateriaal");
        tx.Start();
        ElementId id = Material.Create(_doc, name);
        tx.Commit();
        return id;
    }

    private static Material AddMaterialWithAssets(string name)
    {
        using var tx = new Transaction(_doc, "Testmateriaal met assets");
        tx.Start();
        var material = (Material)_doc.GetElement(Material.Create(_doc, name));
        material.MaterialClass = "Beton";
        var structural = PropertySetElement.Create(_doc, new StructuralAsset(name + " physical", StructuralAssetClass.Concrete));
        var thermal = PropertySetElement.Create(_doc, new ThermalAsset(name + " thermal", ThermalMaterialType.Solid));
        material.SetMaterialAspectByPropertySet(MaterialAspect.Structural, structural.Id);
        material.SetMaterialAspectByPropertySet(MaterialAspect.Thermal, thermal.Id);
        tx.Commit();
        return material;
    }

    private static Material FindMaterial(string name)
    {
        return new FilteredElementCollector(_doc)
            .OfClass(typeof(Material))
            .Cast<Material>()
            .FirstOrDefault(m => m.Name == name);
    }

    private static int CountPropertySets()
    {
        return new FilteredElementCollector(_doc).OfClass(typeof(PropertySetElement)).GetElementCount();
    }

    private static int CountMaterials()
    {
        return new FilteredElementCollector(_doc).OfClass(typeof(Material)).GetElementCount();
    }

    private static string IdText(ElementId id)
    {
        return id.GetValue().ToString();
    }
}
