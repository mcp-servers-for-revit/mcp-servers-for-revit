using Autodesk.Revit.UI;
using RevitMCPSDK.API.Interfaces;
using RevitMCPCommandSet.Models.Common;
using RevitMCPCommandSet.Utils;

namespace RevitMCPCommandSet.Services
{
    public class CreatePointElementEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
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
        public List<PointElement> CreatedInfo { get; private set; }
        /// <summary>
        /// 执行结果（传出数据）
        /// </summary>
        public AIResult<List<long>> Result { get; private set; }
        private List<string> _warnings = new List<string>();

        /// <summary>
        /// 设置创建的参数
        /// </summary>
        public void SetParameters(List<PointElement> data)
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
                    PointElement data = CreatedInfo[i];
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
                    Message = $"Error creating point-based elements: {ex.Message}",
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
        private long CreateElement(PointElement data)
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
                }
            }
            if (builtInCategory == BuiltInCategory.INVALID)
            {
                throw new ArgumentException(
                    $"Unknown category '{data.Category}'. Use a built-in category such as OST_Doors, OST_Windows or OST_Furniture.");
            }
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

            // Step3 调用通用方法创建族实例
            using (Transaction transaction = new Transaction(doc, "Create point-based element"))
            {
                transaction.Start();

                if (!symbol.IsActive)
                    symbol.Activate();

                // Resolve explicit host wall if provided
                Element explicitHost = null;
                if (data.HostWallId > 0)
                {
                    ElementId hostId = data.HostWallId.ToRevitElementId();
                    Element hostElem = doc.GetElement(hostId);
                    if (hostElem is Wall)
                    {
                        explicitHost = hostElem;
                    }
                    else
                    {
                        _warnings.Add($"Requested hostWallId {data.HostWallId} is not a valid wall. Using auto-detection.");
                    }
                }

                var instance = doc.CreateInstance(
                    symbol,
                    JZPoint.ToXYZ(data.LocationPoint),
                    null,           // locationLine
                    baseLevel,
                    topLevel,
                    baseOffset,
                    topOffset,
                    null,           // faceDirection
                    null,           // handDirection
                    null,           // view
                    explicitHost,   // explicit host wall
                    true);          // snap to host center

                if (instance == null)
                {
                    throw new InvalidOperationException(
                        $"Revit could not place '{symbol.FamilyName}: {symbol.Name}' at that location.");
                }

                // Handle orientation for doors and windows
                if (builtInCategory == BuiltInCategory.OST_Doors ||
                    builtInCategory == BuiltInCategory.OST_Windows)
                {
                    doc.Regenerate();

                    bool shouldFlip = data.FacingFlipped;

                    // Auto-detect facing based on which side of the wall
                    // the original (pre-snap) placement point was on
                    if (!shouldFlip)
                    {
                        Wall hostWall = instance.Host as Wall;
                        if (hostWall != null)
                        {
                            LocationCurve locCurve = hostWall.Location as LocationCurve;
                            if (locCurve != null)
                            {
                                XYZ originalPt = JZPoint.ToXYZ(data.LocationPoint);
                                XYZ wallStart = locCurve.Curve.GetEndPoint(0);
                                XYZ wallEnd = locCurve.Curve.GetEndPoint(1);
                                XYZ wallDir = new XYZ(wallEnd.X - wallStart.X, wallEnd.Y - wallStart.Y, 0).Normalize();
                                XYZ wallNormal = wallDir.CrossProduct(XYZ.BasisZ).Normalize();

                                IntersectionResult ir = locCurve.Curve.Project(originalPt);
                                if (ir != null)
                                {
                                    XYZ centerPt = ir.XYZPoint;
                                    double side = (originalPt - centerPt).DotProduct(wallNormal);

                                    // If the point is on the negative-normal side but
                                    // instance faces positive-normal (or vice versa), flip
                                    double facingDot = instance.FacingOrientation.DotProduct(wallNormal);
                                    if ((side < -1e-10 && facingDot > 0) ||
                                        (side > 1e-10 && facingDot < 0))
                                    {
                                        shouldFlip = true;
                                    }
                                }
                            }
                        }
                    }

                    if (shouldFlip)
                    {
                        instance.flipFacing();
                        doc.Regenerate();
                    }
                }

                // Handle rotation for non-hosted elements (furniture, generic models)
                if (data.Rotation != 0 &&
                    builtInCategory != BuiltInCategory.OST_Doors &&
                    builtInCategory != BuiltInCategory.OST_Windows)
                {
                    XYZ origin = JZPoint.ToXYZ(data.LocationPoint);
                    Line rotationAxis = Line.CreateBound(origin, origin + XYZ.BasisZ);
                    double angleRadians = data.Rotation * Math.PI / 180.0;
                    ElementTransformUtils.RotateElement(doc, instance.Id, rotationAxis, angleRadians);
                }

                transaction.Commit();
                return instance.Id.GetValue();
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
            return "创建点状构件";
        }

    }
}
