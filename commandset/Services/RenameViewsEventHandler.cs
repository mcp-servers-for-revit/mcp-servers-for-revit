using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCPCommandSet.Models.Common;
using RevitMCPSDK.API.Interfaces;

namespace RevitMCPCommandSet.Services
{
    public class RenameViewsEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        private readonly ManualResetEvent _resetEvent = new ManualResetEvent(false);

        public string Operation { get; set; } = "find_replace";
        public string Prefix { get; set; } = "";
        public string Suffix { get; set; } = "";
        public string FindText { get; set; } = "";
        public string ReplaceText { get; set; } = "";
        public List<string> ViewTypes { get; set; } = new List<string>();
        public string FilterName { get; set; } = "";
        public bool DryRun { get; set; } = false;
        public AIResult<object> Result { get; private set; }

        public void SetParameters(string operation, string prefix, string suffix, string findText, string replaceText, List<string> viewTypes, string filterName, bool dryRun)
        {
            Operation = operation ?? "find_replace";
            Prefix = prefix ?? "";
            Suffix = suffix ?? "";
            FindText = findText ?? "";
            ReplaceText = replaceText ?? "";
            ViewTypes = viewTypes ?? new List<string>();
            FilterName = filterName ?? "";
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
                var views = GetTargetViews(doc);

                if (views.Count == 0)
                {
                    Result = new AIResult<object> { Success = false, Message = "No views found matching the specified criteria" };
                    return;
                }

                var renameResults = new List<object>();
                int successCount = 0;

                using (var transaction = DryRun ? null : new Transaction(doc, "Rename Views"))
                {
                    transaction?.Start();

                    foreach (var view in views)
                    {
                        string oldName = view.Name;
                        if (string.IsNullOrEmpty(oldName)) continue;

                        string newName = ComputeNewName(oldName);
                        if (newName == oldName) continue;

                        bool success = true;
                        string message = "";

                        if (!DryRun)
                        {
                            try
                            {
                                view.Name = newName;
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
                            viewId = view.Id.Value,
#else
                            viewId = view.Id.IntegerValue,
#endif
                            viewType = view.ViewType.ToString(),
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
                        ? $"Preview: {successCount} views would be renamed (dry run)"
                        : $"Renamed {successCount} views",
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
                    Message = $"Rename views failed: {ex.Message}"
                };
            }
            finally
            {
                _resetEvent.Set();
            }
        }

        private List<View> GetTargetViews(Document doc)
        {
            var allViews = new FilteredElementCollector(doc)
                .OfClass(typeof(View))
                .Cast<View>()
                .Where(v => !v.IsTemplate)
                .Where(v => v.ViewType != ViewType.ProjectBrowser && v.ViewType != ViewType.SystemBrowser)
                .ToList();

            if (ViewTypes.Count > 0)
            {
                var targetViewTypes = new HashSet<ViewType>();
                foreach (var vt in ViewTypes)
                {
                    var mapped = MapStringToViewType(vt);
                    if (mapped.HasValue)
                        targetViewTypes.Add(mapped.Value);
                }

                allViews = allViews.Where(v => targetViewTypes.Contains(v.ViewType)).ToList();
            }

            if (!string.IsNullOrEmpty(FilterName))
            {
                allViews = allViews.Where(v => v.Name.Contains(FilterName)).ToList();
            }

            return allViews;
        }

        private ViewType? MapStringToViewType(string viewTypeString)
        {
            switch (viewTypeString)
            {
                case "FloorPlan": return ViewType.FloorPlan;
                case "CeilingPlan": return ViewType.CeilingPlan;
                case "Section": return ViewType.Section;
                case "Elevation": return ViewType.Elevation;
                case "ThreeDimensional": return ViewType.ThreeD;
                case "DraftingView": return ViewType.DraftingView;
                case "Legend": return ViewType.Legend;
                case "Schedule": return ViewType.Schedule;
                case "AreaPlan": return ViewType.AreaPlan;
                case "StructuralPlan": return ViewType.EngineeringPlan;
                default: return null;
            }
        }

        private string ComputeNewName(string oldName)
        {
            switch (Operation)
            {
                case "prefix":
                    return Prefix + oldName;
                case "suffix":
                    return oldName + Suffix;
                case "find_replace":
                    if (!string.IsNullOrEmpty(FindText))
                        return oldName.Replace(FindText, ReplaceText ?? "");
                    return oldName;
                default:
                    return oldName;
            }
        }

        public string GetName() => "Rename Views";
    }
}
