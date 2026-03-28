using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using RevitMCPSDK.API.Interfaces;
using RevitMCPCommandSet.Models.Common;
using RevitMCPCommandSet.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Xml;

namespace RevitMCPCommandSet.Services
{
    public class AIElementFilterEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        private UIApplication uiApp;
        private UIDocument uiDoc => uiApp.ActiveUIDocument;
        private Document doc => uiDoc.Document;
        private Autodesk.Revit.ApplicationServices.Application app => uiApp.Application;
        /// <summary>
        /// 事件等待对象
        /// </summary>
        private readonly ManualResetEvent _resetEvent = new ManualResetEvent(false);
        /// <summary>
        /// 创建数据（传入数据）
        /// </summary>
        public FilterSetting FilterSetting { get; private set; }
        /// <summary>
        /// 执行结果（传出数据）
        /// </summary>
        public AIResult<List<object>> Result { get; private set; }

        /// <summary>
        /// 设置创建的参数
        /// </summary>
        public void SetParameters(FilterSetting data)
        {
            FilterSetting = data;
            _resetEvent.Reset();
        }
        public void Execute(UIApplication uiapp)
        {
            uiApp = uiapp;

            try
            {
                var elementInfoList = new List<object>();
                // 检查过滤器设置是否有效
                if (!FilterSetting.Validate(out string errorMessage))
                    throw new Exception(errorMessage);
                // 获取指定条件元素的Id
                var elementList = GetFilteredElements(doc, FilterSetting);
                if (elementList == null || !elementList.Any())
                    throw new Exception("未在项目中找到指定元素，请检查过滤器设置是否正确");
                // 过滤器最大个数限制
                string message = "";
                if (FilterSetting.MaxElements > 0)
                {
                    if (elementList.Count > FilterSetting.MaxElements)
                    {
                        elementList = elementList.Take(FilterSetting.MaxElements).ToList();
                        message = $"。此外，符合过滤条件的共有 {elementList.Count} 个元素，仅显示前 {FilterSetting.MaxElements} 个";
                    }
                }

                // 获取指定Id元素的信息
                elementInfoList = GetElementFullInfo(doc, elementList);

                Result = new AIResult<List<object>>
                {
                    Success = true,
                    Message = $"成功获取{elementInfoList.Count}个元素信息，具体信息储存在Response属性中"+ message,
                    Response = elementInfoList,
                };
            }
            catch (Exception ex)
            {
                Result = new AIResult<List<object>>
                {
                    Success = false,
                    Message = $"获取元素信息时出错: {ex.Message}",
                };
            }
            finally
            {
                _resetEvent.Set(); // 通知等待线程操作已完成
            }
        }

        /// <summary>
        /// 等待创建完成
        /// </summary>
        /// <param name="timeoutMilliseconds">超时时间（毫秒）</param>
        /// <returns>操作是否在超时前完成</returns>
        public bool WaitForCompletion(int timeoutMilliseconds = 10000)
        {
            _resetEvent.Reset();
            return _resetEvent.WaitOne(timeoutMilliseconds);
        }

        /// <summary>
        /// IExternalEventHandler.GetName 实现
        /// </summary>
        public string GetName()
        {
            return "获取元素信息";
        }

