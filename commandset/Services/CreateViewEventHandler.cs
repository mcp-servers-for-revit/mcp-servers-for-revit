using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCPCommandSet.Models.Common;
using RevitMCPCommandSet.Models.Views;
using RevitMCPSDK.API.Interfaces;

namespace RevitMCPCommandSet.Services
{
    public class CreateViewEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        private readonly ManualResetEvent _resetEvent = new ManualResetEvent(false);

        public ViewCreationInfo ViewInfo { get; set; }
        public AIResult<object> Result { get; private set; }

        public void SetParameters(ViewCreationInfo viewInfo)
        {
            ViewInfo = viewInfo;
            _resetEvent.Reset();
        }

        public bool WaitForCompletion(int timeoutMilliseconds = 10000)
        {
            return _resetEvent.WaitOne(timeoutMilliseconds);
        }

        public void Execute(UIApplication app)
        {
            try
            {
                var doc = app.ActiveUIDocument.Document;
                string viewType = ViewInfo.ViewType?.ToLower() ?? "floorplan";

                using (var transaction = new Transaction(doc, $"Create {viewType} View"))
                {
                    transaction.Start();
                    try
                    {
                        object result;

                        switch (viewType)
                        {
                            case "section":
                                result = CreateSectionView(doc);
                                break;
                            case "3d":
                            case "threed":
                                result = Create3DView(doc);
                                break;
                            case "elevation":
                                result = CreateElevationView(doc);
                                break;
                            case "floorplan":
                                result = CreateFloorPlanView(doc);
                                break;
                            case "ceilingplan":
                                result = CreateCeilingPlanView(doc);
                                break;
                            default:
                                throw new ArgumentException($"Unsupported view type: {ViewInfo.ViewType}");
                        }

                        transaction.Commit();

                        Result = new AIResult<object>
                        {
                            Success = true,
                            Message = $"Successfully created {viewType} view '{ViewInfo.Name}'",
                            Response = result
                        };
                    }
                    catch
                    {
                        if (transaction.GetStatus() == TransactionStatus.Started)
                            transaction.RollBack();
                        throw;
                    }
                }
            }
            catch (Exception ex)
            {
                Result = new AIResult<object>
                {
                    Success = false,
                    Message = $"Failed to create view: {ex.Message}"
                };
            }
            finally
            {
                _resetEvent.Set();
            }
        }

        private object CreateSectionView(Document doc)
        {
            var vft = FindViewFamilyType(doc, ViewFamily.Section);
            if (vft == null)
                throw new InvalidOperationException("No Section view family type found");

            var sectionBox = new BoundingBoxXYZ();

            if (ViewInfo.Direction != null)
            {
                XYZ direction = new XYZ(ViewInfo.Direction.X, ViewInfo.Direction.Y, ViewInfo.Direction.Z).Normalize();
                XYZ up = XYZ.BasisZ;
                XYZ right = direction.CrossProduct(up).Normalize();
                if (right.IsZeroLength()) right = XYZ.BasisX;
                up = right.CrossProduct(direction).Normalize();

                Transform transform = Transform.Identity;
                transform.Origin = ViewInfo.LevelElevation != 0
                    ? new XYZ(0, 0, ViewInfo.LevelElevation / 304.8)
                    : XYZ.Zero;
                transform.BasisX = right;
                transform.BasisY = up;
                transform.BasisZ = direction;

                sectionBox.Transform = transform;
            }

            sectionBox.Min = new XYZ(-5000 / 304.8, -2500 / 304.8, 0);
            sectionBox.Max = new XYZ(5000 / 304.8, 2500 / 304.8, 20000 / 304.8);

            var section = ViewSection.CreateSection(doc, vft.Id, sectionBox);

            if (!string.IsNullOrEmpty(ViewInfo.Name))
                section.Name = ViewInfo.Name;

            if (ViewInfo.Scale > 0)
                section.Scale = ViewInfo.Scale;

            ApplyDetailLevel(section);

            return MakeResult(section, "Section");
        }

        private object Create3DView(Document doc)
        {
            var vft = FindViewFamilyType(doc, ViewFamily.ThreeDimensional);
            if (vft == null)
                throw new InvalidOperationException("No 3D view family type found");

            var view3D = View3D.CreateIsometric(doc, vft.Id);

            if (!string.IsNullOrEmpty(ViewInfo.Name))
                view3D.Name = ViewInfo.Name;

