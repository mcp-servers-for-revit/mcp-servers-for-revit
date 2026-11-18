using Autodesk.Revit.UI;
using RevitMCPCommandSet.Models.Common;
using RevitMCPSDK.API.Interfaces;

namespace RevitMCPCommandSet.Services
{
    public class GetCurrentViewElementsEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        // Default model category list
        private readonly List<string> _defaultModelCategories = new List<string>
        {
            "OST_Walls",
            "OST_Doors",
            "OST_Windows",
            "OST_Furniture",
            "OST_Columns",
            "OST_Floors",
            "OST_Roofs",
            "OST_Stairs",
            "OST_StructuralFraming",
            "OST_Ceilings",
            "OST_MEPSpaces",
            "OST_Rooms"
        };
        // Default annotation category list
        private readonly List<string> _defaultAnnotationCategories = new List<string>
        {
            "OST_Dimensions",
            "OST_TextNotes",
            "OST_GenericAnnotation",
            "OST_WallTags",
            "OST_DoorTags",
            "OST_WindowTags",
            "OST_RoomTags",
            "OST_AreaTags",
            "OST_SpaceTags",
            "OST_ViewportLabels",
            "OST_TitleBlocks"
        };

        // Query parameters
        private List<string> _modelCategoryList;
        private List<string> _annotationCategoryList;
        private bool _includeHidden;
        private int _limit;
        private List<string> _fields;

        // Execution result
        public ViewElementsResult ResultInfo { get; private set; }

        // Error message if execution fails
        public string ErrorMessage { get; private set; }

        // State synchronization object
        public bool TaskCompleted { get; private set; }
        private readonly ManualResetEvent _resetEvent = new ManualResetEvent(false);

        // Set query parameters
        public void SetQueryParameters(List<string> modelCategoryList, List<string> annotationCategoryList, bool includeHidden, int limit, List<string> fields = null)
        {
            _modelCategoryList = modelCategoryList;
            _annotationCategoryList = annotationCategoryList;
            _includeHidden = includeHidden;
            _limit = limit;
            _fields = fields;
            TaskCompleted = false;
            _resetEvent.Reset();
        }

        // Implement IWaitableExternalEventHandler interface
        public bool WaitForCompletion(int timeoutMilliseconds = 10000)
        {
            return _resetEvent.WaitOne(timeoutMilliseconds);
        }

        public void Execute(UIApplication app)
        {
            try
            {
                var uiDoc = app.ActiveUIDocument;
                var doc = uiDoc.Document;
                var activeView = doc.ActiveView;


                // Merge all categories
                List<string> allCategories = new List<string>();
                if (_modelCategoryList == null && _annotationCategoryList == null)
                {
                    allCategories.AddRange(_defaultModelCategories);
                    allCategories.AddRange(_defaultAnnotationCategories);
                }
                else
                {
                    allCategories.AddRange(_modelCategoryList ?? new List<string>());
                    allCategories.AddRange(_annotationCategoryList ?? new List<string>());
                }

                // Get all elements in the current view
                var collector = new FilteredElementCollector(doc, activeView.Id)
                    .WhereElementIsNotElementType();

                // Get all elements
                IList<Element> elements = collector.ToElements();

                // Filter by category
                if (allCategories.Count > 0)
                {
                    // Convert string categories to enum
                    List<BuiltInCategory> builtInCategories = new List<BuiltInCategory>();
                    foreach (string categoryName in allCategories)
                    {
                        if (Enum.TryParse(categoryName, out BuiltInCategory category))
                        {
                            builtInCategories.Add(category);
                        }
                    }
                    // If categories were successfully parsed, use category filter
                    if (builtInCategories.Count > 0)
                    {
                        ElementMulticategoryFilter categoryFilter = new ElementMulticategoryFilter(builtInCategories);
                        elements = new FilteredElementCollector(doc, activeView.Id)
                            .WhereElementIsNotElementType()
                            .WherePasses(categoryFilter)
                            .ToElements();
                    }
                }

                // Filter hidden elements
                if (!_includeHidden)
                {
                    elements = elements.Where(e => !e.IsHidden(activeView)).ToList();
                }

                // Limit return count
                int totalFiltered = elements.Count;
                bool truncated = false;
                if (_limit > 0 && elements.Count > _limit)
                {
                    elements = elements.Take(_limit).ToList();
                    truncated = true;
                }

                // Build result
                var elementInfos = elements.Select(e => new ElementInfo
                {
#if REVIT2024_OR_GREATER
                    Id = e.Id.Value,
#else
                    Id = e.Id.IntegerValue,
#endif
                    UniqueId = e.UniqueId,
                    Name = e.Name,
                    Category = e.Category?.Name ?? "unknow",
                    Properties = GetElementProperties(e, _fields)
                }).ToList();

                ResultInfo = new ViewElementsResult
                {
#if REVIT2024_OR_GREATER
                    ViewId = activeView.Id.Value,
#else
                    ViewId = activeView.Id.IntegerValue,
#endif
                    ViewName = activeView.Name,
                    TotalElementsInView = new FilteredElementCollector(doc, activeView.Id).GetElementCount(),
                    FilteredElementCount = elementInfos.Count,
                    Truncated = truncated,
                    Elements = elementInfos
                };
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Failed to get current view elements: {ex.Message}";
            }
            finally
            {
                TaskCompleted = true;
                _resetEvent.Set();
            }
        }

