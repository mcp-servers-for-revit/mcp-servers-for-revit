using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCPCommandSet.Helpers;
using RevitMCPCommandSet.Models.Common;
using RevitMCPSDK.API.Interfaces;

namespace RevitMCPCommandSet.Services
{
    public class BatchRenameEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        private readonly ManualResetEvent _resetEvent = new ManualResetEvent(false);

        public List<long> ElementIds { get; set; } = new List<long>();
        public string TargetCategory { get; set; } = "";
        public string FindText { get; set; } = "";
        public string ReplaceText { get; set; } = "";
        public string Prefix { get; set; } = "";
        public string Suffix { get; set; } = "";
        public bool DryRun { get; set; } = true;
        public AIResult<object> Result { get; private set; }

        public void SetParameters(List<long> elementIds, string targetCategory, string findText, string replaceText, string prefix, string suffix, bool dryRun)
        {
            ElementIds = elementIds ?? new List<long>();
            TargetCategory = targetCategory ?? "";
            FindText = findText ?? "";
            ReplaceText = replaceText ?? "";
            Prefix = prefix ?? "";
            Suffix = suffix ?? "";
            DryRun = dryRun;
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
                var elements = GetTargetElements(doc);

                if (elements.Count == 0)
                {
                    Result = new AIResult<object> { Success = false, Message = "No elements found to rename" };
                    return;
                }

                if (!DryRun && !ConfirmationHelper.Confirm("rename", elements.Count))
                {
                    Result = new AIResult<object>
                    {
                        Success = false,
                        Message = "Operation cancelled by user",
                        Response = new { cancelled = true, dryRun = false }
                    };
                    return;
                }

                var renameResults = new List<object>();
                int successCount = 0;

                using (var transaction = DryRun ? null : new Transaction(doc, "Batch Rename"))
                {
                    transaction?.Start();

                    foreach (var elem in elements)
                    {
                        string oldName = GetElementName(elem);
                        if (string.IsNullOrEmpty(oldName)) continue;

                        string newName = ComputeNewName(oldName);
                        if (newName == oldName) continue;

                        bool success = true;
                        string message = "";

                        if (!DryRun)
                        {
                            try
                            {
                                SetElementName(elem, newName);
                                successCount++;
                            }
                            catch (Exception ex)
                            {
                                success = false;
                                message = ex.Message;
                            }
                        }
                        else
                        {
                            successCount++;
                        }

                        renameResults.Add(new
                        {
#if REVIT2024_OR_GREATER
                            id = elem.Id.Value,
#else
                            id = elem.Id.IntegerValue,
#endif
                            oldName,
                            newName,
                            success,
                            message
                        });
                    }

                    transaction?.Commit();
                }

                Result = new AIResult<object>
                {
                    Success = true,
                    Message = DryRun
                        ? $"Preview: {successCount} elements would be renamed (dry run)"
                        : $"Renamed {successCount} elements",
                    Response = new
                    {
                        dryRun = DryRun,
                        totalProcessed = renameResults.Count,
                        successCount,
                        renames = renameResults
                    }
                };
            }
            catch (Exception ex)
            {
                Result = new AIResult<object>
                {
                    Success = false,
                    Message = $"Batch rename failed: {ex.Message}"
                };
            }
            finally
            {
                _resetEvent.Set();
            }
        }

        private List<Element> GetTargetElements(Document doc)
        {
            var elements = new List<Element>();

            if (ElementIds.Count > 0)
            {
                foreach (var id in ElementIds)
                {
                    var elem = doc.GetElement(ToElementId(id));
                    if (elem != null) elements.Add(elem);
                }
                return elements;
            }

            switch (TargetCategory)
            {
                case "Views":
                    return new FilteredElementCollector(doc)
                        .OfClass(typeof(View))
                        .Cast<View>()
                        .Where(v => !v.IsTemplate)
                        .Cast<Element>()
                        .ToList();
                case "Sheets":
                    return new FilteredElementCollector(doc)
                        .OfClass(typeof(ViewSheet))
                        .ToList();
                case "Levels":
                    return new FilteredElementCollector(doc)
                        .OfClass(typeof(Level))
                        .ToList();
                case "Grids":
                    return new FilteredElementCollector(doc)
                        .OfClass(typeof(Grid))
                        .ToList();
                case "Rooms":
                    return new FilteredElementCollector(doc)
                        .OfCategory(BuiltInCategory.OST_Rooms)
                        .WhereElementIsNotElementType()
                        .ToList();
                default:
                    return elements;
            }
        }

        private string GetElementName(Element elem)
        {
            if (elem is ViewSheet sheet) return sheet.Name;
            if (elem is View view) return view.Name;
            return elem.Name;
        }

        private void SetElementName(Element elem, string newName)
        {
            if (elem is ViewSheet sheet)
                sheet.Name = newName;
            else if (elem is View view)
                view.Name = newName;
            else
                elem.Name = newName;
        }

        private string ComputeNewName(string oldName)
        {
            string result = oldName;

            if (!string.IsNullOrEmpty(FindText))
                result = result.Replace(FindText, ReplaceText ?? "");

            if (!string.IsNullOrEmpty(Prefix))
                result = Prefix + result;

            if (!string.IsNullOrEmpty(Suffix))
                result = result + Suffix;

            return result;
        }

        private ElementId ToElementId(long id)
        {
#if REVIT2024_OR_GREATER
            return new ElementId(id);
#else
            return new ElementId((int)id);
#endif
        }

        public string GetName() => "Batch Rename";
    }
}
