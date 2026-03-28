using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCPCommandSet.Models.Common;
using RevitMCPSDK.API.Interfaces;

namespace RevitMCPCommandSet.Services
{
    public class CreateArrayEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        private readonly ManualResetEvent _resetEvent = new ManualResetEvent(false);

        public List<long> ElementIds { get; set; } = new List<long>();
        public string ArrayType { get; set; } = "linear";
        public int Count { get; set; } = 1;
        public double SpacingX { get; set; } = 0;
        public double SpacingY { get; set; } = 0;
        public double SpacingZ { get; set; } = 0;
        public double CenterX { get; set; } = 0;
        public double CenterY { get; set; } = 0;
        public double TotalAngle { get; set; } = 360;
        public AIResult<object> Result { get; private set; }

        public bool WaitForCompletion(int timeoutMilliseconds = 10000)
        {
            _resetEvent.Reset();
            return _resetEvent.WaitOne(timeoutMilliseconds);
        }

        public void Execute(UIApplication app)
        {
            try
            {
                var doc = app.ActiveUIDocument.Document;

                if (ElementIds.Count == 0)
                    throw new ArgumentException("elementIds is required");
                if (Count < 1)
                    throw new ArgumentException("count must be at least 1");

                var allCopiedIds = new List<ElementId>();

                using (var transaction = new Transaction(doc, "Create Array"))
                {
                    transaction.Start();

                    var ids = ElementIds.Select(id => ToElementId(id)).ToList();

                    if (ArrayType.ToLower() == "linear")
                    {
                        var offset = new XYZ(SpacingX / 304.8, SpacingY / 304.8, SpacingZ / 304.8);
                        for (int i = 0; i < Count; i++)
                        {
                            var translation = offset * (i + 1);
                            var copiedIds = ElementTransformUtils.CopyElements(doc, ids, translation);
                            allCopiedIds.AddRange(copiedIds);
                        }
                    }
                    else if (ArrayType.ToLower() == "radial")
                    {
                        var center = new XYZ(CenterX / 304.8, CenterY / 304.8, 0);
                        var axis = Line.CreateBound(center, center + XYZ.BasisZ);
                        double angleStep = (TotalAngle / (Count + 1)) * Math.PI / 180.0;

                        for (int i = 0; i < Count; i++)
                        {
                            double angle = angleStep * (i + 1);
                            var copiedIds = ElementTransformUtils.CopyElements(doc, ids, XYZ.Zero);
                            foreach (var copiedId in copiedIds)
                            {
                                ElementTransformUtils.RotateElement(doc, copiedId, axis, angle);
                            }
                            allCopiedIds.AddRange(copiedIds);
                        }
                    }
                    else
                    {
                        throw new ArgumentException($"Unknown array type: {ArrayType}");
                    }

                    transaction.Commit();
                }

                var copiedElements = new List<object>();
                foreach (var id in allCopiedIds)
                {
                    var elem = doc.GetElement(id);
                    copiedElements.Add(new
                    {
                        id = id.Value,
                        name = elem?.Name ?? "",
                        category = elem?.Category?.Name ?? ""
                    });
                }

                Result = new AIResult<object>
                {
                    Success = true,
                    Message = $"Created {ArrayType} array with {allCopiedIds.Count} new elements ({Count} copies of {ElementIds.Count} elements)",
                    Response = new
                    {
                        arrayType = ArrayType,
                        copyCount = Count,
                        totalNewElements = allCopiedIds.Count,
                        elements = copiedElements
                    }
                };
            }
            catch (Exception ex)
            {
                Result = new AIResult<object>
                {
                    Success = false,
                    Message = $"Create array failed: {ex.Message}"
                };
            }
            finally
            {
                _resetEvent.Set();
            }
        }

        private ElementId ToElementId(long id)
        {
            return new ElementId(id);
        }

        public string GetName() => "Create Array";
    }
}
