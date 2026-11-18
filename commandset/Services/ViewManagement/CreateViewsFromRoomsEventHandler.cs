using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using RevitMCPCommandSet.Models.Common;
using RevitMCPSDK.API.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace RevitMCPCommandSet.Services.ViewManagement
{
    public class CreateViewsFromRoomsEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        public List<long> RoomIds { get; set; } = new List<long>();
        public bool AllRooms { get; set; } = false;
        public string ViewType { get; set; } = "callout"; // callout, section, elevation
        public double OffsetMm { get; set; } = 500;
        public int Scale { get; set; } = 50;
        public string DetailLevel { get; set; } = "Medium";
        public string ViewTemplateName { get; set; } = "";
        public string NamingPattern { get; set; } = "{RoomNumber} - {RoomName}";

        public AIResult<object> Result { get; private set; }
        public bool TaskCompleted { get; private set; }
        private readonly ManualResetEvent _resetEvent = new ManualResetEvent(false);

        public void SetParameters() { TaskCompleted = false; _resetEvent.Reset(); }
        public bool WaitForCompletion(int timeoutMilliseconds = 60000) { return _resetEvent.WaitOne(timeoutMilliseconds); }

        public void Execute(UIApplication app)
        {
            try
            {
                var doc = app.ActiveUIDocument.Document;

                // Get rooms
                var rooms = new List<Room>();
                if (AllRooms || RoomIds.Count == 0)
                {
                    rooms = new FilteredElementCollector(doc)
                        .OfCategory(BuiltInCategory.OST_Rooms)
                        .WhereElementIsNotElementType()
                        .Cast<Room>()
                        .Where(r => r.Area > 0) // only placed rooms
                        .ToList();
                }
                else
                {
                    foreach (var id in RoomIds)
                    {
#if REVIT2024_OR_GREATER
                        var room = doc.GetElement(new ElementId(id)) as Room;
#else
                        var room = doc.GetElement(new ElementId((int)id)) as Room;
#endif
                        if (room != null && room.Area > 0) rooms.Add(room);
                    }
                }

                if (rooms.Count == 0)
                    throw new InvalidOperationException("No valid rooms found");

                // Find view template if specified
                View viewTemplate = null;
                if (!string.IsNullOrEmpty(ViewTemplateName))
                {
                    viewTemplate = new FilteredElementCollector(doc)
                        .OfClass(typeof(View))
                        .Cast<View>()
                        .FirstOrDefault(v => v.IsTemplate && v.Name.Equals(ViewTemplateName, StringComparison.OrdinalIgnoreCase));
                }

                double offsetFt = OffsetMm / 304.8;
                var createdViews = new List<object>();
                int successCount = 0;

                using (var tg = new TransactionGroup(doc, "Create Views From Rooms"))
                {
                    tg.Start();

                    foreach (var room in rooms)
                    {
                        try
                        {
                            var bb = room.get_BoundingBox(null);
                            if (bb == null) continue;

                            // Build view name from pattern
                            string viewName = NamingPattern
                                .Replace("{RoomNumber}", room.Number ?? "")
                                .Replace("{RoomName}", room.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString() ?? "")
                                .Replace("{Level}", room.Level?.Name ?? "");

                            using (var t = new Transaction(doc, $"Create View for Room {room.Number}"))
                            {
                                t.Start();
                                try
                                {

                                View createdView = null;
                                string viewTypeName = ViewType.ToLower();

                                switch (viewTypeName)
                                {
                                    case "callout":
                                        createdView = CreateCalloutForRoom(doc, room, bb, offsetFt, viewName);
                                        break;
                                    case "section":
                                        createdView = CreateSectionForRoom(doc, room, bb, offsetFt, viewName);
                                        break;
                                    case "elevation":
                                        createdView = CreateElevationsForRoom(doc, room, bb, offsetFt, viewName, createdViews, ref successCount);
                                        // elevation creates 4 views, handled inside
                                        break;
                                }

                                if (createdView != null)
                                {
                                    if (Scale > 0) createdView.Scale = Scale;
                                    ApplyDetailLevel(createdView);
                                    if (viewTemplate != null)
                                        createdView.ViewTemplateId = viewTemplate.Id;

                                    try { createdView.Name = viewName; } catch { }

                                    t.Commit();

                                    successCount++;
                                    createdViews.Add(new
                                    {
#if REVIT2024_OR_GREATER
                                        viewId = createdView.Id.Value,
                                        roomId = room.Id.Value,
#else
                                        viewId = createdView.Id.IntegerValue,
                                        roomId = room.Id.IntegerValue,
#endif
                                        viewName = createdView.Name,
                                        roomNumber = room.Number,
                                        roomName = room.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString() ?? "",
                                        type = viewTypeName
                                    });
                                }
                                else if (viewTypeName != "elevation") // elevation handled inside
                                {
                                    t.RollBack();
                                }
                                }
                                catch
                                {
                                    if (t.GetStatus() == TransactionStatus.Started)
                                        t.RollBack();
                                    throw;
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            createdViews.Add(new
                            {
                                viewId = (long)0,
#if REVIT2024_OR_GREATER
                                roomId = room.Id.Value,
#else
                                roomId = room.Id.IntegerValue,
#endif
                                viewName = "",
                                roomNumber = room.Number,
                                roomName = room.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString() ?? "",
                                type = "error",
                                error = ex.Message
                            });
                        }
                    }

                    tg.Assimilate();
                }

                Result = new AIResult<object>
                {
                    Success = successCount > 0,
                    Message = $"Created {successCount} views from {rooms.Count} rooms",
                    Response = new { successCount, totalRooms = rooms.Count, views = createdViews }
                };
            }
            catch (Exception ex)
            {
                Result = new AIResult<object> { Success = false, Message = $"Create views from rooms failed: {ex.Message}" };
            }
            finally
            {
                TaskCompleted = true;
                _resetEvent.Set();
            }
        }

        private ViewPlan CreateCalloutForRoom(Document doc, Room room, BoundingBoxXYZ bb, double offsetFt, string viewName)
        {
            // Get parent view (floor plan at room level)
            var level = room.Level;
            var parentView = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewPlan))
                .Cast<ViewPlan>()
                .FirstOrDefault(v => !v.IsTemplate && v.GenLevel?.Id == level?.Id && v.ViewType == ViewType_Revit(Autodesk.Revit.DB.ViewType.FloorPlan));

            if (parentView == null)
            {
                // Create a floor plan callout-style view instead
                var vft = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewFamilyType))
                    .Cast<ViewFamilyType>()
                    .FirstOrDefault(v => v.ViewFamily == ViewFamily.FloorPlan);
                if (vft == null) throw new InvalidOperationException("No FloorPlan view family type found");

                var plan = ViewPlan.Create(doc, vft.Id, level.Id);

                // Set crop region to room bounds + offset
                plan.CropBoxActive = true;
                var cropBox = new BoundingBoxXYZ();
                cropBox.Min = new XYZ(bb.Min.X - offsetFt, bb.Min.Y - offsetFt, bb.Min.Z);
                cropBox.Max = new XYZ(bb.Max.X + offsetFt, bb.Max.Y + offsetFt, bb.Max.Z);
                plan.CropBox = cropBox;

                return plan;
            }

            // Create callout from parent view
            var min = new XYZ(bb.Min.X - offsetFt, bb.Min.Y - offsetFt, 0);
            var max = new XYZ(bb.Max.X + offsetFt, bb.Max.Y + offsetFt, 0);

            var calloutView = ViewSection.CreateCallout(doc, parentView.Id, parentView.GetTypeId(), min, max);
            return calloutView as ViewPlan;
        }

        private ViewSection CreateSectionForRoom(Document doc, Room room, BoundingBoxXYZ bb, double offsetFt, string viewName)
        {
            var vft = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewFamilyType))
                .Cast<ViewFamilyType>()
                .FirstOrDefault(v => v.ViewFamily == ViewFamily.Section);
            if (vft == null) throw new InvalidOperationException("No Section view family type found");

            // Create section looking at the room from the longer side
            double width = bb.Max.X - bb.Min.X;
            double depth = bb.Max.Y - bb.Min.Y;
            double height = bb.Max.Z - bb.Min.Z;

            XYZ center = new XYZ((bb.Min.X + bb.Max.X) / 2, (bb.Min.Y + bb.Max.Y) / 2, (bb.Min.Z + bb.Max.Z) / 2);

            // Section box
            var sectionBox = new BoundingBoxXYZ();
            Transform transform = Transform.Identity;

            if (width >= depth)
            {
                // Look from south (Y- direction, viewing north)
                transform.Origin = new XYZ(center.X, bb.Min.Y - offsetFt, center.Z);
                transform.BasisX = XYZ.BasisX;
                transform.BasisY = XYZ.BasisZ;
                transform.BasisZ = -XYZ.BasisY; // looking north (into screen)

                double halfWidth = (width / 2) + offsetFt;
                double halfHeight = (height / 2) + offsetFt;
                sectionBox.Min = new XYZ(-halfWidth, -halfHeight, 0);
                sectionBox.Max = new XYZ(halfWidth, halfHeight, depth + 2 * offsetFt);
            }
            else
            {
                // Look from west (X- direction, viewing east)
                transform.Origin = new XYZ(bb.Min.X - offsetFt, center.Y, center.Z);
                transform.BasisX = -XYZ.BasisY;
                transform.BasisY = XYZ.BasisZ;
                transform.BasisZ = XYZ.BasisX; // looking east

                double halfWidth = (depth / 2) + offsetFt;
                double halfHeight = (height / 2) + offsetFt;
                sectionBox.Min = new XYZ(-halfWidth, -halfHeight, 0);
                sectionBox.Max = new XYZ(halfWidth, halfHeight, width + 2 * offsetFt);
            }

            sectionBox.Transform = transform;
            return ViewSection.CreateSection(doc, vft.Id, sectionBox);
        }

        private View CreateElevationsForRoom(Document doc, Room room, BoundingBoxXYZ bb, double offsetFt, string baseName, List<object> createdViews, ref int successCount)
        {
            // For elevation, we create an ElevationMarker at room center and create 4 views
            var vft = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewFamilyType))
                .Cast<ViewFamilyType>()
                .FirstOrDefault(v => v.ViewFamily == ViewFamily.Elevation);
            if (vft == null) return null;

            XYZ center = new XYZ((bb.Min.X + bb.Max.X) / 2, (bb.Min.Y + bb.Max.Y) / 2, bb.Min.Z);
            var marker = ElevationMarker.CreateElevationMarker(doc, vft.Id, center, Scale > 0 ? Scale : 50);

            string[] directions = { "North", "East", "South", "West" };
            View lastView = null;

            for (int i = 0; i < 4; i++)
            {
                try
                {
                    // Find a floor plan view at room level for the elevation
                    var level = room.Level;
                    var planView = new FilteredElementCollector(doc)
                        .OfClass(typeof(ViewPlan))
                        .Cast<ViewPlan>()
                        .FirstOrDefault(v => !v.IsTemplate && v.GenLevel?.Id == level?.Id && v.ViewType == Autodesk.Revit.DB.ViewType.FloorPlan);

                    if (planView == null) continue;

                    var elevView = marker.CreateElevation(doc, planView.Id, i);
                    if (elevView == null) continue;

                    try { elevView.Name = $"{baseName} - {directions[i]}"; } catch { }
                    if (Scale > 0) elevView.Scale = Scale;
                    ApplyDetailLevel(elevView);

                    // Set crop to room bounds
                    elevView.CropBoxActive = true;

                    lastView = elevView;
                    successCount++;

                    createdViews.Add(new
                    {
#if REVIT2024_OR_GREATER
                        viewId = elevView.Id.Value,
                        roomId = room.Id.Value,
#else
                        viewId = elevView.Id.IntegerValue,
                        roomId = room.Id.IntegerValue,
#endif
                        viewName = elevView.Name,
                        roomNumber = room.Number,
                        roomName = room.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString() ?? "",
                        type = $"elevation_{directions[i].ToLower()}"
                    });
                }
                catch { }
            }

            return lastView;
        }

        private Autodesk.Revit.DB.ViewType ViewType_Revit(Autodesk.Revit.DB.ViewType vt) => vt; // helper - not actually filtering by this

        private void ApplyDetailLevel(View view)
        {
            switch (DetailLevel?.ToLower())
            {
                case "coarse": view.DetailLevel = ViewDetailLevel.Coarse; break;
                case "fine": view.DetailLevel = ViewDetailLevel.Fine; break;
                default: view.DetailLevel = ViewDetailLevel.Medium; break;
            }
        }

        public string GetName() => "Create Views From Rooms";
    }
}