        /// <summary>
        /// 根据过滤器设置获取Revit文档中符合条件的元素，支持多条件组合过滤
        /// </summary>
        /// <param name="doc">Revit文档</param>
        /// <param name="settings">过滤器设置</param>
        /// <returns>符合所有过滤条件的元素集合</returns>
        public static IList<Element> GetFilteredElements(Document doc, FilterSetting settings)
        {
            if (doc == null)
                throw new ArgumentNullException(nameof(doc));
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));
            // 验证过滤器设置
            if (!settings.Validate(out string errorMessage))
            {
                System.Diagnostics.Trace.WriteLine($"过滤器设置无效: {errorMessage}");
                return new List<Element>();
            }
            // 记录过滤条件应用情况
            List<string> appliedFilters = new List<string>();
            List<Element> result = new List<Element>();
            // 如果同时包含类型和实例，需要分别过滤再合并结果
            if (settings.IncludeTypes && settings.IncludeInstances)
            {
                // 收集类型元素
                result.AddRange(GetElementsByKind(doc, settings, true, appliedFilters));

                // 收集实例元素
                result.AddRange(GetElementsByKind(doc, settings, false, appliedFilters));
            }
            else if (settings.IncludeInstances)
            {
                // 仅收集实例元素
                result = GetElementsByKind(doc, settings, false, appliedFilters);
            }
            else if (settings.IncludeTypes)
            {
                // 仅收集类型元素
                result = GetElementsByKind(doc, settings, true, appliedFilters);
            }

            // 输出应用的过滤器信息
            if (appliedFilters.Count > 0)
            {
                System.Diagnostics.Trace.WriteLine($"已应用 {appliedFilters.Count} 个过滤条件: {string.Join(", ", appliedFilters)}");
                System.Diagnostics.Trace.WriteLine($"最终筛选结果: 共找到 {result.Count} 个元素");
            }
            return result;

        }

        /// <summary>
        /// 根据元素种类(类型或实例)获取满足过滤条件的元素
        /// </summary>
        private static List<Element> GetElementsByKind(Document doc, FilterSetting settings, bool isElementType, List<string> appliedFilters)
        {
            // 创建基础的FilteredElementCollector
            FilteredElementCollector collector;
            // 检查是否需要过滤当前视图可见的元素 (仅适用于实例元素)
            if (!isElementType && settings.FilterVisibleInCurrentView && doc.ActiveView != null)
            {
                collector = new FilteredElementCollector(doc, doc.ActiveView.Id);
                appliedFilters.Add("当前视图可见元素");
            }
            else
            {
                collector = new FilteredElementCollector(doc);
            }
            // 根据元素种类过滤
            if (isElementType)
            {
                collector = collector.WhereElementIsElementType();
                appliedFilters.Add("仅元素类型");
            }
            else
            {
                collector = collector.WhereElementIsNotElementType();
                appliedFilters.Add("仅元素实例");
            }
            // 创建过滤器列表
            List<ElementFilter> filters = new List<ElementFilter>();
            // 1. 类别过滤器
            if (!string.IsNullOrWhiteSpace(settings.FilterCategory))
            {
                BuiltInCategory category;
                if (!Enum.TryParse(settings.FilterCategory, true, out category))
                {
                    throw new ArgumentException($"无法将 '{settings.FilterCategory}' 转换为有效的Revit类别。");
                }
                ElementCategoryFilter categoryFilter = new ElementCategoryFilter(category);
                filters.Add(categoryFilter);
                appliedFilters.Add($"类别：{settings.FilterCategory}");
            }
            // 2. 元素类型过滤器
            if (!string.IsNullOrWhiteSpace(settings.FilterElementType))
            {

                Type elementType = null;
                // 尝试解析类型名称的各种可能形式
                string[] possibleTypeNames = new string[]
                {
                    settings.FilterElementType,                                    // 原始输入
                    $"Autodesk.Revit.DB.{settings.FilterElementType}, RevitAPI",  // Revit API命名空间
                    $"{settings.FilterElementType}, RevitAPI"                      // 完整限定带程序集
                };
                foreach (string typeName in possibleTypeNames)
                {
                    elementType = Type.GetType(typeName);
                    if (elementType != null)
                        break;
                }
                if (elementType != null)
                {
                    ElementClassFilter classFilter = new ElementClassFilter(elementType);
                    filters.Add(classFilter);
                    appliedFilters.Add($"元素类型：{elementType.Name}");
                }
                else
                {
                    throw new Exception($"警告：无法找到类型 '{settings.FilterElementType}'");
                }
            }
            // 3. 族符号过滤器 (仅适用于元素实例)
            if (!isElementType && settings.FilterFamilySymbolId > 0)
            {
                ElementId symbolId = new ElementId(settings.FilterFamilySymbolId);
                // 检查元素是否存在且是族类型
                Element symbolElement = doc.GetElement(symbolId);
                if (symbolElement != null && symbolElement is FamilySymbol)
                {
                    FamilyInstanceFilter familyFilter = new FamilyInstanceFilter(doc, symbolId);
                    filters.Add(familyFilter);
                    // 添加更详细的族信息日志
                    FamilySymbol symbol = symbolElement as FamilySymbol;
                    string familyName = symbol.Family?.Name ?? "未知族";
                    string symbolName = symbol.Name ?? "未知类型";
                    appliedFilters.Add($"族类型：{familyName} - {symbolName} (ID: {settings.FilterFamilySymbolId})");
                }
                else
                {
                    string elementType = symbolElement != null ? symbolElement.GetType().Name : "不存在";
                    System.Diagnostics.Trace.WriteLine($"警告：ID为 {settings.FilterFamilySymbolId} 的元素{(symbolElement == null ? "不存在" : "不是有效的FamilySymbol")} (实际类型: {elementType})");
                }
            }
            // 4. 空间范围过滤器
            if (settings.BoundingBoxMin != null && settings.BoundingBoxMax != null)
            {
                // 转换为Revit的XYZ坐标 (毫米转内部单位)
                XYZ minXYZ = JZPoint.ToXYZ(settings.BoundingBoxMin);
                XYZ maxXYZ = JZPoint.ToXYZ(settings.BoundingBoxMax);
                // 创建空间范围Outline对象
                Outline outline = new Outline(minXYZ, maxXYZ);
                // 创建相交过滤器
                BoundingBoxIntersectsFilter boundingBoxFilter = new BoundingBoxIntersectsFilter(outline);
                filters.Add(boundingBoxFilter);
                appliedFilters.Add($"空间范围过滤：Min({settings.BoundingBoxMin.X:F2}, {settings.BoundingBoxMin.Y:F2}, {settings.BoundingBoxMin.Z:F2}), " +
                                  $"Max({settings.BoundingBoxMax.X:F2}, {settings.BoundingBoxMax.Y:F2}, {settings.BoundingBoxMax.Z:F2}) mm");
            }
            // 应用组合过滤器
            if (filters.Count > 0)
            {
                ElementFilter combinedFilter = filters.Count == 1
                    ? filters[0]
                    : new LogicalAndFilter(filters);
                collector = collector.WherePasses(combinedFilter);
                if (filters.Count > 1)
                {
                    System.Diagnostics.Trace.WriteLine($"应用了{filters.Count}个过滤条件的组合过滤器 (逻辑AND关系)");
                }
            }
            return collector.ToElements().ToList();
        }

        /// <summary>
        /// 获取模型元素信息
        /// </summary>
        public static List<object> GetElementFullInfo(Document doc, IList<Element> elementCollector)
        {
            List<object> infoList = new List<object>();

            // 获取并处理元素
            foreach (var element in elementCollector)
            {
                // 判断是否为实体模型元素
                // 获取元素实例信息
                if (element?.Category?.HasMaterialQuantities ?? false)
                {
                    var info = CreateElementFullInfo(doc, element);
                    if (info != null)
                    {
                        infoList.Add(info);
                    }
                }
                // 获取元素类型信息
                else if (element is ElementType elementType)
                {
                    var info = CreateTypeFullInfo(doc, elementType);
                    if (info != null)
                    {
                        infoList.Add(info);
                    }
                }
                // 3. 空间定位元素 (高频)
                else if (element is Level || element is Grid)
                {
                    var info = CreatePositioningElementInfo(doc, element);
                    if (info != null)
                    {
                        infoList.Add(info);
                    }
                }
                // 4. 空间元素 (中高频)
                else if (element is SpatialElement) // Room, Area等
                {
                    var info = CreateSpatialElementInfo(doc, element);
                    if (info != null)
                    {
                        infoList.Add(info);
                    }
                }
                // 5. 视图元素 (高频)
                else if (element is View)
                {
                    var info = CreateViewInfo(doc, element);
                    if (info != null)
                    {
                        infoList.Add(info);
                    }
                }
                // 6. 注释元素 (中频)
                else if (element is TextNote || element is Dimension ||
                         element is IndependentTag || element is AnnotationSymbol ||
                         element is SpotDimension)
                {
                    var info = CreateAnnotationInfo(doc, element);
                    if (info != null)
                    {
                        infoList.Add(info);
                    }
                }
                // 7. 处理组和链接
                else if (element is Group || element is RevitLinkInstance)
                {
                    var info = CreateGroupOrLinkInfo(doc, element);
                    if (info != null)
                    {
                        infoList.Add(info);
                    }
                }
                // 8. 获取元素基本信息(兜底处理)
                else
                {
                    var info = CreateElementBasicInfo(doc, element);
                    if (info != null)
                    {
                        infoList.Add(info);
                    }
                }
            }

            return infoList;
        }

        /// <summary>
        /// 创建单个元素完整的ElementInfo对象
        /// </summary>
        public static ElementInstanceInfo CreateElementFullInfo(Document doc, Element element)
        {
            try
            {
                if (element?.Category == null)
                    return null;

                ElementInstanceInfo elementInfo = new ElementInstanceInfo();        //创建存储元素完整信息的自定义类
                // ID
                elementInfo.Id = element.Id.GetIntValue();
                // UniqueId
                elementInfo.UniqueId = element.UniqueId;
                // 类型名称
                elementInfo.Name = element.Name;
                // 族名称
                elementInfo.FamilyName = element?.get_Parameter(BuiltInParameter.ELEM_FAMILY_PARAM)?.AsValueString();
                // 类别
                elementInfo.Category = element.Category.Name;
                // 内置类别
                elementInfo.BuiltInCategory = Enum.GetName(typeof(BuiltInCategory), element.Category.Id.GetIntValue());
                // 类型Id
                elementInfo.TypeId = element.GetTypeId().GetIntValue();
                //所属房间Id  
                if (element is FamilyInstance instance)
                    elementInfo.RoomId = instance.Room?.Id.GetIntValue() ?? -1;
                // 标高
                elementInfo.Level = GetElementLevel(doc, element);
                // 最大包围盒
                BoundingBoxInfo boundingBoxInfo = new BoundingBoxInfo();
                elementInfo.BoundingBox = GetBoundingBoxInfo(element);
                // 参数
                //elementInfo.Parameters = GetDimensionParameters(element);
                ParameterInfo thicknessParam = GetThicknessInfo(element);      //厚度参数
                if (thicknessParam != null)
                {
                    elementInfo.Parameters.Add(thicknessParam);
                }
                ParameterInfo heightParam = GetBoundingBoxHeight(elementInfo.BoundingBox);      //高度参数
                if (heightParam != null)
                {
                    elementInfo.Parameters.Add(heightParam);
                }

                return elementInfo;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 创建单个类型完整的TypeFullInfo对象
        /// </summary>
        /// <param name="doc"></param>
        /// <param name="elementType"></param>
        /// <returns></returns>
        public static ElementTypeInfo CreateTypeFullInfo(Document doc, ElementType elementType)
        {
            ElementTypeInfo typeInfo = new ElementTypeInfo();
            // Id
            typeInfo.Id = elementType.Id.GetIntValue();
            // UniqueId
            typeInfo.UniqueId = elementType.UniqueId;
            // 类型名称
            typeInfo.Name = elementType.Name;
            // 族名称
            typeInfo.FamilyName = elementType.FamilyName;
            // 类别
            typeInfo.Category = elementType.Category.Name;
            // 内置类别
            typeInfo.BuiltInCategory = Enum.GetName(typeof(BuiltInCategory), elementType.Category.Id.GetIntValue());
            // 参数字典
            typeInfo.Parameters = GetDimensionParameters(elementType);
            ParameterInfo thicknessParam = GetThicknessInfo(elementType);      //厚度参数
            if (thicknessParam != null)
            {
                typeInfo.Parameters.Add(thicknessParam);
            }
            return typeInfo;
        }

        /// <summary>
        /// 创建空间定位元素的信息
        /// </summary>
        public static PositioningElementInfo CreatePositioningElementInfo(Document doc, Element element)
        {
            try
            {
                if (element == null)
                    return null;
                PositioningElementInfo info = new PositioningElementInfo
                {
                    Id = element.Id.GetIntValue(),
                    UniqueId = element.UniqueId,
                    Name = element.Name,
                    FamilyName = element?.get_Parameter(BuiltInParameter.ELEM_FAMILY_PARAM)?.AsValueString(),
                    Category = element.Category?.Name,
                    BuiltInCategory = element.Category != null ?
                        Enum.GetName(typeof(BuiltInCategory), element.Category.Id.GetIntValue()) : null,
                    ElementClass = element.GetType().Name,
                    BoundingBox = GetBoundingBoxInfo(element)
                };

                // 处理标高
                if (element is Level level)
                {
                    // 转换为mm
                    info.Elevation = level.Elevation * 304.8;
                }
                // 处理轴网
                else if (element is Grid grid)
                {
                    Curve curve = grid.Curve;
                    if (curve != null)
                    {
                        XYZ start = curve.GetEndPoint(0);
                        XYZ end = curve.GetEndPoint(1);
                        // 创建JZLine（转换为mm）
                        info.GridLine = new JZLine(
                            start.X * 304.8, start.Y * 304.8, start.Z * 304.8,
                            end.X * 304.8, end.Y * 304.8, end.Z * 304.8);
                    }
                }

                // 获取标高信息
                info.Level = GetElementLevel(doc, element);

                return info;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"创建空间定位元素信息时出错: {ex.Message}");
                return null;
            }
        }
        /// <summary>
        /// 创建空间元素的信息
        /// </summary>
        public static SpatialElementInfo CreateSpatialElementInfo(Document doc, Element element)
        {
            try
            {
                if (element == null || !(element is SpatialElement))
                    return null;
                SpatialElement spatialElement = element as SpatialElement;
                SpatialElementInfo info = new SpatialElementInfo
                {
                    Id = element.Id.GetIntValue(),
                    UniqueId = element.UniqueId,
                    Name = element.Name,
                    FamilyName = element?.get_Parameter(BuiltInParameter.ELEM_FAMILY_PARAM)?.AsValueString(),
                    Category = element.Category?.Name,
                    BuiltInCategory = element.Category != null ?
                        Enum.GetName(typeof(BuiltInCategory), element.Category.Id.GetIntValue()) : null,
                    ElementClass = element.GetType().Name,
                    BoundingBox = GetBoundingBoxInfo(element)
                };

                // 获取房间或区域的编号
                if (element is Room room)
                {
                    info.Number = room.Number;
                    // 转换为mm³
                    info.Volume = room.Volume * Math.Pow(304.8, 3);
                }
                else if (element is Area area)
                {
                    info.Number = area.Number;
                }

                // 获取面积
                Parameter areaParam = element.get_Parameter(BuiltInParameter.ROOM_AREA);
                if (areaParam != null && areaParam.HasValue)
                {
                    // 转换为mm²
                    info.Area = areaParam.AsDouble() * Math.Pow(304.8, 2);
                }

                // 获取周长
                Parameter perimeterParam = element.get_Parameter(BuiltInParameter.ROOM_PERIMETER);
                if (perimeterParam != null && perimeterParam.HasValue)
                {
                    // 转换为mm
                    info.Perimeter = perimeterParam.AsDouble() * 304.8;
                }

                // 获取标高
                info.Level = GetElementLevel(doc, element);

                return info;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"创建空间元素信息时出错: {ex.Message}");
                return null;
            }
        }
        /// <summary>
        /// 创建视图元素的信息
        /// </summary>
        public static ViewInfo CreateViewInfo(Document doc, Element element)
        {
            try
            {
                if (element == null || !(element is View))
                    return null;
                View view = element as View;

                ViewInfo info = new ViewInfo
                {
                    Id = element.Id.GetIntValue(),
                    UniqueId = element.UniqueId,
                    Name = element.Name,
                    FamilyName = element?.get_Parameter(BuiltInParameter.ELEM_FAMILY_PARAM)?.AsValueString(),
                    Category = element.Category?.Name,
                    BuiltInCategory = element.Category != null ?
                        Enum.GetName(typeof(BuiltInCategory), element.Category.Id.GetIntValue()) : null,
                    ElementClass = element.GetType().Name,
                    ViewType = view.ViewType.ToString(),
                    Scale = view.Scale,
                    IsTemplate = view.IsTemplate,
                    DetailLevel = view.DetailLevel.ToString(),
                    BoundingBox = GetBoundingBoxInfo(element)
                };

                // 获取与视图关联的标高
                if (view is ViewPlan viewPlan && viewPlan.GenLevel != null)
                {
                    Level level = viewPlan.GenLevel;
                    info.AssociatedLevel = new LevelInfo
                    {
                        Id = level.Id.GetIntValue(),
                        Name = level.Name,
                        Height = level.Elevation * 304.8 // 转换为mm
                    };
                }

                // 判断视图是否打开和激活
                UIDocument uidoc = new UIDocument(doc);

                // 获取所有打开的视图
                IList<UIView> openViews = uidoc.GetOpenUIViews();

                foreach (UIView uiView in openViews)
                {
                    // 检查视图是否打开
                    if (uiView.ViewId.GetValue() == view.Id.GetValue())
                    {
                        info.IsOpen = true;

                        // 检查视图是否是当前激活的视图
                        if (uidoc.ActiveView.Id.GetValue() == view.Id.GetValue())
                        {
                            info.IsActive = true;
                        }
                        break;
                    }
                }

                return info;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"创建视图元素信息时出错: {ex.Message}");
                return null;
            }
        }
        /// <summary>
        /// 创建注释元素的信息
        /// </summary>
        public static AnnotationInfo CreateAnnotationInfo(Document doc, Element element)
        {
            try
            {
                if (element == null)
                    return null;
                AnnotationInfo info = new AnnotationInfo
                {
                    Id = element.Id.GetIntValue(),
                    UniqueId = element.UniqueId,
                    Name = element.Name,
                    FamilyName = element?.get_Parameter(BuiltInParameter.ELEM_FAMILY_PARAM)?.AsValueString(),
                    Category = element.Category?.Name,
                    BuiltInCategory = element.Category != null ?
                        Enum.GetName(typeof(BuiltInCategory), element.Category.Id.GetIntValue()) : null,
                    ElementClass = element.GetType().Name,
                    BoundingBox = GetBoundingBoxInfo(element)
                };

                // 获取所在视图
                Parameter viewParam = element.get_Parameter(BuiltInParameter.VIEW_NAME);
                if (viewParam != null && viewParam.HasValue)
                {
                    info.OwnerView = viewParam.AsString();
                }
                else if (element.OwnerViewId != ElementId.InvalidElementId)
                {
                    View ownerView = doc.GetElement(element.OwnerViewId) as View;
                    info.OwnerView = ownerView?.Name;
                }

                // 处理文字标注
                if (element is TextNote textNote)
                {
                    info.TextContent = textNote.Text;
                    XYZ position = textNote.Coord;
                    // 转换为mm
                    info.Position = new JZPoint(
                        position.X * 304.8,
                        position.Y * 304.8,
                        position.Z * 304.8);
                }
                // 处理尺寸标注
                else if (element is Dimension dimension)
                {
                    info.DimensionValue = dimension.Value.ToString();
                    XYZ origin = dimension.Origin;
                    // 转换为mm
                    info.Position = new JZPoint(
                        origin.X * 304.8,
                        origin.Y * 304.8,
                        origin.Z * 304.8);
                }
                // 处理其他注释元素
                else if (element is AnnotationSymbol annotationSymbol)
                {
                    if (annotationSymbol.Location is LocationPoint locationPoint)
                    {
                        XYZ position = locationPoint.Point;
                        // 转换为mm
                        info.Position = new JZPoint(
                            position.X * 304.8,
                            position.Y * 304.8,
                            position.Z * 304.8);
                    }
                }
                return info;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"创建注释元素信息时出错: {ex.Message}");
                return null;
            }
        }
        /// <summary>
        /// 创建组或链接的信息
        /// </summary>
        public static GroupOrLinkInfo CreateGroupOrLinkInfo(Document doc, Element element)
        {
            try
            {
                if (element == null)
                    return null;
                GroupOrLinkInfo info = new GroupOrLinkInfo
                {
                    Id = element.Id.GetIntValue(),
                    UniqueId = element.UniqueId,
                    Name = element.Name,
                    FamilyName = element?.get_Parameter(BuiltInParameter.ELEM_FAMILY_PARAM)?.AsValueString(),
                    Category = element.Category?.Name,
                    BuiltInCategory = element.Category != null ?
                        Enum.GetName(typeof(BuiltInCategory), element.Category.Id.GetIntValue()) : null,
                    ElementClass = element.GetType().Name,
                    BoundingBox = GetBoundingBoxInfo(element)
                };

                // 处理组
                if (element is Group group)
                {
                    ICollection<ElementId> memberIds = group.GetMemberIds();
                    info.MemberCount = memberIds?.Count;
                    info.GroupType = group.GroupType?.Name;
                }
                // 处理链接
                else if (element is RevitLinkInstance linkInstance)
                {
                    RevitLinkType linkType = doc.GetElement(linkInstance.GetTypeId()) as RevitLinkType;
                    if (linkType != null)
                    {
                        ExternalFileReference extFileRef = linkType.GetExternalFileReference();
                        // 获取绝对路径
                        string absPath = ModelPathUtils.ConvertModelPathToUserVisiblePath(extFileRef.GetAbsolutePath());
                        info.LinkPath = absPath;

                        // 使用GetLinkedFileStatus获取链接状态
                        LinkedFileStatus linkStatus = linkType.GetLinkedFileStatus();
                        info.LinkStatus = linkStatus.ToString();
                    }
                    else
                    {
                        info.LinkStatus = LinkedFileStatus.Invalid.ToString();
                    }

                    // 获取位置
                    LocationPoint location = linkInstance.Location as LocationPoint;
                    if (location != null)
                    {
                        XYZ point = location.Point;
                        // 转换为mm
                        info.Position = new JZPoint(
                            point.X * 304.8,
                            point.Y * 304.8,
                            point.Z * 304.8);
                    }
                }

                return info;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"创建组和链接信息时出错: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 创建元素的增强基础信息
        /// </summary>
        public static ElementBasicInfo CreateElementBasicInfo(Document doc, Element element)
        {
            try
            {
                if (element == null)
                    return null;
                ElementBasicInfo basicInfo = new ElementBasicInfo
                {
                    Id = element.Id.GetIntValue(),
                    UniqueId = element.UniqueId,
                    Name = element.Name,
                    FamilyName = element?.get_Parameter(BuiltInParameter.ELEM_FAMILY_PARAM)?.AsValueString(),
                    Category = element.Category?.Name,
                    BuiltInCategory = element.Category != null ?
                        Enum.GetName(typeof(BuiltInCategory), element.Category.Id.GetIntValue()) : null,
                    BoundingBox = GetBoundingBoxInfo(element)
                };
                return basicInfo;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"创建元素基础信息时出错: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 获取系统族构件的厚度参数信息
        /// </summary>
        /// <param name="element">系统族构件（墙、楼板、门等）</param>
        /// <returns>参数信息对象，无效返回null</returns>
        public static ParameterInfo GetThicknessInfo(Element element)
        {
            if (element == null)
            {
                return null;
            }

            // 获取构件类型
            ElementType elementType = element.Document.GetElement(element.GetTypeId()) as ElementType;
            if (elementType == null)
            {
                return null;
            }

            // 根据不同构件类型获取对应的内置厚度参数
            Parameter thicknessParam = null;

            if (elementType is WallType)
            {
                thicknessParam = elementType.get_Parameter(BuiltInParameter.WALL_ATTR_WIDTH_PARAM);
            }
            else if (elementType is FloorType)
            {
                thicknessParam = elementType.get_Parameter(BuiltInParameter.FLOOR_ATTR_THICKNESS_PARAM);
            }
            else if (elementType is FamilySymbol familySymbol)
            {
                switch (familySymbol.Category?.Id.GetIntValue())
                {
                    case (int)BuiltInCategory.OST_Doors:
                    case (int)BuiltInCategory.OST_Windows:
                        thicknessParam = elementType.get_Parameter(BuiltInParameter.FAMILY_THICKNESS_PARAM);
                        break;
                }
            }
            else if (elementType is CeilingType)
            {
                thicknessParam = elementType.get_Parameter(BuiltInParameter.CEILING_THICKNESS);
            }

            if (thicknessParam != null && thicknessParam.HasValue)
            {
                return new ParameterInfo
                {
                    Name = "厚度",
                    Value = $"{thicknessParam.AsDouble() * 304.8}"
                };
            }
            return null;
        }

        /// <summary>
        /// 获取元素所属的标高信息
        /// </summary>
        public static LevelInfo GetElementLevel(Document doc, Element element)
        {
            try
            {
                Level level = null;

                // 处理不同类型元素的标高获取
                if (element is Wall wall) // 墙体
                {
                    level = doc.GetElement(wall.LevelId) as Level;
                }
                else if (element is Floor floor) // 楼板
                {
                    Parameter levelParam = floor.get_Parameter(BuiltInParameter.LEVEL_PARAM);
                    if (levelParam != null && levelParam.HasValue)
                    {
                        level = doc.GetElement(levelParam.AsElementId()) as Level;
                    }
                }
                else if (element is FamilyInstance familyInstance) // 族实例（包括常规模型等）
                {
                    // 尝试获取族实例的标高参数
                    Parameter levelParam = familyInstance.get_Parameter(BuiltInParameter.FAMILY_LEVEL_PARAM);
                    if (levelParam != null && levelParam.HasValue)
                    {
                        level = doc.GetElement(levelParam.AsElementId()) as Level;
                    }
                    // 如果上面的方法获取不到，尝试使用SCHEDULE_LEVEL_PARAM
                    if (level == null)
                    {
                        levelParam = familyInstance.get_Parameter(BuiltInParameter.SCHEDULE_LEVEL_PARAM);
                        if (levelParam != null && levelParam.HasValue)
                        {
                            level = doc.GetElement(levelParam.AsElementId()) as Level;
                        }
                    }
                }
                else // 其他元素
                {
                    // 尝试获取通用的标高参数
                    Parameter levelParam = element.get_Parameter(BuiltInParameter.INSTANCE_REFERENCE_LEVEL_PARAM);
                    if (levelParam != null && levelParam.HasValue)
                    {
                        level = doc.GetElement(levelParam.AsElementId()) as Level;
                    }
                }

                if (level != null)
                {
                    LevelInfo levelInfo = new LevelInfo
                    {
                        Id = level.Id.GetIntValue(),
                        Name = level.Name,
                        Height = level.Elevation * 304.8
                    };
                    return levelInfo;
                }
                else
                {
                    return null;
                }
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 获取元素的包围盒信息
        /// </summary>
        public static BoundingBoxInfo GetBoundingBoxInfo(Element element)
        {
            try
            {
                BoundingBoxXYZ bbox = element.get_BoundingBox(null);
                if (bbox == null)
                    return null;
                return new BoundingBoxInfo
                {
                    Min = new JZPoint(
                        bbox.Min.X * 304.8,
                        bbox.Min.Y * 304.8,
                        bbox.Min.Z * 304.8),
                    Max = new JZPoint(
                        bbox.Max.X * 304.8,
                        bbox.Max.Y * 304.8,
                        bbox.Max.Z * 304.8)
                };
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 获取包围盒的高度参数信息
        /// </summary>
        /// <param name="boundingBoxInfo">包围盒信息</param>
        /// <returns>参数信息对象，无效返回null</returns>
        public static ParameterInfo GetBoundingBoxHeight(BoundingBoxInfo boundingBoxInfo)
        {
            try
            {
                // 参数检查
                if (boundingBoxInfo?.Min == null || boundingBoxInfo?.Max == null)
                {
                    return null;
                }

                // Z轴方向的差值即为高度
                double height = Math.Abs(boundingBoxInfo.Max.Z - boundingBoxInfo.Min.Z);

                return new ParameterInfo
                {
                    Name = "高度",
                    Value = $"{height}"
                };
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 获取元素中所有非空参数的名称和值
        /// </summary>
        /// <param name="element">Revit元素</param>
        /// <returns>参数信息列表</returns>
        public static List<ParameterInfo> GetDimensionParameters(Element element)
        {
            // 检查元素是否为空
            if (element == null)
            {
                return new List<ParameterInfo>();
            }

            var parameters = new List<ParameterInfo>();

            // 获取元素的所有参数
            foreach (Parameter param in element.Parameters)
            {
                try
                {
                    // 跳过无效参数
                    if (!param.HasValue || param.IsReadOnly)
                    {
                        continue;
                    }

                    // 如果当前参数是尺寸相关参数
                    if (IsDimensionParameter(param))
                    {
                        // 获取参数值的字符串表示
                        string value = param.AsValueString();

                        // 如果值非空，则添加到列表中
                        if (!string.IsNullOrWhiteSpace(value))
                        {
                            parameters.Add(new ParameterInfo
                            {
                                Name = param.Definition.Name,
                                Value = value
                            });
                        }
                    }
                }
                catch
                {
                    // 如果获取某个参数值出错，继续处理下一个
                    continue;
                }
            }

            // 按参数名称排序后返回
            return parameters.OrderBy(p => p.Name).ToList();
        }

        /// <summary>
        /// 判断参数是否为可写入的尺寸参数
        /// </summary>
        public static bool IsDimensionParameter(Parameter param)
        {

            // 在Revit 2023中使用Definition的GetDataType()方法获取参数类型
            ForgeTypeId paramTypeId = param.Definition.GetDataType();

            // 判断参数是否为尺寸相关的类型
            bool isDimensionType = paramTypeId.Equals(SpecTypeId.Length) ||
                                   paramTypeId.Equals(SpecTypeId.Angle) ||
                                   paramTypeId.Equals(SpecTypeId.Area) ||
                                   paramTypeId.Equals(SpecTypeId.Volume);
            // 只存储尺寸类型参数
            return isDimensionType;
        }

    }

    /// <summary>
    /// 存储元素完整信息的自定义类
    /// </summary>
    public class ElementInstanceInfo
    {
        /// <summary>
        /// Id
        /// </summary>
        public int Id { get; set; }
        /// <summary>
        /// Id
        /// </summary>
        public string UniqueId { get; set; }
        /// <summary>
        /// 类型Id
        /// </summary>
        public int TypeId { get; set; }
        /// <summary>
        /// 名称
        /// </summary>
        public string Name { get; set; }
        /// <summary>
        /// 族名称
        /// </summary>
        public string FamilyName { get; set; }
        /// <summary>
        /// 类别
        /// </summary>
        public string Category { get; set; }
        /// <summary>
        /// 内置类别
        /// </summary>
        public string BuiltInCategory { get; set; }
        /// <summary>
        /// 所属房间Id
        /// </summary>
        public int RoomId { get; set; }
        /// <summary>
        /// 所属标高名称
        /// </summary>
        public LevelInfo Level { get; set; }
        /// <summary>
        /// 位置信息
        /// </summary>
        public BoundingBoxInfo BoundingBox { get; set; }
        /// <summary>
        /// 实例参数
        /// </summary>
        public List<ParameterInfo> Parameters { get; set; } = new List<ParameterInfo>();

    }

    /// <summary>
    /// 存储元素类型完整信息的自定义类
    /// </summary>
    public class ElementTypeInfo
    {
        /// <summary>
        /// ID
        /// </summary>
        public int Id { get; set; }
        /// <summary>
        /// Id
        /// </summary>
        public string UniqueId { get; set; }
        /// <summary>
        /// 名称
        /// </summary>
        public string Name { get; set; }
        /// <summary>
        /// 族名称
        /// </summary>
        public string FamilyName { get; set; }
        /// <summary>
        /// 类别名称
        /// </summary>
        public string Category { get; set; }
        /// <summary>
        /// 内置类别ID
        /// </summary>
        public string BuiltInCategory { get; set; }
        /// <summary>
        /// 类型参数
        /// </summary>
        public List<ParameterInfo> Parameters { get; set; } = new List<ParameterInfo>();

    }

    /// <summary>
    /// 空间定位元素(标高、轴网等)基础信息的类
    /// </summary>
    public class PositioningElementInfo
    {
        /// <summary>
        /// 元素ID
        /// </summary>
        public int Id { get; set; }
        /// <summary>
        /// 元素唯一ID
        /// </summary>
        public string UniqueId { get; set; }
        /// <summary>
        /// 名称
        /// </summary>
        public string Name { get; set; }
        /// <summary>
        /// 族名称
        /// </summary>
        public string FamilyName { get; set; }
        /// <summary>
        /// 类别名称
        /// </summary>
        public string Category { get; set; }
        /// <summary>
        /// 内置类别(可选)
        /// </summary>
        public string BuiltInCategory { get; set; }
        /// <summary>
        /// 元素的.NET类名称
        /// </summary>
        public string ElementClass { get; set; }
        /// <summary>
        /// 高程值 (适用于标高，单位mm)
        /// </summary>
        public double? Elevation { get; set; }
        /// <summary>
        /// 所属标高
        /// </summary>
        public LevelInfo Level { get; set; }
        /// <summary>
        /// 位置信息
        /// </summary>
        public BoundingBoxInfo BoundingBox { get; set; }
        /// <summary>
        /// 轴网线(适用于轴网)
        /// </summary>
        public JZLine GridLine { get; set; }
    }
    /// <summary>
    /// 存储空间元素(房间、区域等)基础信息的类
    /// </summary>
    public class SpatialElementInfo
    {
        /// <summary>
        /// 元素ID
        /// </summary>
        public int Id { get; set; }
        /// <summary>
        /// 元素唯一ID
        /// </summary>
        public string UniqueId { get; set; }
        /// <summary>
        /// 名称
        /// </summary>
        public string Name { get; set; }
        /// <summary>
        /// 族名称
        /// </summary>
        public string FamilyName { get; set; }
        /// <summary>
        /// 编号
        /// </summary>
        public string Number { get; set; }
        /// <summary>
        /// 类别名称
        /// </summary>
        public string Category { get; set; }
        /// <summary>
        /// 内置类别(可选)
        /// </summary>
        public string BuiltInCategory { get; set; }
        /// <summary>
        /// 元素的.NET类名称
        /// </summary>
        public string ElementClass { get; set; }
        /// <summary>
        /// 面积(单位mm²)
        /// </summary>
        public double? Area { get; set; }
        /// <summary>
        /// 体积(单位mm³)
        /// </summary>
        public double? Volume { get; set; }
        /// <summary>
        /// 周长(单位mm)
        /// </summary>
        public double? Perimeter { get; set; }
        /// <summary>
        /// 所在标高
        /// </summary>
        public LevelInfo Level { get; set; }

        /// <summary>
        /// 位置信息
        /// </summary>
        public BoundingBoxInfo BoundingBox { get; set; }
    }
    /// <summary>
    /// 存储视图元素基础信息的类
    /// </summary>
    public class ViewInfo
    {
        /// <summary>
        /// 元素ID
        /// </summary>
        public int Id { get; set; }
        /// <summary>
        /// 元素唯一ID
        /// </summary>
        public string UniqueId { get; set; }
        /// <summary>
        /// 名称
        /// </summary>
        public string Name { get; set; }
        /// <summary>
        /// 族名称
        /// </summary>
        public string FamilyName { get; set; }
        /// <summary>
        /// 类别名称
        /// </summary>
        public string Category { get; set; }
        /// <summary>
        /// 内置类别(可选)
        /// </summary>
        public string BuiltInCategory { get; set; }
        /// <summary>
        /// 元素的.NET类名称
        /// </summary>
        public string ElementClass { get; set; }

        /// <summary>
        /// 视图类型
        /// </summary>
        public string ViewType { get; set; }

        /// <summary>
        /// 视图比例
        /// </summary>
        public int? Scale { get; set; }

        /// <summary>
        /// 是否为模板视图
        /// </summary>
        public bool IsTemplate { get; set; }

        /// <summary>
        /// 详图级别
        /// </summary>
        public string DetailLevel { get; set; }

        /// <summary>
        /// 关联的标高
        /// </summary>
        public LevelInfo AssociatedLevel { get; set; }

        /// <summary>
        /// 位置信息
        /// </summary>
        public BoundingBoxInfo BoundingBox { get; set; }

        /// <summary>
        /// 视图是否已打开
        /// </summary>
        public bool IsOpen { get; set; }

        /// <summary>
        /// 是否是当前激活的视图
        /// </summary>
        public bool IsActive { get; set; }
    }
    /// <summary>
    /// 存储注释元素基础信息的类
    /// </summary>
    public class AnnotationInfo
    {
        /// <summary>
        /// 元素ID
        /// </summary>
        public int Id { get; set; }
        /// <summary>
        /// 元素唯一ID
        /// </summary>
        public string UniqueId { get; set; }
        /// <summary>
        /// 名称
        /// </summary>
        public string Name { get; set; }
        /// <summary>
        /// 族名称
        /// </summary>
        public string FamilyName { get; set; }
        /// <summary>
        /// 类别名称
        /// </summary>
        public string Category { get; set; }
        /// <summary>
        /// 内置类别(可选)
        /// </summary>
        public string BuiltInCategory { get; set; }
        /// <summary>
        /// 元素的.NET类名称
        /// </summary>
        public string ElementClass { get; set; }
        /// <summary>
        /// 所在视图
        /// </summary>
        public string OwnerView { get; set; }
        /// <summary>
        /// 文本内容 (适用于文字标注)
        /// </summary>
        public string TextContent { get; set; }
        /// <summary>
        /// 位置信息(单位mm)
        /// </summary>
        public JZPoint Position { get; set; }

        /// <summary>
        /// 位置信息
        /// </summary>
        public BoundingBoxInfo BoundingBox { get; set; }
        /// <summary>
        /// 尺寸值 (适用于尺寸标注)
        /// </summary>
        public string DimensionValue { get; set; }
    }
    /// <summary>
    /// 存储组和链接基础信息的类
    /// </summary>
    public class GroupOrLinkInfo
    {
        /// <summary>
        /// 元素ID
        /// </summary>
        public int Id { get; set; }
        /// <summary>
        /// 元素唯一ID
        /// </summary>
        public string UniqueId { get; set; }
        /// <summary>
        /// 名称
        /// </summary>
        public string Name { get; set; }
        /// <summary>
        /// 族名称
        /// </summary>
        public string FamilyName { get; set; }
        /// <summary>
        /// 类别名称
        /// </summary>
        public string Category { get; set; }
        /// <summary>
        /// 内置类别(可选)
        /// </summary>
        public string BuiltInCategory { get; set; }
        /// <summary>
        /// 元素的.NET类名称
        /// </summary>
        public string ElementClass { get; set; }
        /// <summary>
        /// 组成员数量
        /// </summary>
        public int? MemberCount { get; set; }
        /// <summary>
        /// 组类型
        /// </summary>
        public string GroupType { get; set; }
        /// <summary>
        /// 链接状态
        /// </summary>
        public string LinkStatus { get; set; }
        /// <summary>
        /// 链接路径
        /// </summary>
        public string LinkPath { get; set; }
        /// <summary>
        /// 位置信息(单位mm)
        /// </summary>
        public JZPoint Position { get; set; }

        /// <summary>
        /// 位置信息
        /// </summary>
        public BoundingBoxInfo BoundingBox { get; set; }
    }
    /// <summary>
    /// 存储元素基础信息的增强类
    /// </summary>
    public class ElementBasicInfo
    {
        /// <summary>
        /// 元素ID
        /// </summary>
        public int Id { get; set; }
        /// <summary>
        /// 元素唯一ID
        /// </summary>
        public string UniqueId { get; set; }
        /// <summary>
        /// 名称
        /// </summary>
        public string Name { get; set; }
        /// <summary>
        /// 族名称
        /// </summary>
        public string FamilyName { get; set; }
        /// <summary>
        /// 类别名称
        /// </summary>
        public string Category { get; set; }
        /// <summary>
        /// 内置类别(可选)
        /// </summary>
        public string BuiltInCategory { get; set; }

        /// <summary>
        /// 位置信息
        /// </summary>
        public BoundingBoxInfo BoundingBox { get; set; }
    }



    /// <summary>
    /// 存储参数信息完整的自定义类
    /// </summary>
    public class ParameterInfo
    {
        public string Name { get; set; }
        public string Value { get; set; }
    }

    /// <summary>
    /// 存储包围盒信息的自定义类
    /// </summary>
    public class BoundingBoxInfo
    {
        public JZPoint Min { get; set; }
        public JZPoint Max { get; set; }
    }

    /// <summary>
    /// 存储标高信息的自定义类
    /// </summary>
    public class LevelInfo
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public double Height { get; set; }
    }



}
