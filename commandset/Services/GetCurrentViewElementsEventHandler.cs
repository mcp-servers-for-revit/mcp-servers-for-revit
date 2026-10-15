using Autodesk.Revit.UI;
using RevitMCPCommandSet.Models.Common;
using RevitMCPCommandSet.Utils;
using RevitMCPSDK.API.Interfaces;

namespace RevitMCPCommandSet.Services
{
    public class GetCurrentViewElementsEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        // 默认模型类别列表
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
        // 默认注释类别列表
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

        // 查询参数
        private List<string> _modelCategoryList;
        private List<string> _annotationCategoryList;
        private bool _includeHidden;
        private int _limit;

        // 执行结果
        public ViewElementsResult ResultInfo { get; private set; }

        // 状态同步对象
        public bool TaskCompleted { get; private set; }
        private readonly ManualResetEvent _resetEvent = new ManualResetEvent(false);

        // 设置查询参数
        public void SetQueryParameters(List<string> modelCategoryList, List<string> annotationCategoryList, bool includeHidden, int limit)
        {
            _modelCategoryList = modelCategoryList;
            _annotationCategoryList = annotationCategoryList;
            _includeHidden = includeHidden;
            _limit = limit;
            TaskCompleted = false;
            _resetEvent.Reset();
        }

        // 实现IWaitableExternalEventHandler接口
        public bool WaitForCompletion(int timeoutMilliseconds = 10000)
        {
            _resetEvent.Reset();
            return _resetEvent.WaitOne(timeoutMilliseconds);
        }

        public void Execute(UIApplication app)
        {
            try
            {
                var uiDoc = app.ActiveUIDocument;
                var doc = uiDoc.Document;
                var activeView = doc.ActiveView;


                // 合并所有类别
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

                // 获取当前视图中的所有元素
                var collector = new FilteredElementCollector(doc, activeView.Id)
                    .WhereElementIsNotElementType();

                // 获取所有元素
                IList<Element> elements = collector.ToElements();

                // 按类别筛选
                if (allCategories.Count > 0)
                {
                    // 转换字符串类别为枚举
                    List<BuiltInCategory> builtInCategories = new List<BuiltInCategory>();
                    foreach (string categoryName in allCategories)
                    {
                        if (Enum.TryParse(categoryName, out BuiltInCategory category))
                        {
                            builtInCategories.Add(category);
                        }
                    }
                    // 如果成功解析了类别，则使用类别过滤器
                    if (builtInCategories.Count > 0)
                    {
                        ElementMulticategoryFilter categoryFilter = new ElementMulticategoryFilter(builtInCategories);
                        elements = new FilteredElementCollector(doc, activeView.Id)
                            .WhereElementIsNotElementType()
                            .WherePasses(categoryFilter)
                            .ToElements();
                    }
                }

                // 过滤隐藏的元素
                if (!_includeHidden)
                {
                    elements = elements.Where(e => !e.IsHidden(activeView)).ToList();
                }

                // 限制返回数量
                if (_limit > 0 && elements.Count > _limit)
                {
                    elements = elements.Take(_limit).ToList();
                }

                // 构建结果
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
                    Properties = ElementProperties.GetBasic(e)
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
                    Elements = elementInfos
                };
            }
            // Exceptions propagate to RevitCommandBase, which reports them to the caller.
            finally
            {
                TaskCompleted = true;
                _resetEvent.Set();
            }
        }

        public string GetName()
        {
            return "获取当前视图元素";
        }
    }
}
