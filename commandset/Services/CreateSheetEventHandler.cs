using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCPCommandSet.Models.Common;
using RevitMCPCommandSet.Models.Views;
using RevitMCPSDK.API.Interfaces;

namespace RevitMCPCommandSet.Services
{
    public class CreateSheetEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        private readonly ManualResetEvent _resetEvent = new ManualResetEvent(false);

        public SheetCreationInfo SheetInfo { get; set; }
        public AIResult<object> Result { get; private set; }

        public void SetParameters(SheetCreationInfo sheetInfo)
        {
            SheetInfo = sheetInfo;
            _resetEvent.Reset();
        }

        public bool WaitForCompletion(int timeoutMilliseconds = 10000)
        {
            return _resetEvent.WaitOne(timeoutMilliseconds);
        }

        public void Execute(UIApplication app)
        {
            try
            {
                var doc = app.ActiveUIDocument.Document;

                using (var transaction = new Transaction(doc, "Create Sheet"))
                {
                    transaction.Start();
                    try
                    {
                        // Find title block
                        ElementId titleBlockId = ElementId.InvalidElementId;

                        if (SheetInfo.TitleBlockTypeId > 0)
                        {
#if REVIT2024_OR_GREATER
                            titleBlockId = new ElementId((long)SheetInfo.TitleBlockTypeId);
#else
                            titleBlockId = new ElementId((int)SheetInfo.TitleBlockTypeId);
#endif
                        }
                        else if (!string.IsNullOrEmpty(SheetInfo.TitleBlockFamilyName))
                        {
                            var titleBlocks = new FilteredElementCollector(doc)
                                .OfClass(typeof(FamilySymbol))
                                .OfCategory(BuiltInCategory.OST_TitleBlocks)
                                .Cast<FamilySymbol>();

                            FamilySymbol titleBlock = null;
                            if (!string.IsNullOrEmpty(SheetInfo.TitleBlockTypeName))
                            {
                                titleBlock = titleBlocks.FirstOrDefault(tb =>
                                    tb.Family.Name.Equals(SheetInfo.TitleBlockFamilyName, StringComparison.OrdinalIgnoreCase) &&
                                    tb.Name.Equals(SheetInfo.TitleBlockTypeName, StringComparison.OrdinalIgnoreCase));
                            }

                            titleBlock ??= titleBlocks.FirstOrDefault(tb =>
                                tb.Family.Name.Equals(SheetInfo.TitleBlockFamilyName, StringComparison.OrdinalIgnoreCase));

                            if (titleBlock != null)
                            {
                                if (!titleBlock.IsActive) titleBlock.Activate();
                                titleBlockId = titleBlock.Id;
                            }
                        }
                        else
                        {
                            // Use first available title block
                            var firstTitleBlock = new FilteredElementCollector(doc)
                                .OfClass(typeof(FamilySymbol))
                                .OfCategory(BuiltInCategory.OST_TitleBlocks)
                                .Cast<FamilySymbol>()
                                .FirstOrDefault();

                            if (firstTitleBlock != null)
                            {
                                if (!firstTitleBlock.IsActive) firstTitleBlock.Activate();
                                titleBlockId = firstTitleBlock.Id;
                            }
                        }

                        var sheet = ViewSheet.Create(doc, titleBlockId);

                        if (!string.IsNullOrEmpty(SheetInfo.SheetNumber))
                            sheet.SheetNumber = SheetInfo.SheetNumber;

                        if (!string.IsNullOrEmpty(SheetInfo.SheetName))
                            sheet.Name = SheetInfo.SheetName;

                        transaction.Commit();

                        Result = new AIResult<object>
                        {
                            Success = true,
                            Message = $"Successfully created sheet '{sheet.SheetNumber} - {sheet.Name}'",
                            Response = new
                            {
#if REVIT2024_OR_GREATER
                                sheetId = sheet.Id.Value,
#else
                                sheetId = sheet.Id.IntegerValue,
#endif
                                sheetNumber = sheet.SheetNumber,
                                sheetName = sheet.Name
                            }
                        };
                    }
                    catch
                    {
                        if (transaction.GetStatus() == TransactionStatus.Started)
                            transaction.RollBack();
                        throw;
                    }
                }
            }
            catch (Exception ex)
            {
                Result = new AIResult<object>
                {
                    Success = false,
                    Message = $"Failed to create sheet: {ex.Message}"
                };
            }
            finally
            {
                _resetEvent.Set();
            }
        }

        public string GetName() => "Create Sheet";
    }
}
