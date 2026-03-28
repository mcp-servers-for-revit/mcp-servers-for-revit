using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using RevitMCPCommandSet.Models.Common;
using RevitMCPSDK.API.Interfaces;

namespace RevitMCPCommandSet.Services
{
    public class CreateFloorEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        private readonly ManualResetEvent _resetEvent = new ManualResetEvent(false);

        public List<Dictionary<string, double>> BoundaryPoints { get; set; } = new List<Dictionary<string, double>>();
        public long RoomId { get; set; } = 0;
        public string FloorTypeName { get; set; } = "";
        public double LevelElevation { get; set; } = 0;
        public bool IsStructural { get; set; } = false;
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

                // Get floor type
                FloorType floorType = GetFloorType(doc);
                if (floorType == null)
                    throw new Exception("No floor type found");

                // Get level
                Level level = GetLevel(doc);
                if (level == null)
                    throw new Exception("No suitable level found");

                // Get boundary
                CurveLoop boundary = GetBoundary(doc);
                if (boundary == null || boundary.Count() < 3)
                    throw new Exception("Invalid boundary - need at least 3 points");

                Floor floor;
                using (var transaction = new Transaction(doc, "Create Floor"))
                {
                    transaction.Start();
                    var curveLoops = new List<CurveLoop> { boundary };
                    floor = Floor.Create(doc, curveLoops, floorType.Id, level.Id);
                    transaction.Commit();
                }

                Result = new AIResult<object>
                {
                    Success = true,
                    Message = $"Successfully created floor '{floorType.Name}' on level '{level.Name}'",
                    Response = new
                    {
                        floorId = floor.Id.Value,
                        floorTypeName = floorType.Name,
                        levelName = level.Name
                    }
                };
            }
            catch (Exception ex)
            {
                Result = new AIResult<object>
                {
                    Success = false,
                    Message = $"Failed to create floor: {ex.Message}"
                };
            }
            finally
            {
                _resetEvent.Set();
            }
        }

        private FloorType GetFloorType(Document doc)
        {
            if (!string.IsNullOrEmpty(FloorTypeName))
            {
                return new FilteredElementCollector(doc)
                    .OfClass(typeof(FloorType))
                    .Cast<FloorType>()
                    .FirstOrDefault(ft => ft.Name.Equals(FloorTypeName, StringComparison.OrdinalIgnoreCase));
            }

            return new FilteredElementCollector(doc)
                .OfClass(typeof(FloorType))
                .Cast<FloorType>()
                .FirstOrDefault();
        }

        private Level GetLevel(Document doc)
        {
            var levels = new FilteredElementCollector(doc)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .OrderBy(l => l.Elevation)
                .ToList();

            if (RoomId != 0)
            {
                var room = doc.GetElement(ToElementId(RoomId)) as Room;
                if (room?.Level != null) return room.Level;
            }

            if (LevelElevation != 0)
            {
                double elevFeet = LevelElevation / 304.8;
                return levels.OrderBy(l => Math.Abs(l.Elevation - elevFeet)).FirstOrDefault();
            }

            return levels.FirstOrDefault();
        }

        private CurveLoop GetBoundary(Document doc)
        {
            if (RoomId != 0)
            {
                return GetBoundaryFromRoom(doc);
            }

            if (BoundaryPoints.Count >= 3)
            {
                return GetBoundaryFromPoints();
            }

            throw new Exception("Either boundaryPoints or roomId must be provided");
        }

        private CurveLoop GetBoundaryFromRoom(Document doc)
        {
            var room = doc.GetElement(ToElementId(RoomId)) as Room;
            if (room == null) throw new Exception($"Room with ID {RoomId} not found");

            var segments = room.GetBoundarySegments(new SpatialElementBoundaryOptions());
            if (segments == null || segments.Count == 0)
                throw new Exception("Room has no boundary segments");

            var curveLoop = new CurveLoop();
            foreach (var segment in segments[0])
            {
                curveLoop.Append(segment.GetCurve());
            }
            return curveLoop;
        }

        private CurveLoop GetBoundaryFromPoints()
        {
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
            return curveLoop;
        }

        private ElementId ToElementId(long id)
        {
            return new ElementId(id);
        }

        public string GetName() => "Create Floor";
    }
}
