using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using RevitMCPSDK.API.Interfaces;

namespace RevitMCPCommandSet.Services.DataExtraction
{
    public class ExportRoomDataEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        private bool _includeUnplacedRooms;
        private bool _includeNotEnclosedRooms;
        private int _maxResults;
        private List<string> _fields;

        public object ResultInfo { get; private set; }
        public bool TaskCompleted { get; private set; }
        private readonly ManualResetEvent _resetEvent = new ManualResetEvent(false);

        // All available field names for validation
        private static readonly HashSet<string> AllFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Name", "Number", "Level", "Area", "Volume", "Perimeter",
            "UnboundedHeight", "Department", "Comments", "Phase", "Occupancy", "Id", "UniqueId"
        };

        public void SetParameters(bool includeUnplacedRooms = false, bool includeNotEnclosedRooms = false,
            int maxResults = 100, List<string> fields = null)
        {
            _includeUnplacedRooms = includeUnplacedRooms;
            _includeNotEnclosedRooms = includeNotEnclosedRooms;
            _maxResults = maxResults > 0 ? maxResults : 100;
            _fields = fields != null && fields.Count > 0
                ? fields.Where(f => AllFields.Contains(f)).ToList()
                : null;
            TaskCompleted = false;
            _resetEvent.Reset();
        }

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
                var rooms = new List<Dictionary<string, object>>();
                double totalArea = 0;
                int totalCount = 0;
                bool useAllFields = _fields == null;
                var fieldSet = _fields != null ? new HashSet<string>(_fields, StringComparer.OrdinalIgnoreCase) : null;

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

                    totalCount++;
                    totalArea += room.Area;

                    // Only build room data if we haven't hit maxResults yet
                    if (rooms.Count >= _maxResults)
                        continue;

                    var roomData = new Dictionary<string, object>();

                    if (useAllFields || fieldSet.Contains("Id"))
                    {
#if REVIT2024_OR_GREATER
                        roomData["id"] = room.Id.Value;
#else
                        roomData["id"] = room.Id.IntegerValue;
#endif
                    }
                    if (useAllFields || fieldSet.Contains("UniqueId"))
                        roomData["uniqueId"] = room.UniqueId;
                    if (useAllFields || fieldSet.Contains("Name"))
                        roomData["name"] = room.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString() ?? "";
                    if (useAllFields || fieldSet.Contains("Number"))
                        roomData["number"] = room.Number ?? "";
                    if (useAllFields || fieldSet.Contains("Level"))
                        roomData["level"] = room.Level?.Name ?? "No Level";
                    if (useAllFields || fieldSet.Contains("Area"))
                        roomData["area"] = room.Area;
                    if (useAllFields || fieldSet.Contains("Volume"))
                        roomData["volume"] = room.Volume;
                    if (useAllFields || fieldSet.Contains("Perimeter"))
                        roomData["perimeter"] = room.Perimeter;
                    if (useAllFields || fieldSet.Contains("UnboundedHeight"))
                        roomData["unboundedHeight"] = room.UnboundedHeight;
                    if (useAllFields || fieldSet.Contains("Department"))
                        roomData["department"] = room.get_Parameter(BuiltInParameter.ROOM_DEPARTMENT)?.AsString() ?? "";
                    if (useAllFields || fieldSet.Contains("Comments"))
                        roomData["comments"] = room.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.AsString() ?? "";
                    if (useAllFields || fieldSet.Contains("Phase"))
                        roomData["phase"] = doc.GetElement(room.get_Parameter(BuiltInParameter.ROOM_PHASE)?.AsElementId())?.Name ?? "";
                    if (useAllFields || fieldSet.Contains("Occupancy"))
                        roomData["occupancy"] = room.get_Parameter(BuiltInParameter.ROOM_OCCUPANCY)?.AsString() ?? "";

                    rooms.Add(roomData);
                }

                ResultInfo = new Dictionary<string, object>
                {
                    { "totalRooms", totalCount },
                    { "totalArea", totalArea },
                    { "rooms", rooms },
                    { "truncated", totalCount > rooms.Count },
                    { "totalCount", totalCount },
                    { "success", true },
                    { "message", $"Successfully exported {rooms.Count} of {totalCount} rooms" }
                };
            }
            catch (Exception ex)
            {
                ResultInfo = new Dictionary<string, object>
                {
                    { "success", false },
                    { "message", $"Error exporting room data: {ex.Message}" }
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
