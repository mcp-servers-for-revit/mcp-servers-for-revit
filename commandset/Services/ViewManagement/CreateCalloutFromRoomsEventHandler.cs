using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using RevitMCPSDK.API.Interfaces;

namespace RevitMCPCommandSet.Services.ViewManagement
{
    public class CreateCalloutFromRoomsEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        private readonly ManualResetEvent _resetEvent = new ManualResetEvent(false);

        public List<long> RoomIds { get; set; } = new();
        public string LevelName { get; set; } = "";
        public double Offset { get; set; } = 300; // mm
        public string ViewTemplateId { get; set; } = "";
        public int Scale { get; set; } = 50;
        public object Result { get; private set; }

        public void SetParameters(List<long> roomIds, string levelName, double offset,
            string viewTemplateId, int scale)
        {
            RoomIds = roomIds;
            LevelName = levelName;
            Offset = offset;
            ViewTemplateId = viewTemplateId;
            Scale = scale;
            _resetEvent.Reset();
        }

        public bool WaitForCompletion(int timeoutMilliseconds = 60000)
        {
            return _resetEvent.WaitOne(timeoutMilliseconds);
        }

        public void Execute(UIApplication app)
        {
            try
            {
                var doc = app.ActiveUIDocument.Document;
                double offsetFt = Offset / 304.8; // mm to feet

                // Get rooms
                List<Room> rooms;
                if (RoomIds.Count > 0)
                {
                    rooms = RoomIds
                        .Select(id =>
                        {
#if REVIT2024_OR_GREATER
                            return doc.GetElement(new ElementId(id)) as Room;
#else
                            return doc.GetElement(new ElementId((int)id)) as Room;
#endif
                        })
                        .Where(r => r != null)
                        .ToList();
                }
                else if (!string.IsNullOrEmpty(LevelName))
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
                    Result = new { success = true, created = 0, message = "No rooms found" };
                    return;
                }

                // Find the parent view (floor plan of each room's level)
                var created = new List<object>();
                using (var tx = new Transaction(doc, "Create Callout Views from Rooms"))
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

                            // Create callout
                            var callout = ViewSection.CreateCallout(doc, parentView.Id,
                                parentView.GetTypeId(), min, max);

                            // Set name
                            string viewName = $"Callout - {room.Number} {room.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString() ?? ""}".Trim();
                            try { callout.Name = viewName; } catch { /* name conflict */ }

                            // Set scale
                            callout.Scale = Scale;

                            // Apply template if specified
                            if (!string.IsNullOrEmpty(ViewTemplateId) && long.TryParse(ViewTemplateId, out long vtId))
                            {
#if REVIT2024_OR_GREATER
                                callout.ViewTemplateId = new ElementId(vtId);
#else
                                callout.ViewTemplateId = new ElementId((int)vtId);
#endif
                            }

                            created.Add(new
                            {
#if REVIT2024_OR_GREATER
                                viewId = callout.Id.Value,
                                roomId = room.Id.Value,
#else
                                viewId = callout.Id.IntegerValue,
                                roomId = room.Id.IntegerValue,
#endif
                                name = callout.Name,
                                roomNumber = room.Number,
                                level = level.Name
                            });
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Trace.WriteLine($"[RevitMCP] Callout for room {room.Number} failed: {ex.Message}");
                        }
                    }

                    tx.Commit();
                }

                Result = new { success = true, created = created.Count, views = created };
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

        public string GetName() => "Create Callout From Rooms";
    }
}