            if (ViewInfo.Scale > 0)
                view3D.Scale = ViewInfo.Scale;

            ApplyDetailLevel(view3D);

            return MakeResult(view3D, "3D");
        }

        private object CreateElevationView(Document doc)
        {
            var vft = FindViewFamilyType(doc, ViewFamily.Elevation);
            if (vft == null)
                throw new InvalidOperationException("No Elevation view family type found");

            XYZ location = ViewInfo.LevelElevation != 0
                ? new XYZ(0, 0, ViewInfo.LevelElevation / 304.8)
                : XYZ.Zero;

            var marker = ElevationMarker.CreateElevationMarker(doc, vft.Id, location, ViewInfo.Scale > 0 ? ViewInfo.Scale : 100);
            var elevationView = marker.CreateElevation(doc, doc.ActiveView.Id, 0);

            if (!string.IsNullOrEmpty(ViewInfo.Name))
                elevationView.Name = ViewInfo.Name;

            ApplyDetailLevel(elevationView);

            return MakeResult(elevationView, "Elevation");
        }

        private object CreateFloorPlanView(Document doc)
        {
            var vft = FindViewFamilyType(doc, ViewFamily.FloorPlan);
            if (vft == null)
                throw new InvalidOperationException("No FloorPlan view family type found");

            Level level = FindOrCreateLevel(doc);
            var floorPlan = ViewPlan.Create(doc, vft.Id, level.Id);

            if (!string.IsNullOrEmpty(ViewInfo.Name))
                floorPlan.Name = ViewInfo.Name;

            if (ViewInfo.Scale > 0)
                floorPlan.Scale = ViewInfo.Scale;

            ApplyDetailLevel(floorPlan);

            return MakeResult(floorPlan, "FloorPlan");
        }

        private object CreateCeilingPlanView(Document doc)
        {
            var vft = FindViewFamilyType(doc, ViewFamily.CeilingPlan);
            if (vft == null)
                throw new InvalidOperationException("No CeilingPlan view family type found");

            Level level = FindOrCreateLevel(doc);
            var ceilingPlan = ViewPlan.Create(doc, vft.Id, level.Id);

            if (!string.IsNullOrEmpty(ViewInfo.Name))
                ceilingPlan.Name = ViewInfo.Name;

            if (ViewInfo.Scale > 0)
                ceilingPlan.Scale = ViewInfo.Scale;

            ApplyDetailLevel(ceilingPlan);

            return MakeResult(ceilingPlan, "CeilingPlan");
        }

        private ViewFamilyType FindViewFamilyType(Document doc, ViewFamily family)
        {
            var collector = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewFamilyType));

            if (!string.IsNullOrEmpty(ViewInfo.ViewFamilyTypeName))
            {
                var byName = collector.Cast<ViewFamilyType>()
                    .FirstOrDefault(v => v.Name.Equals(ViewInfo.ViewFamilyTypeName, StringComparison.OrdinalIgnoreCase));
                if (byName != null) return byName;
            }

            return collector.Cast<ViewFamilyType>()
                .FirstOrDefault(v => v.ViewFamily == family);
        }

        private Level FindOrCreateLevel(Document doc)
        {
            double elevationFt = ViewInfo.LevelElevation / 304.8;

            var levels = new FilteredElementCollector(doc)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .OrderBy(l => Math.Abs(l.Elevation - elevationFt))
                .ToList();

            if (levels.Count > 0 && Math.Abs(levels[0].Elevation - elevationFt) < 0.01)
                return levels[0];

            if (ViewInfo.LevelElevation == 0 && levels.Count > 0)
                return levels[0];

            return Level.Create(doc, elevationFt);
        }

        private void ApplyDetailLevel(View view)
        {
            switch (ViewInfo.DetailLevel?.ToLower())
            {
                case "coarse":
                    view.DetailLevel = ViewDetailLevel.Coarse;
                    break;
                case "fine":
                    view.DetailLevel = ViewDetailLevel.Fine;
                    break;
                default:
                    view.DetailLevel = ViewDetailLevel.Medium;
                    break;
            }
        }

        private object MakeResult(View view, string type)
        {
            return new
            {
#if REVIT2024_OR_GREATER
                viewId = view.Id.Value,
#else
                viewId = view.Id.IntegerValue,
#endif
                name = view.Name,
                viewType = type
            };
        }

        public string GetName() => "Create View";
    }
}
