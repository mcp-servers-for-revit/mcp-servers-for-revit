using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCPCommandSet.Models.Common;
using RevitMCPSDK.API.Interfaces;

namespace RevitMCPCommandSet.Services
{
    public class CopyElementsEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        private readonly ManualResetEvent _resetEvent = new ManualResetEvent(false);

        public List<long> ElementIds { get; set; } = new List<long>();
        public long SourceViewId { get; set; } = 0;
        public long TargetViewId { get; set; } = 0;
        public double OffsetX { get; set; } = 0;
        public double OffsetY { get; set; } = 0;
        public double OffsetZ { get; set; } = 0;
        public AIResult<object> Result { get; private set; }

        public void SetParameters(List<long> elementIds, long sourceViewId, long targetViewId, double offsetX, double offsetY, double offsetZ)
        {
            ElementIds = elementIds ?? new List<long>();
            SourceViewId = sourceViewId;
            TargetViewId = targetViewId;
            OffsetX = offsetX;
            OffsetY = offsetY;
            OffsetZ = offsetZ;
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

                if (ElementIds.Count == 0)
                    throw new ArgumentException("elementIds is required");
                if (SourceViewId == 0)
                    throw new ArgumentException("sourceViewId is required");
                if (TargetViewId == 0)
                    throw new ArgumentException("targetViewId is required");

                var sourceView = doc.GetElement(ToElementId(SourceViewId)) as View;
                var targetView = doc.GetElement(ToElementId(TargetViewId)) as View;

                if (sourceView == null)
                    throw new Exception($"Source view with ID {SourceViewId} not found");
                if (targetView == null)
                    throw new Exception($"Target view with ID {TargetViewId} not found");

                var ids = ElementIds.Select(id => ToElementId(id)).ToList();
                var transform = Transform.CreateTranslation(
                    new XYZ(OffsetX / 304.8, OffsetY / 304.8, OffsetZ / 304.8));

                ICollection<ElementId> copiedIds;
                using (var transaction = new Transaction(doc, "Copy Elements Between Views"))
                {
                    transaction.Start();
                    try
                    {
                        copiedIds = ElementTransformUtils.CopyElements(
                            sourceView, ids, targetView, transform, new CopyPasteOptions());
                        transaction.Commit();
                    }
                    catch
                    {
                        if (transaction.GetStatus() == TransactionStatus.Started)
                            transaction.RollBack();
                        throw;
                    }
                }

                var copiedElements = new List<object>();
                foreach (var id in copiedIds)
                {
                    var elem = doc.GetElement(id);
                    copiedElements.Add(new
                    {
#if REVIT2024_OR_GREATER
                        id = id.Value,
#else
                        id = id.IntegerValue,
#endif
                        name = elem?.Name ?? "",
                        category = elem?.Category?.Name ?? ""
                    });
                }

                Result = new AIResult<object>
                {
                    Success = true,
                    Message = $"Copied {copiedIds.Count} elements from '{sourceView.Name}' to '{targetView.Name}'",
                    Response = new
                    {
                        copiedCount = copiedIds.Count,
                        copiedElements
                    }
                };
            }
            catch (Exception ex)
            {
                Result = new AIResult<object>
                {
                    Success = false,
                    Message = $"Copy elements failed: {ex.Message}"
                };
            }
            finally
            {
                _resetEvent.Set();
            }
        }

        private ElementId ToElementId(long id)
        {
#if REVIT2024_OR_GREATER
            return new ElementId(id);
#else
            return new ElementId((int)id);
#endif
        }

        public string GetName() => "Copy Elements";
    }
}
