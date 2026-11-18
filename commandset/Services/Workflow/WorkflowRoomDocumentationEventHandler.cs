using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using RevitMCPSDK.API.Interfaces;

namespace RevitMCPCommandSet.Services.Workflow
{
    public class WorkflowRoomDocumentationEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        private readonly ManualResetEvent _resetEvent = new ManualResetEvent(false);

        public string LevelName { get; set; } = "";
        public bool CreateSections { get; set; } = true;
        public double Offset { get; set; } = 300; // mm
        public object Result { get; private set; }

        public void SetParameters(string levelName, bool createSections, double offset)
        {
            LevelName = levelName;
            CreateSections = createSections;
            Offset = offset;
            _resetEvent.Reset();
        }

        public bool WaitForCompletion(int timeoutMilliseconds = 120000)
        {
            return _resetEvent.WaitOne(timeoutMilliseconds);
        }

        public void Execute(UIApplication app)
        {
            try
            {
                var doc = app.ActiveUIDocument.Document;
                var activeView = doc.ActiveView;
                double offsetFt = Offset / 304.8; // mm to feet

                // 1. Collect rooms (filtered by level if provided)
                List<Room> rooms;
                if (!string.IsNullOrEmpty(LevelName))
                {
                    rooms = new FilteredElementCollector(doc)
                        .OfCategory(BuiltInCategory.OST_Rooms)
                        .WhereElementIsNotElementType()
                        .Cast<Room>()
                        .Where(r => r.Level?.Name?.Equals(LevelName, StringComparison.OrdinalIgnoreCase) == true)
                        .Where(r => r.Area > 0)
                        .ToList();
                }
                else
                {
                    rooms = new FilteredElementCollector(doc)
                        .OfCategory(BuiltInCategory.OST_Rooms)
                        .WhereElementIsNotElementType()
                        .Cast<Room>()
                        .Where(r => r.Area > 0)
                        .ToList();
                }

                if (rooms.Count == 0)
                {
                    Result = new { success = true, roomCount = 0, viewsCreated = 0, tagsPlaced = 0, rooms = new List<object>(), message = "No rooms found" };
                    return;
                }

                // Collect room data
                var roomDataList = new List<object>();
                foreach (var room in rooms)
                {
                    roomDataList.Add(new
                    {
#if REVIT2024_OR_GREATER
                        id = room.Id.Value,
#else
                        id = room.Id.IntegerValue,
#endif
                        number = room.Number,
                        name = room.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString() ?? "",
                        area = Math.Round(room.Area * 0.092903, 2), // sq ft to sq m
                        volume = Math.Round(room.Volume * 0.0283168, 2), // cu ft to cu m
                        level = room.Level?.Name ?? "",
                        department = room.get_Parameter(BuiltInParameter.ROOM_DEPARTMENT)?.AsString() ?? ""
                    });
                }

                int viewsCreated = 0;
                int tagsPlaced = 0;

                // 2. Create section views from room bounding boxes if requested
                if (CreateSections)
                {
                    using (var tx = new Transaction(doc, "Workflow Room Documentation - Sections"))
                    {
                        tx.Start();

                        foreach (var room in rooms)
                        {
                            try
                            {
                                var level = room.Level;
                                if (level == null) continue;

                                // Find existing floor plan for this level
                                var parentView = new FilteredElementCollector(doc)
                                    .OfClass(typeof(ViewPlan))
                                    .Cast<ViewPlan>()
                                    .FirstOrDefault(v => !v.IsTemplate &&
                                        v.GenLevel?.Id == level.Id &&
                                        v.ViewType == ViewType.FloorPlan);

                                if (parentView == null) continue;

                                // Get room bounding box
                                var bb = room.get_BoundingBox(null);
                                if (bb == null) continue;

                                var min = new XYZ(bb.Min.X - offsetFt, bb.Min.Y - offsetFt, bb.Min.Z);
                                var max = new XYZ(bb.Max.X + offsetFt, bb.Max.Y + offsetFt, bb.Max.Z);

                                // Create callout view
                                var callout = ViewSection.CreateCallout(doc, parentView.Id,
                                    parentView.GetTypeId(), min, max);

                                // Set name
                                string viewName = $"Room Doc - {room.Number} {room.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString() ?? ""}".Trim();
                                try { callout.Name = viewName; } catch { /* name conflict */ }

                                viewsCreated++;
                            }
                            catch (Exception ex)
                            {
                                System.Diagnostics.Trace.WriteLine($"[RevitMCP] Section for room {room.Number} failed: {ex.Message}");
                            }
                        }

                        tx.Commit();
                    }
                }

                // 3. Tag rooms in the active view
                using (var tx = new Transaction(doc, "Workflow Room Documentation - Tags"))
                {
                    tx.Start();

                    // Find room tag type
                    var roomTagType = new FilteredElementCollector(doc)
                        .OfClass(typeof(FamilySymbol))
                        .WhereElementIsElementType()
                        .Where(e => e.Category != null &&
#if REVIT2024_OR_GREATER
                               e.Category.Id.Value == (long)BuiltInCategory.OST_RoomTags)
#else
                               e.Category.Id.IntegerValue == (int)BuiltInCategory.OST_RoomTags)
#endif
                        .Cast<FamilySymbol>()
                        .FirstOrDefault();

                    if (roomTagType != null)
                    {
                        if (!roomTagType.IsActive)
                        {
                            roomTagType.Activate();
                            doc.Regenerate();
                        }

                        // Get existing room tags to avoid duplicates
                        var existingTaggedRoomIds = new HashSet<long>();
                        if (activeView.ViewType == ViewType.FloorPlan || activeView.ViewType == ViewType.CeilingPlan)
                        {
                            var existingTags = new FilteredElementCollector(doc, activeView.Id)
                                .OfCategory(BuiltInCategory.OST_RoomTags)
                                .WhereElementIsNotElementType()
                                .Cast<RoomTag>()
                                .ToList();
                            foreach (var tag in existingTags)
                            {
                                if (tag.Room != null)
                                {
#if REVIT2024_OR_GREATER
                                    existingTaggedRoomIds.Add(tag.Room.Id.Value);
#else
                                    existingTaggedRoomIds.Add(tag.Room.Id.IntegerValue);
#endif
                                }
                            }
                        }

                        // Tag rooms visible in the active view
                        var viewRooms = activeView.ViewType == ViewType.FloorPlan || activeView.ViewType == ViewType.CeilingPlan
                            ? new FilteredElementCollector(doc, activeView.Id)
                                .OfCategory(BuiltInCategory.OST_Rooms)
                                .WhereElementIsNotElementType()
                                .Cast<Room>()
                                .Where(r => r.Area > 0)
                                .ToList()
                            : new List<Room>();

                        foreach (var room in viewRooms)
                        {
#if REVIT2024_OR_GREATER
                            if (existingTaggedRoomIds.Contains(room.Id.Value)) continue;
#else
                            if (existingTaggedRoomIds.Contains(room.Id.IntegerValue)) continue;
#endif

                            try
                            {
                                var locPoint = room.Location as LocationPoint;
                                XYZ roomCenter;

                                if (locPoint != null)
                                {
                                    roomCenter = locPoint.Point;
                                }
                                else
                                {
                                    var bbox = room.get_BoundingBox(activeView);
                                    if (bbox == null) continue;
                                    roomCenter = (bbox.Min + bbox.Max) / 2;
                                }

                                var tagPoint = new UV(roomCenter.X, roomCenter.Y);
                                var tag = doc.Create.NewRoomTag(
                                    new LinkElementId(room.Id),
                                    tagPoint,
                                    activeView.Id);

                                if (tag != null) tagsPlaced++;
                            }
                            catch (Exception ex)
                            {
                                System.Diagnostics.Trace.WriteLine($"[RevitMCP] Tag for room {room.Number} failed: {ex.Message}");
                            }
                        }
                    }

                    tx.Commit();
                }

                Result = new
                {
                    success = true,
                    roomCount = rooms.Count,
                    viewsCreated,
                    tagsPlaced,
                    rooms = roomDataList
                };
            }
            catch (Exception ex)
            {
                Result = new { success = false, error = ex.Message };
            }
            finally
            {
                _resetEvent.Set();
            }
        }

        public string GetName() => "Workflow Room Documentation";
    }
}
