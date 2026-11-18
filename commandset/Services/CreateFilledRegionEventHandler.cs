using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCPCommandSet.Models.Common;
using RevitMCPSDK.API.Interfaces;

namespace RevitMCPCommandSet.Services
{
    public class CreateFilledRegionEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        private readonly ManualResetEvent _resetEvent = new ManualResetEvent(false);

        public List<Dictionary<string, double>> BoundaryPoints { get; set; } = new List<Dictionary<string, double>>();
        public long ViewId { get; set; } = 0;
        public string FilledRegionTypeName { get; set; } = "";
        public AIResult<object> Result { get; private set; }

        public void SetParameters(List<Dictionary<string, double>> boundaryPoints, long viewId, string filledRegionTypeName)
        {
            BoundaryPoints = boundaryPoints ?? new List<Dictionary<string, double>>();
            ViewId = viewId;
            FilledRegionTypeName = filledRegionTypeName ?? "";
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

                if (BoundaryPoints.Count < 3)
                    throw new ArgumentException("Need at least 3 boundary points");

                var view = ViewId > 0
                    ? doc.GetElement(ToElementId(ViewId)) as View
                    : doc.ActiveView;

                if (view == null)
                    throw new Exception("View not found");

                // Get filled region type
                var regionType = GetFilledRegionType(doc);
                if (regionType == null)
                    throw new Exception("No filled region type found");

                // Build boundary curve loop
                var curveLoop = new CurveLoop();
                for (int i = 0; i < BoundaryPoints.Count; i++)
                {
                    var p1 = BoundaryPoints[i];
                    var p2 = BoundaryPoints[(i + 1) % BoundaryPoints.Count];

                    var start = new XYZ(
                        (p1.ContainsKey("x") ? p1["x"] : 0) / 304.8,
                        (p1.ContainsKey("y") ? p1["y"] : 0) / 304.8,
                        0);
                    var end = new XYZ(
                        (p2.ContainsKey("x") ? p2["x"] : 0) / 304.8,
                        (p2.ContainsKey("y") ? p2["y"] : 0) / 304.8,
                        0);

                    curveLoop.Append(Line.CreateBound(start, end));
                }

                FilledRegion filledRegion;
                using (var transaction = new Transaction(doc, "Create Filled Region"))
                {
                    transaction.Start();
                    try
                    {
                        filledRegion = FilledRegion.Create(doc, regionType.Id, view.Id, new List<CurveLoop> { curveLoop });
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
                    Message = $"Created filled region '{regionType.Name}' in view '{view.Name}'",
                    Response = new
                    {
#if REVIT2024_OR_GREATER
                        filledRegionId = filledRegion.Id.Value,
#else
                        filledRegionId = filledRegion.Id.IntegerValue,
#endif
                        typeName = regionType.Name,
                        viewName = view.Name
                    }
                };
            }
            catch (Exception ex)
            {
                Result = new AIResult<object> { Success = false, Message = $"Create filled region failed: {ex.Message}" };
            }
            finally
            {
                _resetEvent.Set();
            }
        }

        private FilledRegionType GetFilledRegionType(Document doc)
        {
            var types = new FilteredElementCollector(doc)
                .OfClass(typeof(FilledRegionType))
                .Cast<FilledRegionType>()
                .ToList();

            if (!string.IsNullOrEmpty(FilledRegionTypeName))
            {
                var match = types.FirstOrDefault(t =>
                    t.Name.Equals(FilledRegionTypeName, StringComparison.OrdinalIgnoreCase));
                if (match != null) return match;
            }

            return types.FirstOrDefault();
        }

        private ElementId ToElementId(long id)
        {
#if REVIT2024_OR_GREATER
            return new ElementId(id);
#else
            return new ElementId((int)id);
#endif
        }

        public string GetName() => "Create Filled Region";
    }
}
