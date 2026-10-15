using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.UI;
using RevitMCPCommandSet.Models.Common;
using RevitMCPCommandSet.Utils;
using RevitMCPSDK.API.Interfaces;

namespace RevitMCPCommandSet.Services
{
    public class CreateLineElementEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
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
        public List<LineElement> CreatedInfo { get; private set; }
        /// <summary>
        /// 执行结果（传出数据）
        /// </summary>
        public AIResult<List<long>> Result { get; private set; }
        private List<string> _warnings = new List<string>();

        public string _wallName = "常规 - ";
        public string _ductName = "矩形风管 - ";

        /// <summary>
        /// 设置创建的参数
        /// </summary>
        public void SetParameters(List<LineElement> data)
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
                    LineElement data = CreatedInfo[i];
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
                    Message = $"Error creating line-based elements: {ex.Message}",
                };
            }
            finally
            {
                _resetEvent.Set(); // 通知等待线程操作已完成
            }
        }

        /// <summary>
        /// Creates one element and returns its id. Throws with the reason when it can't be created.
        /// </summary>
        private long CreateElement(LineElement data)
        {
            long requestedTypeId = data.TypeId;

            // Step0 获取构件类型
            BuiltInCategory builtInCategory = BuiltInCategory.INVALID;
            Enum.TryParse(data.Category.Replace(".", ""), true, out builtInCategory);

            // Step1 获取标高和偏移 (offsets in ft)
            var (baseLevel, baseOffset, topLevel, topOffset) =
                LevelPlacement.Resolve(doc, data.LevelId, data.BaseLevel, data.BaseOffset, data.Height);

            // Step2 获取族类型
            FamilySymbol symbol = null;
            WallType wallType = null;
            DuctType ductType = null;

            if (data.TypeId != -1 && data.TypeId != 0)
            {
                ElementId typeELeId = data.TypeId.ToRevitElementId();
                if (typeELeId != null)
                {
                    Element typeEle = doc.GetElement(typeELeId);
                    if (typeEle != null && typeEle is FamilySymbol)
                    {
                        symbol = typeEle as FamilySymbol;
                        // 获取symbol的Category对象并转换为BuiltInCategory枚举
                        builtInCategory = (BuiltInCategory)symbol.Category.Id.GetIntValue();
                    }
                    else if (typeEle != null && typeEle is WallType)
                    {
                        wallType = typeEle as WallType;
                        builtInCategory = (BuiltInCategory)wallType.Category.Id.GetIntValue();
                    }
                    else if (typeEle != null && typeEle is DuctType)
                    {
                        ductType = typeEle as DuctType;
                        builtInCategory = (BuiltInCategory)ductType.Category.Id.GetIntValue();
                    }
                }
            }
            if (builtInCategory == BuiltInCategory.INVALID)
            {
                throw new ArgumentException(
                    $"Unknown category '{data.Category}'. Use a built-in category such as OST_Walls, OST_StructuralFraming or OST_DuctCurves.");
            }
            switch (builtInCategory)
            {
                case BuiltInCategory.OST_Walls:
                    if (wallType == null)
                    {
                        // Requested typeId was invalid or not provided, fall back to first available
                        wallType = new FilteredElementCollector(doc)
                            .OfClass(typeof(WallType))
                            .Cast<WallType>()
                            .FirstOrDefault()
                            ?? throw new InvalidOperationException("No wall types are available in the model.");
                        if (requestedTypeId != -1 && requestedTypeId != 0)
                        {
                            _warnings.Add($"Requested wall typeId {requestedTypeId} not found. Defaulted to '{wallType.Name}' (ID: {wallType.Id.GetValue()})");
                        }
                    }
                    break;
                case BuiltInCategory.OST_DuctCurves:
                    if (ductType == null)
                    {
                        // Requested typeId was invalid or not provided, fall back to first available rectangular duct
                        ductType = new FilteredElementCollector(doc)
                            .OfClass(typeof(DuctType))
                            .Cast<DuctType>()
                            .FirstOrDefault(d => d.Shape == ConnectorProfileType.Rectangular)
                            ?? throw new InvalidOperationException("No rectangular duct types are available in the model.");
                        if (requestedTypeId != -1 && requestedTypeId != 0)
                        {
                            _warnings.Add($"Requested duct typeId {requestedTypeId} not found. Defaulted to '{ductType.Name}' (ID: {ductType.Id.GetValue()})");
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
                        if (symbol == null)
                        {
                            throw new InvalidOperationException(
                                $"No family types for {builtInCategory} are loaded in the model. Load a family of that category first.");
                        }
                        if (requestedTypeId != -1 && requestedTypeId != 0)
                        {
                            _warnings.Add($"Requested typeId {requestedTypeId} not found. Defaulted to '{symbol.FamilyName}: {symbol.Name}' (ID: {symbol.Id.GetValue()})");
                        }
                    }
                    break;
            }

            // Step3 调用通用方法创建族实例
            using (Transaction transaction = new Transaction(doc, "Create line-based element"))
            {
                transaction.Start();
                Element created;
                switch (builtInCategory)
                {
                    case BuiltInCategory.OST_Walls:
                        created = Wall.Create
                        (
                          doc,
                          JZLine.ToLine(data.LocationLine),
                          wallType.Id,
                          baseLevel.Id,
                          data.Height / 304.8,
                          baseOffset,
                          false,
                          false
                        );
                        break;
                    case BuiltInCategory.OST_DuctCurves:
                        // 获取MEP系统类型（必需）
                        MEPSystemType mepSystemType = new FilteredElementCollector(doc)
                            .OfClass(typeof(MEPSystemType))
                            .Cast<MEPSystemType>()
                            .FirstOrDefault(m => m.SystemClassification == MEPSystemClassification.SupplyAir)
                            ?? throw new InvalidOperationException("Ducts need a supply air system type, and the model has none.");

                        Duct duct = Duct.Create(
                            doc,
                            mepSystemType.Id,
                            ductType.Id,
                            baseLevel.Id,
                            JZLine.ToLine(data.LocationLine).GetEndPoint(0),
                            JZLine.ToLine(data.LocationLine).GetEndPoint(1)
                        );

                        if (duct != null)
                        {
                            // 设置高度偏移
                            Parameter offsetParam = duct.get_Parameter(BuiltInParameter.RBS_OFFSET_PARAM);
                            if (offsetParam != null)
                                offsetParam.Set(baseOffset);
                        }
                        created = duct;
                        break;
                    default:
                        if (!symbol.IsActive)
                            symbol.Activate();

                        // 调用FamilyInstance通用创建方法
                        created = doc.CreateInstance(symbol, null, JZLine.ToLine(data.LocationLine), baseLevel, topLevel, baseOffset, topOffset);
                        break;
                }

                if (created == null)
                {
                    throw new InvalidOperationException($"Revit could not create the {builtInCategory} element on that line.");
                }

                //doc.Refresh();
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
            return "创建线状构件";
        }

        /// <summary>
        /// 创建或获取指定厚度的墙体类型
        /// </summary>
        /// <param name="doc">Revit文档</param>
        /// <param name="width">宽度（ft）</param>
        /// <returns></returns>
        /// <exception cref="InvalidOperationException"></exception>
        private WallType CreateOrGetWallType(Document doc, double width = 200 / 304.8)
        {
            // 如果没有有效的类型
            // 先查找是否存在指定厚度的建筑墙类型
            WallType existingType = new FilteredElementCollector(doc)
                                    .OfClass(typeof(WallType))
                                    .Cast<WallType>()
                                    .FirstOrDefault(w => w.Name == $"{_wallName}{width * 304.8}mm");
            if (existingType != null)
                return existingType;

            // 不存在则创建新的墙体类型，基于基本墙
            WallType baseWallType = new FilteredElementCollector(doc)
                                    .OfClass(typeof(WallType))
                                    .Cast<WallType>()
                                    .FirstOrDefault(w => w.Name.Contains("常规")); ;
            if (baseWallType == null)
            {
                baseWallType = new FilteredElementCollector(doc)
                                    .OfClass(typeof(WallType))
                                    .Cast<WallType>()
                                    .FirstOrDefault(); ;
            }

            if (baseWallType == null)
                throw new InvalidOperationException("未找到可用的基础墙类型");

            // 复制墙体类型
            WallType newWallType = null;
            newWallType = baseWallType.Duplicate($"{_wallName}{width * 304.8}mm") as WallType;

            // 设置墙厚
            CompoundStructure cs = newWallType.GetCompoundStructure();
            if (cs != null)
            {
                // 获取原始层的材料ID
                ElementId materialId = cs.GetLayers().First().MaterialId;

                // 创建新的单层结构
                CompoundStructureLayer newLayer = new CompoundStructureLayer(
                    width,  // 宽度（转换为英尺）
                    MaterialFunctionAssignment.Structure,  // 功能分配
                    materialId  // 材料ID
                );

                // 创建新的复合结构
                IList<CompoundStructureLayer> newLayers = new List<CompoundStructureLayer> { newLayer };
                cs.SetLayers(newLayers);

                // 应用新的复合结构
                newWallType.SetCompoundStructure(cs);
            }
            return newWallType;
        }

        /// <summary>
        /// 创建或获取指定尺寸的风管类型
        /// </summary>
        /// <param name="doc">Revit文档</param>
        /// <param name="width">宽度（ft）</param>
        /// <param name="height">高度（ft）</param>
        /// <returns>风管类型</returns>
        private DuctType CreateOrGetDuctType(Document doc, double width, double height)
        {
            string typeName = $"{_ductName}{width * 304.8}x{height * 304.8}mm";

            // 先查找是否存在指定尺寸的风管类型
            DuctType existingType = new FilteredElementCollector(doc)
                                    .OfClass(typeof(DuctType))
                                    .Cast<DuctType>()
                                    .FirstOrDefault(d => d.Name == typeName && d.Shape == ConnectorProfileType.Rectangular);

            if (existingType != null)
                return existingType;

            // 不存在则创建新的风管类型，基于已有的矩形风管类型
            DuctType baseDuctType = new FilteredElementCollector(doc)
                                    .OfClass(typeof(DuctType))
                                    .Cast<DuctType>()
                                    .FirstOrDefault(d => d.Shape == ConnectorProfileType.Rectangular);

            if (baseDuctType == null)
                throw new InvalidOperationException("未找到可用的基础矩形风管类型");

            // 复制风管类型
            DuctType newDuctType = baseDuctType.Duplicate(typeName) as DuctType;

            // 设置风管尺寸参数
            Parameter widthParam = newDuctType.get_Parameter(BuiltInParameter.RBS_CURVE_WIDTH_PARAM);
            Parameter heightParam = newDuctType.get_Parameter(BuiltInParameter.RBS_CURVE_HEIGHT_PARAM);

            if (widthParam != null && heightParam != null)
            {
                widthParam.Set(width);
                heightParam.Set(height);
            }

            return newDuctType;
        }

    }
}
