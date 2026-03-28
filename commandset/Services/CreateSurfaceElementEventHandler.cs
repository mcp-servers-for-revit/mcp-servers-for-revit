using Autodesk.Revit.UI;
using RevitMCPCommandSet.Models.Common;
using RevitMCPCommandSet.Utils;
using RevitMCPSDK.API.Interfaces;

namespace RevitMCPCommandSet.Services
{
    public class CreateSurfaceElementEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
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
        public List<SurfaceElement> CreatedInfo { get; private set; }
        /// <summary>
        /// 执行结果（传出数据）
        /// </summary>
        public AIResult<List<int>> Result { get; private set; }
        public string _floorName = "常规 - ";
        public bool _structural = true;
        private List<string> _warnings = new List<string>();

        /// <summary>
        /// 设置创建的参数
        /// </summary>
        public void SetParameters(List<SurfaceElement> data)
        {
            CreatedInfo = data;
            _resetEvent.Reset();
        }
        public void Execute(UIApplication uiapp)
        {
            uiApp = uiapp;

            try
            {
                var elementIds = new List<int>();
                _warnings.Clear();
                foreach (var data in CreatedInfo)
                {
                    int requestedTypeId = data.TypeId;
                    // Step0 获取构件类型
                    BuiltInCategory builtInCategory = BuiltInCategory.INVALID;
                    Enum.TryParse(data.Category.Replace(".", "").Replace("BuiltInCategory", ""), true, out builtInCategory);

                    // Step1 获取标高和偏移
                    Level baseLevel = null;
                    Level topLevel = null;
                    double topOffset = -1;  // ft
                    double baseOffset = -1; // ft
                    baseLevel = doc.FindNearestLevel(data.BaseLevel / 304.8);
                    baseOffset = (data.BaseOffset + data.BaseLevel) / 304.8 - baseLevel.Elevation;
                    topLevel = doc.FindNearestLevel((data.BaseLevel + data.BaseOffset + data.Thickness) / 304.8);
                    topOffset = (data.BaseLevel + data.BaseOffset + data.Thickness) / 304.8 - topLevel.Elevation;
                    if (baseLevel == null)
                        continue;

                    // Step2 获取族类型
                    FamilySymbol symbol = null;
                    FloorType floorType = null;
                    RoofType roofType = null;
                    CeilingType ceilingType = null;
                    if (data.TypeId != -1 && data.TypeId != 0)
                    {
                        ElementId typeELeId = new ElementId(data.TypeId);
                        if (typeELeId != null)
                        {
                            Element typeEle = doc.GetElement(typeELeId);
                            if (typeEle != null && typeEle is FamilySymbol)
                            {
                                symbol = typeEle as FamilySymbol;
                                // 获取symbol的Category对象并转换为BuiltInCategory枚举
                                builtInCategory = (BuiltInCategory)symbol.Category.Id.GetIntValue();
                            }
                            else if (typeEle != null && typeEle is FloorType)
                            {
                                floorType = typeEle as FloorType;
                                builtInCategory = (BuiltInCategory)floorType.Category.Id.GetIntValue();
                            }
                            else if (typeEle != null && typeEle is RoofType)
                            {
                                roofType = typeEle as RoofType;
                                builtInCategory = (BuiltInCategory)roofType.Category.Id.GetIntValue();
                            }
                            else if (typeEle != null && typeEle is CeilingType)
                            {
                                ceilingType = typeEle as CeilingType;
                                builtInCategory = (BuiltInCategory)ceilingType.Category.Id.GetIntValue();
                            }
                        }
                    }
                    if (builtInCategory == BuiltInCategory.INVALID)
                        continue;
                    switch (builtInCategory)
                    {
                        case BuiltInCategory.OST_Floors:
                            if (floorType == null)
                            {
                                // Requested typeId was invalid or not provided, fall back to first available
                                floorType = new FilteredElementCollector(doc)
                                    .OfClass(typeof(FloorType))
                                    .OfCategory(BuiltInCategory.OST_Floors)
                                    .Cast<FloorType>()
                                    .FirstOrDefault();
                                if (floorType == null)
                                {
                                    _warnings.Add($"No floor types available in project.");
                                    continue;
                                }
                                if (requestedTypeId != -1 && requestedTypeId != 0)
                                {
                                    _warnings.Add($"Requested floor typeId {requestedTypeId} not found. Defaulted to '{floorType.Name}' (ID: {floorType.Id.GetIntValue()})");
                                }
                            }
                            break;
                        case BuiltInCategory.OST_Roofs:
                            if (roofType == null)
                            {
                                // Get default roof type if not specified
                                roofType = new FilteredElementCollector(doc)
                                    .OfClass(typeof(RoofType))
                                    .OfCategory(BuiltInCategory.OST_Roofs)
                                    .Cast<RoofType>()
                                    .FirstOrDefault();
                                if (roofType == null)
                                {
                                    _warnings.Add($"No roof types available in project.");
                                    continue;
                                }
                                if (requestedTypeId != -1 && requestedTypeId != 0)
                                {
                                    _warnings.Add($"Requested roof typeId {requestedTypeId} not found. Defaulted to '{roofType.Name}' (ID: {roofType.Id.GetIntValue()})");
                                }
                            }
                            break;
                        case BuiltInCategory.OST_Ceilings:
                            if (ceilingType == null)
                            {
                                // Get default ceiling type if not specified
                                ceilingType = new FilteredElementCollector(doc)
                                    .OfClass(typeof(CeilingType))
                                    .OfCategory(BuiltInCategory.OST_Ceilings)
                                    .Cast<CeilingType>()
                                    .FirstOrDefault();
                                if (ceilingType == null)
                                {
                                    _warnings.Add($"No ceiling types available in project.");
                                    continue;
                                }
                                if (requestedTypeId != -1 && requestedTypeId != 0)
                                {
                                    _warnings.Add($"Requested ceiling typeId {requestedTypeId} not found. Defaulted to '{ceilingType.Name}' (ID: {ceilingType.Id.GetIntValue()})");
                                }
                            }
                            break;
                        default:
                            if (symbol == null)
                            {
                                symbol = new FilteredElementCollector(doc)
                                    .OfClass(typeof(FamilySymbol))
                                    .OfCategory(builtInCategory)
                                    .Cast<FamilySymbol>()
                                    .FirstOrDefault(fs => fs.IsActive); // 获取激活的类型作为默认类型
                                if (symbol == null)
                                {
                                    symbol = new FilteredElementCollector(doc)
                                    .OfClass(typeof(FamilySymbol))
                                    .OfCategory(builtInCategory)
                                    .Cast<FamilySymbol>()
                                    .FirstOrDefault();
                                }
                            }
                            if (symbol == null)
                                continue;
                            break;
                    }

                    // Step3 批量创建楼板
                    Floor floor = null;
                    using (Transaction transaction = new Transaction(doc, "创建面状构件"))
                    {
                        transaction.Start();

                        switch (builtInCategory)
                        {
                            case BuiltInCategory.OST_Floors:
                                CurveArray curves = new CurveArray();
                                foreach (var jzLine in data.Boundary.OuterLoop)
                                {
                                    curves.Append(JZLine.ToLine(jzLine));
                                }
                                CurveLoop curveLoop = CurveLoop.Create(data.Boundary.OuterLoop.Select(l => JZLine.ToLine(l) as Curve).ToList());

                                // 多版本 - Floor.Create introduced in Revit 2022 but stable in 2023+
                                floor = Floor.Create(doc, new List<CurveLoop> { curveLoop }, floorType.Id, baseLevel.Id);
                                //编辑楼板参数
                                if (floor != null)
                                {
                                    floor.get_Parameter(BuiltInParameter.FLOOR_HEIGHTABOVELEVEL_PARAM).Set(baseOffset);
                                    elementIds.Add(floor.Id.GetIntValue());
                                }
                                break;
                            case BuiltInCategory.OST_Roofs:
                                CurveArray roofCurves = new CurveArray();
                                foreach (var jzLine in data.Boundary.OuterLoop)
                                {
                                    roofCurves.Append(JZLine.ToLine(jzLine));
                                }

                                ModelCurveArray modelCurves = new ModelCurveArray();
                                FootPrintRoof roof = doc.Create.NewFootPrintRoof(roofCurves, baseLevel, roofType, out modelCurves);

                                if (roof != null)
                                {
                                    // Set all edges to non-sloped for flat roof
                                    foreach (ModelCurve mc in modelCurves)
                                    {
                                        roof.set_DefinesSlope(mc, false);
                                    }
                                    // Set the roof offset from level
                                    Parameter offsetParam = roof.get_Parameter(BuiltInParameter.ROOF_LEVEL_OFFSET_PARAM);
                                    if (offsetParam != null)
                                    {
                                        offsetParam.Set(baseOffset);
                                    }
                                    elementIds.Add(roof.Id.GetIntValue());
                                }
                                break;
                            case BuiltInCategory.OST_Ceilings:
                                CurveLoop ceilingCurveLoop = CurveLoop.Create(data.Boundary.OuterLoop.Select(l => JZLine.ToLine(l) as Curve).ToList());

                                Ceiling ceiling = Ceiling.Create(doc, new List<CurveLoop> { ceilingCurveLoop }, ceilingType.Id, baseLevel.Id);
                                if (ceiling != null)
                                {
                                    // Set the ceiling height offset from level
                                    Parameter ceilingOffsetParam = ceiling.get_Parameter(BuiltInParameter.CEILING_HEIGHTABOVELEVEL_PARAM);
                                    if (ceilingOffsetParam != null)
                                    {
                                        ceilingOffsetParam.Set(baseOffset);
                                    }
                                    elementIds.Add(ceiling.Id.GetIntValue());
                                }
                                break;
                            default:
                                break;
                        }

                        transaction.Commit();
                    }
                }
                string message = $"Successfully created {elementIds.Count} element(s).";
                if (_warnings.Count > 0)
                {
                    message += "\n\n⚠ Warnings:\n  • " + string.Join("\n  • ", _warnings);
                }
                Result = new AIResult<List<int>>
                {
                    Success = true,
                    Message = message,
                    Response = elementIds,
                };
            }
            catch (Exception ex)
            {
                Result = new AIResult<List<int>>
                {
                    Success = false,
                    Message = $"创建面状构件时出错: {ex.Message}",
                };
                TaskDialog.Show("错误", $"创建面状构件时出错: {ex.Message}");
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
            return "创建面状构件";
        }

        /// <summary>
        /// 获取或创建指定厚度的楼板类型
        /// </summary>
        /// <param name="thickness">目标厚度（ft）</param>
        /// <returns>符合厚度要求的楼板类型</returns>
        private FloorType CreateOrGetFloorType(Document doc, double thickness = 200 / 304.8)
        {

            // 查找匹配厚度的楼板类型
            FloorType existingType = new FilteredElementCollector(doc)
                                     .OfClass(typeof(FloorType))                    // 仅获取FloorType类
                                     .OfCategory(BuiltInCategory.OST_Floors)        // 仅获取楼板类别
                                     .Cast<FloorType>()                            // 转换为FloorType类型
                                     .FirstOrDefault(w => w.Name == $"{_floorName}{thickness * 304.8}mm");
            if (existingType != null)
                return existingType;
            // 如果没有找到匹配的楼板类型，创建新的
            FloorType baseFloorType = existingType = new FilteredElementCollector(doc)
                                     .OfClass(typeof(FloorType))                    // 仅获取FloorType类
                                     .OfCategory(BuiltInCategory.OST_Floors)        // 仅获取楼板类别
                                     .Cast<FloorType>()                            // 转换为FloorType类型
                                     .FirstOrDefault(w => w.Name.Contains("常规"));
            if (existingType != null)
            {
                baseFloorType = existingType = new FilteredElementCollector(doc)
                                     .OfClass(typeof(FloorType))                    // 仅获取FloorType类
                                     .OfCategory(BuiltInCategory.OST_Floors)        // 仅获取楼板类别
                                     .Cast<FloorType>()                            // 转换为FloorType类型
                                     .FirstOrDefault();
            }

            // 复制楼板类型
            FloorType newFloorType = null;
            newFloorType = baseFloorType.Duplicate($"{_floorName}{thickness * 304.8}mm") as FloorType;

            // 设置新楼板类型的厚度
            // 获取构造层设置
            CompoundStructure cs = newFloorType.GetCompoundStructure();
            if (cs != null)
            {
                // 获取所有层
                IList<CompoundStructureLayer> layers = cs.GetLayers();
                if (layers.Count > 0)
                {
                    // 计算当前总厚度
                    double currentTotalThickness = cs.GetWidth();

                    // 按比例调整每层厚度
                    for (int i = 0; i < layers.Count; i++)
                    {
                        CompoundStructureLayer layer = layers[i];
                        double newLayerThickness = thickness;
                        cs.SetLayerWidth(i, newLayerThickness);
                    }

                    // 应用修改后的构造层设置
                    newFloorType.SetCompoundStructure(cs);
                }
            }
            return newFloorType;
        }

    }
}
