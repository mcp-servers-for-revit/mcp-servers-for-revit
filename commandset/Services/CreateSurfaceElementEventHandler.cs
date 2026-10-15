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
        public AIResult<List<long>> Result { get; private set; }
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
                var elementIds = new List<long>();
                var failures = new List<string>();
                _warnings.Clear();
                for (int i = 0; i < CreatedInfo.Count; i++)
                {
                    SurfaceElement data = CreatedInfo[i];
                    try
                    {
                        elementIds.Add(CreateElement(data));
                    }
                    catch (Exception ex)
                    {
                        failures.Add($"Element {i + 1} ({data.Category}): {ex.Message}");
                    }
                }

                string message = $"Created {elementIds.Count} of {CreatedInfo.Count} element(s).";
                if (failures.Count > 0)
                {
                    message += "\n\nNot created:\n  • " + string.Join("\n  • ", failures);
                }
                if (_warnings.Count > 0)
                {
                    message += "\n\n⚠ Warnings:\n  • " + string.Join("\n  • ", _warnings);
                }
                Result = new AIResult<List<long>>
                {
                    Success = elementIds.Count > 0,
                    Message = message,
                    Response = elementIds,
                };
            }
            catch (Exception ex)
            {
                Result = new AIResult<List<long>>
                {
                    Success = false,
                    Message = $"Error creating surface-based elements: {ex.Message}",
                };
            }
            finally
            {
                _resetEvent.Set(); // 通知等待线程操作已完成
            }
        }

        /// <summary>
        /// Creates one floor, roof or ceiling and returns its id. Throws with the reason when it can't be created.
        /// </summary>
        private long CreateElement(SurfaceElement data)
        {
            long requestedTypeId = data.TypeId;
            // Step0 获取构件类型
            BuiltInCategory builtInCategory = BuiltInCategory.INVALID;
            Enum.TryParse(data.Category.Replace(".", "").Replace("BuiltInCategory", ""), true, out builtInCategory);

            // Step1 获取标高和偏移 (offset in ft). Thickness comes from the type, so it doesn't affect the levels.
            var (baseLevel, baseOffset, _, _) =
                LevelPlacement.Resolve(doc, data.LevelId, data.BaseLevel, data.BaseOffset, 0);

            // Step2 获取族类型
            FloorType floorType = null;
            RoofType roofType = null;
            CeilingType ceilingType = null;
            if (data.TypeId != -1 && data.TypeId != 0)
            {
                ElementId typeELeId = data.TypeId.ToRevitElementId();
                if (typeELeId != null)
                {
                    Element typeEle = doc.GetElement(typeELeId);
                    if (typeEle != null && typeEle is FloorType)
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
                            .FirstOrDefault()
                            ?? throw new InvalidOperationException("No floor types are available in the model.");
                        if (requestedTypeId != -1 && requestedTypeId != 0)
                        {
                            _warnings.Add($"Requested floor typeId {requestedTypeId} not found. Defaulted to '{floorType.Name}' (ID: {floorType.Id.GetValue()})");
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
                            .FirstOrDefault()
                            ?? throw new InvalidOperationException("No roof types are available in the model.");
                        if (requestedTypeId != -1 && requestedTypeId != 0)
                        {
                            _warnings.Add($"Requested roof typeId {requestedTypeId} not found. Defaulted to '{roofType.Name}' (ID: {roofType.Id.GetValue()})");
                        }
                    }
                    break;
                case BuiltInCategory.OST_Ceilings:
#if !REVIT2022_OR_GREATER
                    throw new NotSupportedException("Creating ceilings needs Revit 2022 or later.");
#else
                    if (ceilingType == null)
                    {
                        // Get default ceiling type if not specified
                        ceilingType = new FilteredElementCollector(doc)
                            .OfClass(typeof(CeilingType))
                            .OfCategory(BuiltInCategory.OST_Ceilings)
                            .Cast<CeilingType>()
                            .FirstOrDefault()
                            ?? throw new InvalidOperationException("No ceiling types are available in the model.");
                        if (requestedTypeId != -1 && requestedTypeId != 0)
                        {
                            _warnings.Add($"Requested ceiling typeId {requestedTypeId} not found. Defaulted to '{ceilingType.Name}' (ID: {ceilingType.Id.GetValue()})");
                        }
                    }
                    break;
#endif
                default:
                    throw new ArgumentException(
                        $"Unsupported category '{data.Category}'. Use OST_Floors, OST_Roofs or OST_Ceilings, or give the typeId of a floor, roof or ceiling type.");
            }

            // Step3 批量创建楼板
            using (Transaction transaction = new Transaction(doc, "Create surface-based element"))
            {
                transaction.Start();
                Element created = null;

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
#if REVIT2023_OR_GREATER
                        Floor floor = Floor.Create(doc, new List<CurveLoop> { curveLoop }, floorType.Id, baseLevel.Id);
#else
                        Floor floor = doc.Create.NewFloor(curves, floorType, baseLevel, _structural);
#endif
                        //编辑楼板参数
                        floor?.get_Parameter(BuiltInParameter.FLOOR_HEIGHTABOVELEVEL_PARAM).Set(baseOffset);
                        created = floor;
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
                        }
                        created = roof;
                        break;
#if REVIT2022_OR_GREATER
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
                        }
                        created = ceiling;
                        break;
#endif
                }

                if (created == null)
                {
                    throw new InvalidOperationException($"Revit could not create the {builtInCategory} element from that boundary.");
                }

                transaction.Commit();
                return created.Id.GetValue();
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
