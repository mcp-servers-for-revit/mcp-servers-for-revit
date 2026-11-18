using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCPCommandSet.Models.Common;
using RevitMCPSDK.API.Interfaces;

namespace RevitMCPCommandSet.Services
{
    public class ModifyElementEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        private readonly ManualResetEvent _resetEvent = new ManualResetEvent(false);

        public ModifyElementSetting Settings { get; set; }
        public AIResult<object> Result { get; private set; }

        public void SetParameters(ModifyElementSetting settings)
        {
            Settings = settings;
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
                var elementIds = Settings.ElementIds
                    .Select(id => ToElementId(id))
                    .Where(id => doc.GetElement(id) != null)
                    .ToList();

                if (elementIds.Count == 0)
                {
                    Result = new AIResult<object>
                    {
                        Success = false,
                        Message = "No valid elements found"
                    };
                    return;
                }

                string action = Settings.Action?.ToLower() ?? "";

                using (var transaction = new Transaction(doc, $"Modify Elements - {action}"))
                {
                    transaction.Start();
                    try
                    {
                        switch (action)
                        {
                            case "move":
                                ExecuteMove(doc, elementIds);
                                break;
                            case "rotate":
                                ExecuteRotate(doc, elementIds);
                                break;
                            case "mirror":
                                ExecuteMirror(doc, elementIds);
                                break;
                            case "copy":
                                ExecuteCopy(doc, elementIds);
                                break;
                            default:
                                throw new ArgumentException($"Unknown action: {Settings.Action}");
                        }

                        transaction.Commit();
                    }
                    catch
                    {
                        if (transaction.GetStatus() == TransactionStatus.Started)
                            transaction.RollBack();
                        throw;
                    }
                }

                Result = new AIResult<object>
                {
                    Success = true,
                    Message = $"Successfully executed '{action}' on {elementIds.Count} elements"
                };
            }
            catch (Exception ex)
            {
                Result = new AIResult<object>
                {
                    Success = false,
                    Message = $"Modify element failed: {ex.Message}"
                };
            }
            finally
            {
                _resetEvent.Set();
            }
        }

        private void ExecuteMove(Document doc, List<ElementId> elementIds)
        {
            if (Settings.Translation == null)
                throw new ArgumentException("Translation is required for move action");

            XYZ translation = JZPoint.ToXYZ(Settings.Translation);
            ElementTransformUtils.MoveElements(doc, elementIds, translation);
        }

        private void ExecuteRotate(Document doc, List<ElementId> elementIds)
        {
            if (Settings.RotationCenter == null)
                throw new ArgumentException("RotationCenter is required for rotate action");

            XYZ center = JZPoint.ToXYZ(Settings.RotationCenter);
            double angleRadians = Settings.RotationAngle * Math.PI / 180.0;
            Line axis = Line.CreateBound(center, center + XYZ.BasisZ);

            ElementTransformUtils.RotateElements(doc, elementIds, axis, angleRadians);
        }

        private void ExecuteMirror(Document doc, List<ElementId> elementIds)
        {
            if (Settings.MirrorPlaneOrigin == null || Settings.MirrorPlaneNormal == null)
                throw new ArgumentException("MirrorPlaneOrigin and MirrorPlaneNormal are required for mirror action");

            XYZ origin = JZPoint.ToXYZ(Settings.MirrorPlaneOrigin);
            XYZ normal = new XYZ(
                Settings.MirrorPlaneNormal.X,
                Settings.MirrorPlaneNormal.Y,
                Settings.MirrorPlaneNormal.Z
            ).Normalize();

            Plane mirrorPlane = Plane.CreateByNormalAndOrigin(normal, origin);
            ElementTransformUtils.MirrorElements(doc, elementIds, mirrorPlane, false);
        }

        private void ExecuteCopy(Document doc, List<ElementId> elementIds)
        {
            if (Settings.CopyOffset == null)
                throw new ArgumentException("CopyOffset is required for copy action");

            XYZ offset = JZPoint.ToXYZ(Settings.CopyOffset);
            ElementTransformUtils.CopyElements(doc, elementIds, offset);
        }

        private static ElementId ToElementId(long id)
        {
#if REVIT2024_OR_GREATER
            return new ElementId(id);
#else
            return new ElementId((int)id);
#endif
        }

        public string GetName() => "Modify Elements";
    }
}
