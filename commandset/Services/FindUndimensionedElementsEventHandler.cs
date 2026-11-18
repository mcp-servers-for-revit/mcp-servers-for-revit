using Autodesk.Revit.UI;
using RevitMCPSDK.API.Interfaces;

namespace RevitMCPCommandSet.Services
{
    public class FindUndimensionedElementsEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        private static readonly List<BuiltInCategory> DefaultCategories = new List<BuiltInCategory>
        {
            BuiltInCategory.OST_Walls,
            BuiltInCategory.OST_Doors,
            BuiltInCategory.OST_Windows,
            BuiltInCategory.OST_Columns,
            BuiltInCategory.OST_Grids,
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

                // Collect all dimensions in the view and find which elements they reference
                var dimensionCollector = new FilteredElementCollector(doc, targetView.Id)
                    .OfClass(typeof(Dimension))
                    .WhereElementIsNotElementType();

                var dimensionedElementIds = new HashSet<long>();
                foreach (Dimension dim in dimensionCollector)
                {
                    var refs = dim.References;
                    if (refs == null) continue;

                    foreach (Reference reference in refs)
                    {
                        var refElementId = reference.ElementId;
                        if (refElementId != ElementId.InvalidElementId)
                        {
#if REVIT2024_OR_GREATER
                            dimensionedElementIds.Add(refElementId.Value);
#else
                            dimensionedElementIds.Add(refElementId.IntegerValue);
#endif
                        }
                    }
                }

                // Find elements in target categories that are NOT dimensioned
                var undimensionedElements = new List<object>();
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
                        if (!dimensionedElementIds.Contains(elementIdValue))
                        {
                            if (undimensionedElements.Count <= Limit)
                            {
                                undimensionedElements.Add(new
                                {
                                    elementId = elementIdValue,
                                    name = element.Name,
                                    category = element.Category?.Name ?? "Unknown"
                                });
                            }
                        }
                    }
                }

                bool isTruncated = undimensionedElements.Count > Limit;
                var returnedElements = isTruncated
                    ? undimensionedElements.Take(Limit).ToList()
                    : undimensionedElements;

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
                    undimensionedCount = returnedElements.Count,
                    truncated = isTruncated,
                    undimensionedElements = returnedElements
                };
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Failed to find undimensioned elements: {ex.Message}";
            }
            finally
            {
                TaskCompleted = true;
                _resetEvent.Set();
            }
        }

        public string GetName() => "Find Undimensioned Elements";
    }
}
