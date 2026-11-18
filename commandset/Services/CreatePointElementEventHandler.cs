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
        /// Event wait handle
        /// </summary>
        private readonly ManualResetEvent _resetEvent = new ManualResetEvent(false);
        /// <summary>
        /// Creation data (input data)
        /// </summary>
        public List<PointElement> CreatedInfo { get; private set; }
        /// <summary>
        /// Execution result (output data)
        /// </summary>
        public AIResult<List<int>> Result { get; private set; }
        private List<string> _warnings = new List<string>();

        /// <summary>
        /// Set creation parameters
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
                var elementIds = new List<int>();
                _warnings.Clear();
                foreach (var data in CreatedInfo)
                {
                    int requestedTypeId = data.TypeId;

                    // Step0 Get element type
                    BuiltInCategory builtInCategory = BuiltInCategory.INVALID;
                    Enum.TryParse(data.Category.Replace(".", ""), true, out builtInCategory);

                    // Step1 Get level and offset
                    Level baseLevel = null;
                    Level topLevel = null;
                    double topOffset = -1;  // ft
                    double baseOffset = -1; // ft
                    baseLevel = doc.FindNearestLevel(data.BaseLevel / 304.8);
                    baseOffset = (data.BaseOffset + data.BaseLevel) / 304.8 - baseLevel.Elevation;
                    topLevel = doc.FindNearestLevel((data.BaseLevel + data.BaseOffset + data.Height) / 304.8);
                    topOffset = (data.BaseLevel + data.BaseOffset + data.Height) / 304.8 - topLevel.Elevation;
                    if (baseLevel == null)
                        continue;

                    // Step2 Get family type
                    FamilySymbol symbol = null;
                    if (data.TypeId != -1 && data.TypeId != 0)
                    {
                        ElementId typeELeId = new ElementId(data.TypeId);
                        if (typeELeId != null)
                        {
                            Element typeEle = doc.GetElement(typeELeId);
                            if (typeEle != null && typeEle is FamilySymbol)
                            {
                                symbol = typeEle as FamilySymbol;
                                // Get the symbol's Category object and convert to BuiltInCategory enum
                                builtInCategory = (BuiltInCategory)symbol.Category.Id.GetIntValue();
                            }
                        }
                    }
                    if (builtInCategory == BuiltInCategory.INVALID)
                        continue;
                    if (symbol == null)
                    {
                        symbol = new FilteredElementCollector(doc)
                            .OfClass(typeof(FamilySymbol))
                            .OfCategory(builtInCategory)
                            .Cast<FamilySymbol>()
                            .FirstOrDefault(fs => fs.IsActive); // Get active type as default type
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
                            _warnings.Add($"No family types available for category {builtInCategory}.");
                            continue;
                        }
                        if (requestedTypeId != -1 && requestedTypeId != 0)
                        {
                            _warnings.Add($"Requested typeId {requestedTypeId} not found. Defaulted to '{symbol.FamilyName}: {symbol.Name}' (ID: {symbol.Id.GetValue()})");
                        }
                    }
                    if (symbol == null)
                        continue;

                    // Step3 Call generic method to create family instance
                    using (Transaction transaction = new Transaction(doc, "Create Point-Based Element"))
                    {
                        transaction.Start();

                        if (!symbol.IsActive)
                            symbol.Activate();

                        // Resolve explicit host wall if provided
                        Element explicitHost = null;
                        if (data.HostWallId > 0)
                        {
                            ElementId hostId = new ElementId(data.HostWallId);
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

                        if (instance != null)
                        {
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

                            elementIds.Add(instance.Id.GetIntValue());
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
                    Message = $"Error creating point-based element: {ex.Message}",
                };
            }
            finally
            {
                _resetEvent.Set(); // Notify waiting thread that operation is complete
            }
        }

        /// <summary>
        /// Wait for creation to complete
        /// </summary>
        /// <param name="timeoutMilliseconds">Timeout duration (milliseconds)</param>
        /// <returns>Whether the operation completed before timeout</returns>
        public bool WaitForCompletion(int timeoutMilliseconds = 10000)
        {
            _resetEvent.Reset();
        return _resetEvent.WaitOne(timeoutMilliseconds);
        }

        /// <summary>
        /// IExternalEventHandler.GetName implementation
        /// </summary>
        public string GetName()
        {
            return "Create Point-Based Element";
        }

    }
}
