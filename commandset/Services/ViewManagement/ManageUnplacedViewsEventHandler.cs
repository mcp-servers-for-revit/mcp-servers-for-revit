using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCPCommandSet.Models.Common;
using RevitMCPSDK.API.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace RevitMCPCommandSet.Services.ViewManagement
{
    public class ManageUnplacedViewsEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        private readonly ManualResetEvent _resetEvent = new ManualResetEvent(false);

        public string Action { get; private set; } = "list";
        public List<string> ViewTypes { get; private set; } = new List<string>();
        public string FilterName { get; private set; } = "";
        public List<string> ExcludeNames { get; private set; } = new List<string>();
        public bool DryRun { get; private set; } = true;
        public int MaxResults { get; private set; } = 500;

        public AIResult<object> Result { get; private set; }

        public void SetParameters(string action, List<string> viewTypes, string filterName,
            List<string> excludeNames, bool dryRun, int maxResults)
        {
            Action = action ?? "list";
            ViewTypes = viewTypes ?? new List<string>();
            FilterName = filterName ?? "";
            ExcludeNames = excludeNames ?? new List<string>();
            DryRun = dryRun;
            MaxResults = maxResults > 0 ? maxResults : 500;
            _resetEvent.Reset();
        }

        public bool WaitForCompletion(int timeoutMilliseconds = 30000)
        {
            return _resetEvent.WaitOne(timeoutMilliseconds);
        }

        private static readonly Dictionary<string, ViewType> ViewTypeMap = new Dictionary<string, ViewType>(StringComparer.OrdinalIgnoreCase)
        {
            { "FloorPlan", ViewType.FloorPlan },
            { "CeilingPlan", ViewType.CeilingPlan },
            { "Section", ViewType.Section },
            { "Elevation", ViewType.Elevation },
            { "ThreeDimensional", ViewType.ThreeD },
            { "DraftingView", ViewType.DraftingView },
            { "Legend", ViewType.Legend },
            { "AreaPlan", ViewType.AreaPlan },
            { "StructuralPlan", ViewType.FloorPlan },
            { "Detail", ViewType.Detail },
            { "Rendering", ViewType.Rendering },
            { "Walkthrough", ViewType.Walkthrough },
        };

        public void Execute(UIApplication app)
        {
            try
            {
                var doc = app.ActiveUIDocument.Document;

                // Get all view IDs that are placed on sheets
                var placedViewIds = new HashSet<ElementId>(
                    new FilteredElementCollector(doc)
                        .OfClass(typeof(Viewport))
                        .Cast<Viewport>()
                        .Select(vp => vp.ViewId)
                );

                // Get all views, excluding templates and browser-organization views
                var allViews = new FilteredElementCollector(doc)
                    .OfClass(typeof(View))
                    .Cast<View>()
                    .Where(v => !v.IsTemplate
                        && v.ViewType != ViewType.ProjectBrowser
                        && v.ViewType != ViewType.SystemBrowser
                        && v.ViewType != ViewType.Internal
                        && v.ViewType != ViewType.Undefined
                        && v.ViewType != ViewType.DrawingSheet)
                    .ToList();

                // Filter to unplaced views only
                var unplacedViews = allViews.Where(v => !placedViewIds.Contains(v.Id)).ToList();

                // Apply view type filter
                if (ViewTypes.Count > 0)
                {
                    var allowedTypes = new HashSet<ViewType>();
                    foreach (var vt in ViewTypes)
                    {
                        if (ViewTypeMap.TryGetValue(vt, out var mapped))
                            allowedTypes.Add(mapped);
                    }
                    unplacedViews = unplacedViews.Where(v => allowedTypes.Contains(v.ViewType)).ToList();
                }

                // Apply name filter
                if (!string.IsNullOrEmpty(FilterName))
                {
                    unplacedViews = unplacedViews
                        .Where(v => v.Name.IndexOf(FilterName, StringComparison.OrdinalIgnoreCase) >= 0)
                        .ToList();
                }

                // Apply name exclusions
                if (ExcludeNames.Count > 0)
                {
                    unplacedViews = unplacedViews
                        .Where(v => !ExcludeNames.Any(ex =>
                            v.Name.IndexOf(ex, StringComparison.OrdinalIgnoreCase) >= 0))
                        .ToList();
                }

                int totalUnplaced = unplacedViews.Count;

                // Cap results for response
                var viewsToReport = unplacedViews.Take(MaxResults).ToList();

                if (Action.Equals("list", StringComparison.OrdinalIgnoreCase))
                {
                    ExecuteList(viewsToReport, totalUnplaced);
                }
                else if (Action.Equals("delete", StringComparison.OrdinalIgnoreCase))
                {
                    ExecuteDelete(doc, viewsToReport, totalUnplaced);
                }
                else
                {
                    Result = new AIResult<object> { Success = false, Message = $"Unknown action: {Action}. Use 'list' or 'delete'." };
                }
            }
            catch (Exception ex)
            {
                Result = new AIResult<object> { Success = false, Message = $"Manage unplaced views failed: {ex.Message}" };
            }
            finally
            {
                _resetEvent.Set();
            }
        }

        private void ExecuteList(List<View> views, int totalUnplaced)
        {
            var viewData = views.Select(v => new
            {
#if REVIT2024_OR_GREATER
                viewId = v.Id.Value,
#else
                viewId = v.Id.IntegerValue,
#endif
                name = v.Name,
                viewType = v.ViewType.ToString(),
                levelName = (v as ViewPlan)?.GenLevel?.Name ?? ""
            }).ToList();

            Result = new AIResult<object>
            {
                Success = true,
                Message = $"Found {totalUnplaced} unplaced views" + (totalUnplaced > views.Count ? $" (showing first {views.Count})" : ""),
                Response = new
                {
                    action = "list",
                    totalUnplaced,
                    returned = viewData.Count,
                    views = viewData
                }
            };
        }

        private void ExecuteDelete(Document doc, List<View> views, int totalUnplaced)
        {
            if (DryRun)
            {
                var preview = views.Select(v => new
                {
#if REVIT2024_OR_GREATER
                    viewId = v.Id.Value,
#else
                    viewId = v.Id.IntegerValue,
#endif
                    name = v.Name,
                    viewType = v.ViewType.ToString()
                }).ToList();

                Result = new AIResult<object>
                {
                    Success = true,
                    Message = $"⚠ DRY RUN: {totalUnplaced} unplaced views WOULD be deleted. Set dryRun=false to actually delete. This action cannot be undone.",
                    Response = new
                    {
                        action = "delete",
                        dryRun = true,
                        totalWouldDelete = totalUnplaced,
                        returned = preview.Count,
                        views = preview
                    }
                };
                return;
            }

            // Actual deletion
            int deleted = 0;
            int failed = 0;
            var results = new List<object>();

            using (var transaction = new Transaction(doc, "Delete Unplaced Views"))
            {
                transaction.Start();
                try
                {
                    foreach (var view in views)
                    {
                        try
                        {
                            string viewName = view.Name;
                            string viewType = view.ViewType.ToString();
#if REVIT2024_OR_GREATER
                            long viewId = view.Id.Value;
#else
                            long viewId = view.Id.IntegerValue;
#endif
                            doc.Delete(view.Id);
                            deleted++;
                            results.Add(new { viewId, name = viewName, viewType, success = true });
                        }
                        catch (Exception ex)
                        {
                            failed++;
                            results.Add(new
                            {
#if REVIT2024_OR_GREATER
                                viewId = view.Id.Value,
#else
                                viewId = view.Id.IntegerValue,
#endif
                                name = view.Name,
                                viewType = view.ViewType.ToString(),
                                success = false,
                                message = ex.Message
                            });
                        }
                    }

                    transaction.Commit();
                }
                catch
                {
                    if (transaction.GetStatus() == TransactionStatus.Started)
                        transaction.RollBack();
                    throw;
                }
            }

            Result = new AIResult<object>
            {
                Success = deleted > 0,
                Message = $"Deleted {deleted} unplaced views" + (failed > 0 ? $", {failed} failed" : ""),
                Response = new
                {
                    action = "delete",
                    dryRun = false,
                    totalDeleted = deleted,
                    totalFailed = failed,
                    results
                }
            };
        }

        public string GetName() => "Manage Unplaced Views";
    }
}
