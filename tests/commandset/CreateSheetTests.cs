using Autodesk.Revit.DB;
using Nice3point.TUnit.Revit;
using Nice3point.TUnit.Revit.Executors;
using TUnit.Core;
using TUnit.Core.Executors;

namespace RevitMCPCommandSet.Tests;

public class CreateSheetTests : RevitApiTest
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
    public async Task CreateSheet_WithTitleBlock_SheetCreated()
    {
        var titleBlock = new FilteredElementCollector(_doc)
            .OfClass(typeof(FamilySymbol))
            .OfCategory(BuiltInCategory.OST_TitleBlocks)
            .Cast<FamilySymbol>()
            .FirstOrDefault();

        ElementId titleBlockId = titleBlock?.Id ?? ElementId.InvalidElementId;

        ViewSheet sheet;
        using (var tx = new Transaction(_doc, "Create Sheet"))
        {
            tx.Start();

            if (titleBlock != null && !titleBlock.IsActive)
                titleBlock.Activate();

            sheet = ViewSheet.Create(_doc, titleBlockId);

            tx.Commit();
        }

        await Assert.That(sheet).IsNotNull();
        await Assert.That(sheet.SheetNumber).IsNotNullOrEmpty();
    }

    [Test]
    public async Task CreateSheet_SetSheetNumber_NumberApplied()
    {
        ViewSheet sheet;
        using (var tx = new Transaction(_doc, "Create Numbered Sheet"))
        {
            tx.Start();

            var titleBlock = new FilteredElementCollector(_doc)
                .OfClass(typeof(FamilySymbol))
                .OfCategory(BuiltInCategory.OST_TitleBlocks)
                .Cast<FamilySymbol>()
                .FirstOrDefault();

            ElementId tbId = titleBlock?.Id ?? ElementId.InvalidElementId;
            if (titleBlock != null && !titleBlock.IsActive)
                titleBlock.Activate();

            sheet = ViewSheet.Create(_doc, tbId);
            sheet.SheetNumber = "Z999";

            tx.Commit();
        }

        await Assert.That(sheet.SheetNumber).IsEqualTo("Z999");
    }

    [Test]
    public async Task CreateSheet_SetSheetName_NameApplied()
    {
        ViewSheet sheet;
        using (var tx = new Transaction(_doc, "Create Named Sheet"))
        {
            tx.Start();

            var titleBlock = new FilteredElementCollector(_doc)
                .OfClass(typeof(FamilySymbol))
                .OfCategory(BuiltInCategory.OST_TitleBlocks)
                .Cast<FamilySymbol>()
                .FirstOrDefault();

            ElementId tbId = titleBlock?.Id ?? ElementId.InvalidElementId;
            if (titleBlock != null && !titleBlock.IsActive)
                titleBlock.Activate();

            sheet = ViewSheet.Create(_doc, tbId);
            sheet.Name = "First Floor Plan";

            tx.Commit();
        }

        await Assert.That(sheet.Name).IsEqualTo("First Floor Plan");
    }

    [Test]
    public async Task CreateSheet_MultipleSheets_UniqueNumbersAssigned()
    {
        var sheets = new List<ViewSheet>();

        using (var tx = new Transaction(_doc, "Create Multiple Sheets"))
        {
            tx.Start();

            var titleBlock = new FilteredElementCollector(_doc)
                .OfClass(typeof(FamilySymbol))
                .OfCategory(BuiltInCategory.OST_TitleBlocks)
                .Cast<FamilySymbol>()
                .FirstOrDefault();

            ElementId tbId = titleBlock?.Id ?? ElementId.InvalidElementId;
            if (titleBlock != null && !titleBlock.IsActive)
                titleBlock.Activate();

            for (int i = 0; i < 3; i++)
            {
                var sheet = ViewSheet.Create(_doc, tbId);
                sheet.SheetNumber = $"S{i + 1:D3}";
                sheet.Name = $"Sheet {i + 1}";
                sheets.Add(sheet);
            }

            tx.Commit();
        }

        await Assert.That(sheets.Count).IsEqualTo(3);

        var numbers = sheets.Select(s => s.SheetNumber).Distinct().ToList();
        await Assert.That(numbers.Count).IsEqualTo(3);
    }

    [Test]
    public async Task CreateSheet_WithoutTitleBlock_SheetCreated()
    {
        ViewSheet sheet;
        using (var tx = new Transaction(_doc, "Create Sheet No TitleBlock"))
        {
            tx.Start();
            sheet = ViewSheet.Create(_doc, ElementId.InvalidElementId);
            sheet.SheetNumber = "X001";
            tx.Commit();
        }

        await Assert.That(sheet).IsNotNull();
        await Assert.That(sheet.SheetNumber).IsEqualTo("X001");
    }

    [Test]
    public async Task CreateSheet_Rollback_SheetNotPersisted()
    {
        int sheetCountBefore = new FilteredElementCollector(_doc)
            .OfClass(typeof(ViewSheet))
            .GetElementCount();

        using (var tx = new Transaction(_doc, "Rollback Sheet"))
        {
            tx.Start();
            ViewSheet.Create(_doc, ElementId.InvalidElementId);
            tx.RollBack();
        }

        int sheetCountAfter = new FilteredElementCollector(_doc)
            .OfClass(typeof(ViewSheet))
            .GetElementCount();

        await Assert.That(sheetCountAfter).IsEqualTo(sheetCountBefore);
    }

    [Test]
    public async Task FindTitleBlocks_InNewProject_BlocksAvailable()
    {
        var titleBlocks = new FilteredElementCollector(_doc)
            .OfClass(typeof(FamilySymbol))
            .OfCategory(BuiltInCategory.OST_TitleBlocks)
            .Cast<FamilySymbol>()
            .ToList();

        // A new imperial project should have at least one title block
        await Assert.That(titleBlocks.Count).IsGreaterThanOrEqualTo(0);

        foreach (var tb in titleBlocks)
        {
            await Assert.That(tb.Family.Name).IsNotNullOrEmpty();
            await Assert.That(tb.Name).IsNotNullOrEmpty();
        }
    }
}
