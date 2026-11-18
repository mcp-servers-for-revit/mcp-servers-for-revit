using Autodesk.Revit.UI;
using RevitMCPCommandSet.Utils;
using RevitMCPSDK.API.Interfaces;

namespace RevitMCPCommandSet.Services
{
    public class FindUntaggedElementsEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        private static readonly List<BuiltInCategory> DefaultCategories = new List<BuiltInCategory>
        {
            BuiltInCategory.OST_Walls,
            BuiltInCategory.OST_Doors,
            BuiltInCategory.OST_Windows,
            BuiltInCategory.OST_Rooms,
            BuiltInCategory.OST_Floors,
            BuiltInCategory.OST_Columns,
            BuiltInCategory.OST_StructuralFraming
        };

        public List<string> Categories { get; set; }
        public int? ViewId { get; set; }
        public int Limit { get; set; } = 500;

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

                // Determine target view
                View targetView;
                if (ViewId.HasValue)
                {
#if REVIT2024_OR_GREATER
                    var viewElement = doc.GetElement(new ElementId((long)ViewId.Value));
#else
                    var viewElement = doc.GetElement(new ElementId((int)ViewId.Value));
#endif
                    targetView = viewElement as View ?? throw new Exception($"Element {ViewId.Value} is not a view");
                }
                else
                {
                    targetView = doc.ActiveView;
                }

                // Parse categories
                List<BuiltInCategory> builtInCategories;
                if (Categories != null && Categories.Count > 0)
                {
                    builtInCategories = new List<BuiltInCategory>();
                    foreach (var cat in Categories)
                    {
                        if (Enum.TryParse(cat, out BuiltInCategory bic))
                            builtInCategories.Add(bic);
                    }
                }
                else
                {
                    builtInCategories = DefaultCategories;
                }

                // Collect all tags in the view
                var tagCollector = new FilteredElementCollector(doc, targetView.Id)
                    .OfClass(typeof(IndependentTag))
                    .WhereElementIsNotElementType();

                var taggedElementIds = new HashSet<long>();
                foreach (IndependentTag tag in tagCollector)
                {
                    foreach (var id in tag.GetTaggedLocalElementIdsCompat())
                    {
                        if (id != ElementId.InvalidElementId)
#if REVIT2024_OR_GREATER
                            taggedElementIds.Add(id.Value);
#else
                            taggedElementIds.Add(id.IntegerValue);
#endif
                    }
                }

                // Also collect SpatialElementTags (room tags, space tags, area tags)
                var spatialTagCollector = new FilteredElementCollector(doc, targetView.Id)
                    .OfClass(typeof(SpatialElementTag))
                    .WhereElementIsNotElementType();

                foreach (SpatialElementTag spatialTag in spatialTagCollector)
                {
                    Element taggedElement = null;
                    if (spatialTag is Autodesk.Revit.DB.Architecture.RoomTag roomTag)
                        taggedElement = roomTag.Room;
                    else if (spatialTag is Autodesk.Revit.DB.Mechanical.SpaceTag spaceTag)
                        taggedElement = spaceTag.Space;
                    else if (spatialTag is Autodesk.Revit.DB.AreaTag areaTag)
                        taggedElement = areaTag.Area;

                    if (taggedElement != null)
                    {
#if REVIT2024_OR_GREATER
                        taggedElementIds.Add(taggedElement.Id.Value);
#else
                        taggedElementIds.Add(taggedElement.Id.IntegerValue);
#endif
                    }
                }

                // Collect elements in target categories that are NOT tagged
                var untaggedElements = new List<object>();
                int totalChecked = 0;

                foreach (var category in builtInCategories)
                {
                    var elementCollector = new FilteredElementCollector(doc, targetView.Id)
                        .OfCategory(category)
                        .WhereElementIsNotElementType();

                    foreach (var element in elementCollector)
                    {
                        totalChecked++;
#if REVIT2024_OR_GREATER
                        long elementIdValue = element.Id.Value;
#else
                        long elementIdValue = element.Id.IntegerValue;
#endif
                        if (!taggedElementIds.Contains(elementIdValue))
                        {
                            if (untaggedElements.Count <= Limit)
                            {
                                untaggedElements.Add(new
                                {
                                    elementId = elementIdValue,
                                    name = element.Name,
                                    category = element.Category?.Name ?? "Unknown"
                                });
                            }
                        }
                    }
                }

                bool isTruncated = untaggedElements.Count > Limit;
                var returnedElements = isTruncated
                    ? untaggedElements.Take(Limit).ToList()
                    : untaggedElements;

                Result = new
                {
                    viewId =
#if REVIT2024_OR_GREATER
                        targetView.Id.Value,
#else
                        targetView.Id.IntegerValue,
#endif
                    viewName = targetView.Name,
                    totalElementsChecked = totalChecked,
                    untaggedCount = returnedElements.Count,
                    truncated = isTruncated,
                    untaggedElements = returnedElements
                };
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Failed to find untagged elements: {ex.Message}";
            }
            finally
            {
                TaskCompleted = true;
                _resetEvent.Set();
            }
        }

        public string GetName() => "Find Untagged Elements";
    }
}