        private Dictionary<string, string> GetElementProperties(Element element, List<string> fields = null)
        {
            var properties = new Dictionary<string, string>();
            // When fields is provided, only include requested properties
            bool hasFilter = fields != null && fields.Count > 0;
            HashSet<string> requestedFields = hasFilter ? new HashSet<string>(fields, StringComparer.OrdinalIgnoreCase) : null;

            // Add common properties
            if (!hasFilter || requestedFields.Contains("ElementId"))
            {
#if REVIT2024_OR_GREATER
                properties.Add("ElementId", element.Id.Value.ToString());
#else
                properties.Add("ElementId", element.Id.IntegerValue.ToString());
#endif
            }

            if (element.Location != null)
            {
                if (element.Location is LocationPoint locationPoint)
                {
                    var point = locationPoint.Point;
                    if (!hasFilter || requestedFields.Contains("LocationX"))
                        properties.Add("LocationX", point.X.ToString("F2"));
                    if (!hasFilter || requestedFields.Contains("LocationY"))
                        properties.Add("LocationY", point.Y.ToString("F2"));
                    if (!hasFilter || requestedFields.Contains("LocationZ"))
                        properties.Add("LocationZ", point.Z.ToString("F2"));
                }
                else if (element.Location is LocationCurve locationCurve)
                {
                    var curve = locationCurve.Curve;
                    if (!hasFilter || requestedFields.Contains("StartX") || requestedFields.Contains("StartY") || requestedFields.Contains("StartZ"))
                    {
                        var startPt = curve.GetEndPoint(0);
                        if (!hasFilter || requestedFields.Contains("StartX"))
                            properties.Add("StartX", startPt.X.ToString("F2"));
                        if (!hasFilter || requestedFields.Contains("StartY"))
                            properties.Add("StartY", startPt.Y.ToString("F2"));
                        if (!hasFilter || requestedFields.Contains("StartZ"))
                            properties.Add("StartZ", startPt.Z.ToString("F2"));
                    }
                    if (!hasFilter || requestedFields.Contains("EndX") || requestedFields.Contains("EndY") || requestedFields.Contains("EndZ"))
                    {
                        var endPt = curve.GetEndPoint(1);
                        if (!hasFilter || requestedFields.Contains("EndX"))
                            properties.Add("EndX", endPt.X.ToString("F2"));
                        if (!hasFilter || requestedFields.Contains("EndY"))
                            properties.Add("EndY", endPt.Y.ToString("F2"));
                        if (!hasFilter || requestedFields.Contains("EndZ"))
                            properties.Add("EndZ", endPt.Z.ToString("F2"));
                    }
                    if (!hasFilter || requestedFields.Contains("Length"))
                        properties.Add("Length", curve.Length.ToString("F2"));
                }
            }

            // Get common parameter values
            var commonParams = new[] { "Comments", "Mark", "Level", "Family", "Type" };
            foreach (var paramName in commonParams)
            {
                if (hasFilter && !requestedFields.Contains(paramName))
                    continue;

                Parameter param = element.LookupParameter(paramName);
                if (param != null && !param.IsReadOnly)
                {
                    if (param.StorageType == StorageType.String)
                        properties.Add(paramName, param.AsString() ?? "");
                    else if (param.StorageType == StorageType.Double)
                        properties.Add(paramName, param.AsDouble().ToString("F2"));
                    else if (param.StorageType == StorageType.Integer)
                        properties.Add(paramName, param.AsInteger().ToString());
                    else if (param.StorageType == StorageType.ElementId)
#if REVIT2024_OR_GREATER
                        properties.Add(paramName, param.AsElementId().Value.ToString());
#else
                        properties.Add(paramName, param.AsElementId().IntegerValue.ToString());
#endif
                }
            }

            return properties;
        }

        public string GetName()
        {
            return "Get Current View Elements";
        }
    }
}
