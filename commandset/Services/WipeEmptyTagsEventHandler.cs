using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCPSDK.API.Interfaces;
using RevitMCPCommandSet.Helpers;
using RevitMCPCommandSet.Utils;

namespace RevitMCPCommandSet.Services
{
    public class WipeEmptyTagsEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        public bool DryRun { get; set; } = true;
        public int? ViewId { get; set; }
        public List<string> Categories { get; set; }

        public object Result { get; private set; }
        public string ErrorMessage { get; private set; }
        public bool TaskCompleted { get; private set; }
        private readonly ManualResetEvent _resetEvent = new ManualResetEvent(false);

        public bool WaitForCompletion(int timeoutMilliseconds = 10000)
        {
            return _resetEvent.WaitOne(timeoutMilliseconds);
        }

        public void Execute(UIApplication app)
        {
            try
            {
                _resetEvent.Reset();
                TaskCompleted = false;
                ErrorMessage = null;

                var doc = app.ActiveUIDocument.Document;

                // Determine scope: specific view or entire document
                FilteredElementCollector tagCollector;
                string scopeDescription;

                if (ViewId.HasValue)
                {
#if REVIT2024_OR_GREATER
                    var viewElement = doc.GetElement(new ElementId((long)ViewId.Value));
#else
                    var viewElement = doc.GetElement(new ElementId((int)ViewId.Value));
#endif
                    var targetView = viewElement as View ?? throw new Exception($"Element {ViewId.Value} is not a view");
                    tagCollector = new FilteredElementCollector(doc, targetView.Id);
                    scopeDescription = $"View: {targetView.Name}";
                }
                else
                {
                    tagCollector = new FilteredElementCollector(doc);
                    scopeDescription = "Entire document";
                }

                // Collect IndependentTags
                var tags = tagCollector
                    .OfClass(typeof(IndependentTag))
                    .WhereElementIsNotElementType()
                    .Cast<IndependentTag>()
                    .ToList();

                // Filter by categories if specified (language-independent via BuiltInCategory)
                if (Categories != null && Categories.Count > 0)
                {
                    var catIds = new HashSet<long>();
                    foreach (var cat in Categories)
                    {
                        if (Enum.TryParse(cat, out BuiltInCategory bic))
                        {
#if REVIT2024_OR_GREATER
                            catIds.Add(new ElementId(bic).Value);
#else
                            catIds.Add((long)new ElementId(bic).IntegerValue);
#endif
                        }
                    }
                    if (catIds.Count > 0)
                    {
                        tags = tags.Where(t =>
                        {
                            if (t.Category == null) return false;
#if REVIT2024_OR_GREATER
                            return catIds.Contains(t.Category.Id.Value);
#else
                            return catIds.Contains(t.Category.Id.IntegerValue);
#endif
                        }).ToList();
                    }
                }

                // Find empty tags: tags whose TagText is empty or whose host element is deleted
                var emptyTags = new List<object>();
                var emptyTagIds = new List<ElementId>();

                foreach (var tag in tags)
                {
                    bool isEmpty = false;
                    string reason = "";

                    try
                    {
                        // Check if the tag has a valid host
                        var localIds = tag.GetTaggedLocalElementIdsCompat();
                        if (localIds == null || localIds.Count == 0)
                        {
                            isEmpty = true;
                            reason = "No tagged references";
                        }
                        else
                        {
                            // Check if all referenced elements are valid
                            bool allInvalid = true;
                            foreach (var id in localIds)
                            {
                                if (id != ElementId.InvalidElementId && doc.GetElement(id) != null)
                                {
                                    allInvalid = false;
                                    break;
                                }
                            }
                            if (allInvalid)
                            {
                                isEmpty = true;
                                reason = "All tagged elements are invalid";
                            }
                        }

                        // Check if tag text is empty
                        if (!isEmpty)
                        {
                            var tagText = tag.TagText;
                            if (string.IsNullOrWhiteSpace(tagText))
                            {
                                isEmpty = true;
                                reason = "Tag text is empty";
                            }
                        }
                    }
                    catch
                    {
                        isEmpty = true;
                        reason = "Error reading tag properties";
                    }

                    if (isEmpty)
                    {
                        emptyTagIds.Add(tag.Id);
                        emptyTags.Add(new
                        {
                            tagId =
#if REVIT2024_OR_GREATER
                                tag.Id.Value,
#else
                                tag.Id.IntegerValue,
#endif
                            category = tag.Category?.Name ?? "Unknown",
                            reason
                        });
                    }
                }

                int deletedCount = 0;

                if (!DryRun && emptyTagIds.Count > 0)
                {
                    if (!ConfirmationHelper.Confirm("delete", emptyTagIds.Count))
                    {
                        Result = new
                        {
                            scope = scopeDescription,
                            dryRun = DryRun,
                            totalTagsScanned = tags.Count,
                            emptyTagsFound = emptyTags.Count,
                            deletedCount = 0,
                            cancelled = true,
                            emptyTags
                        };
                        return;
                    }

                    using (var tx = new Transaction(doc, "Wipe Empty Tags"))
                    {
                        tx.Start();
                        foreach (var id in emptyTagIds)
                        {
                            try
                            {
                                doc.Delete(id);
                                deletedCount++;
                            }
                            catch
                            {
                                // Skip tags that can't be deleted
                            }
                        }
                        tx.Commit();
                    }
                }

                Result = new
                {
                    scope = scopeDescription,
                    dryRun = DryRun,
                    totalTagsScanned = tags.Count,
                    emptyTagsFound = emptyTags.Count,
                    deletedCount = DryRun ? 0 : deletedCount,
                    emptyTags
                };
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Failed to wipe empty tags: {ex.Message}";
            }
            finally
            {
                TaskCompleted = true;
                _resetEvent.Set();
            }
        }

        public string GetName() => "Wipe Empty Tags";
    }
}
