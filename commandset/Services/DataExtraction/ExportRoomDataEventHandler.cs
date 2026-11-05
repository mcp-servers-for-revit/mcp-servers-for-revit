using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using RevitMCPCommandSet.Models.DataExtraction;
using RevitMCPSDK.API.Interfaces;

namespace RevitMCPCommandSet.Services.DataExtraction
{
    public class ExportRoomDataEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        private bool _includeUnplacedRooms;
        private bool _includeNotEnclosedRooms;

        public ExportRoomDataResult ResultInfo { get; private set; }
        public bool TaskCompleted { get; private set; }
        private readonly ManualResetEvent _resetEvent = new ManualResetEvent(false);

        public void SetParameters(bool includeUnplacedRooms = false, bool includeNotEnclosedRooms = false)
        {
            _includeUnplacedRooms = includeUnplacedRooms;
            _includeNotEnclosedRooms = includeNotEnclosedRooms;
            TaskCompleted = false;
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
                var rooms = new List<RoomDataModel>();
                double totalArea = 0;

                // Collect all rooms in the project
                var roomCollector = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_Rooms)
                    .WhereElementIsNotElementType()
                    .Cast<Room>();

                foreach (Room room in roomCollector)
                {
                    // Skip unplaced rooms if not included
                    if (!_includeUnplacedRooms && room.Area == 0)
                        continue;

                    // Skip not enclosed rooms if not included
                    if (!_includeNotEnclosedRooms && room.Area == 0)
                        continue;

                    var roomData = new RoomDataModel
                    {
#if REVIT2024_OR_GREATER
                        Id = room.Id.Value,
#else
                        Id = room.Id.IntegerValue,
#endif
                        UniqueId = room.UniqueId,
                        Name = room.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString() ?? "",
                        Number = room.Number ?? "",
                        Level = room.Level?.Name ?? "No Level",
                        Area = room.Area, // Already in square feet
                        Volume = room.Volume, // Already in cubic feet
                        Perimeter = room.Perimeter, // Already in feet
                        UnboundedHeight = room.UnboundedHeight, // Already in feet
                        Department = room.get_Parameter(BuiltInParameter.ROOM_DEPARTMENT)?.AsString() ?? "",
                        Comments = room.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.AsString() ?? "",
                        Phase = doc.GetElement(room.get_Parameter(BuiltInParameter.ROOM_PHASE)?.AsElementId())?.Name ?? "",
                        Occupancy = room.get_Parameter(BuiltInParameter.ROOM_OCCUPANCY)?.AsString() ?? ""
                    };

                    rooms.Add(roomData);
                    totalArea += room.Area;
                }

                ResultInfo = new ExportRoomDataResult
                {
                    TotalRooms = rooms.Count,
                    TotalArea = totalArea,
                    Rooms = rooms,
                    Success = true,
                    Message = $"Successfully exported {rooms.Count} rooms"
                };
            }
            catch (Exception ex)
            {
                ResultInfo = new ExportRoomDataResult
                {
                    Success = false,
                    Message = $"Error exporting room data: {ex.Message}"
                };
            }
            finally
            {
                TaskCompleted = true;
                _resetEvent.Set();
            }
        }

        public string GetName()
        {
            return "Export Room Data";
        }
    }
}
