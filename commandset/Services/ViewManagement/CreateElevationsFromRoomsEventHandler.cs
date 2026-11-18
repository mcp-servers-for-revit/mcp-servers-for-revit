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
    public class CreateElevationsFromRoomsEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        public List<long> RoomIds { get; set; } = new List<long>();
        public string ViewType { get; set; } = "elevation";
        public List<string> Directions { get; set; } = new List<string> { "north", "south", "east", "west" };
        public int Scale { get; set; } = 50;
        public double OffsetMm { get; set; } = 300;
        public long ViewTemplateId { get; set; } = -1;
        public string NamingPattern { get; set; } = "{RoomName} - {Direction}";

        public AIResult<object> Result { get; private set; }
        public bool TaskCompleted { get; private set; }
        private readonly ManualResetEvent _resetEvent = new ManualResetEvent(false);

        public void SetParameters() { TaskCompleted = false; _resetEvent.Reset(); }
        public bool WaitForCompletion(int timeoutMilliseconds = 60000) { return _resetEvent.WaitOne(timeoutMilliseconds); }

        // Map direction string to ElevationMarker index
        // ElevationMarker indices: 0=South(front), 1=West(right), 2=North(back), 3=East(left)
        private static readonly Dictionary<string, int> DirectionIndexMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            { "south", 0 },
            { "west", 1 },
            { "north", 2 },
            { "east", 3 }
        };

        private static readonly Dictionary<string, string> DirectionDisplayNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "south", "South" },
            { "west", "West" },
            { "north", "North" },
            { "east", "East" }
        };

        public void Execute(UIApplication app)
        {
            try
            {
                var doc = app.ActiveUIDocument.Document;

                // Get rooms
                var rooms = new List<Room>();
                if (RoomIds.Count == 0)
                {
                    rooms = new FilteredElementCollector(doc)
                        .OfCategory(BuiltInCategory.OST_Rooms)
                        .WhereElementIsNotElementType()
                        .Cast<Room>()
                        .Where(r => r.Area > 0)
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
                if (ViewTemplateId > 0)
                {
#if REVIT2024_OR_GREATER
                    var templateElement = doc.GetElement(new ElementId(ViewTemplateId)) as View;
#else
                    var templateElement = doc.GetElement(new ElementId((int)ViewTemplateId)) as View;
#endif
                    if (templateElement != null && templateElement.IsTemplate)
                        viewTemplate = templateElement;
                }

                // Validate directions
                var validDirections = Directions
                    .Where(d => DirectionIndexMap.ContainsKey(d))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (validDirections.Count == 0)
                    throw new InvalidOperationException("No valid directions specified. Use: north, south, east, west");

                double offsetFt = OffsetMm / 304.8;
                var createdViews = new List<object>();
                int successCount = 0;

                // Find the appropriate ViewFamilyType
                ViewFamily targetFamily = ViewType.ToLower() == "section" ? ViewFamily.Section : ViewFamily.Elevation;
                var vft = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewFamilyType))
                    .Cast<ViewFamilyType>()
                    .FirstOrDefault(v => v.ViewFamily == targetFamily);

                if (vft == null)
                    throw new InvalidOperationException($"No {targetFamily} view family type found");

                // Pre-build level-to-plan-view lookup to avoid per-room collector queries
                var planViewsByLevel = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewPlan))
                    .Cast<ViewPlan>()
                    .Where(v => !v.IsTemplate && v.ViewType == Autodesk.Revit.DB.ViewType.FloorPlan && v.GenLevel != null)
                    .GroupBy(v => v.GenLevel.Id)
                    .ToDictionary(g => g.Key, g => g.First());

                using (var tg = new TransactionGroup(doc, "Create Elevations From Rooms"))
                {
                    tg.Start();

                    foreach (var room in rooms)
                    {
                        try
                        {
                            var bb = room.get_BoundingBox(null);
                            if (bb == null) continue;

                            var level = room.Level;
                            string roomName = room.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString() ?? "";
                            string roomNumber = room.Number ?? "";
                            string levelName = level?.Name ?? "";

                            XYZ center = new XYZ(
                                (bb.Min.X + bb.Max.X) / 2,
                                (bb.Min.Y + bb.Max.Y) / 2,
                                bb.Min.Z);

                            double width = bb.Max.X - bb.Min.X;
                            double depth = bb.Max.Y - bb.Min.Y;
                            double height = bb.Max.Z - bb.Min.Z;

                            if (ViewType.ToLower() == "elevation")
                            {
                                CreateElevationViews(doc, room, bb, center, width, depth, height,
                                    offsetFt, vft, level, roomName, roomNumber, levelName,
                                    viewTemplate, validDirections, createdViews, ref successCount, planViewsByLevel);
                            }
                            else // section
                            {
                                CreateSectionViews(doc, room, bb, center, width, depth, height,
                                    offsetFt, vft, level, roomName, roomNumber, levelName,
                                    viewTemplate, validDirections, createdViews, ref successCount);
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
                                roomName = room.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString() ?? "",
                                direction = "",
                                error = ex.Message
                            });
                        }
                    }

                    tg.Assimilate();
                }

                Result = new AIResult<object>
                {
                    Success = successCount > 0,
                    Message = $"Created {successCount} {ViewType} views from {rooms.Count} rooms",
                    Response = new { successCount, totalRooms = rooms.Count, views = createdViews }
                };
            }
            catch (Exception ex)
            {
                Result = new AIResult<object> { Success = false, Message = $"Create elevations from rooms failed: {ex.Message}" };
            }
            finally
            {
                TaskCompleted = true;
                _resetEvent.Set();
            }
        }

        private void CreateElevationViews(Document doc, Room room, BoundingBoxXYZ bb, XYZ center,
            double width, double depth, double height, double offsetFt, ViewFamilyType vft,
            Level level, string roomName, string roomNumber, string levelName,
            View viewTemplate, List<string> directions, List<object> createdViews, ref int successCount,
            Dictionary<ElementId, ViewPlan> planViewsByLevel)
        {
            ViewPlan planView = null;
            if (level != null)
                planViewsByLevel.TryGetValue(level.Id, out planView);

            if (planView == null)
                throw new InvalidOperationException($"No floor plan view found for level '{levelName}'");

            // Create one marker and all elevations in a single transaction
            using (var t = new Transaction(doc, $"Create Elevations for Room {roomNumber}"))
            {
                t.Start();
                try
                {
                    // Create elevation marker at room center — one marker supports up to 4 views
                    var marker = ElevationMarker.CreateElevationMarker(doc, vft.Id, center, Scale > 0 ? Scale : 50);

                    // We must regenerate the document after creating the marker before creating elevations
                    doc.Regenerate();

                    foreach (var direction in directions)
                    {
                        try
                        {
                            int dirIndex = DirectionIndexMap[direction];
                            string dirDisplay = DirectionDisplayNames[direction];

                            var elevView = marker.CreateElevation(doc, planView.Id, dirIndex);
                            if (elevView == null) continue;

                            // Set view scale
                            if (Scale > 0) elevView.Scale = Scale;

                            // Set crop box based on room bounds + offset
                            elevView.CropBoxActive = true;
                            SetElevationCropBox(elevView, bb, offsetFt, direction, width, depth, height);

                            // Apply view template
                            if (viewTemplate != null)
                                elevView.ViewTemplateId = viewTemplate.Id;

                            // Set view name
                            string viewName = BuildViewName(roomName, roomNumber, dirDisplay, levelName);
                            try { elevView.Name = viewName; } catch { /* name collision */ }

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
                                roomName,
                                direction = dirDisplay
                            });
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
                                roomName,
                                direction = DirectionDisplayNames.ContainsKey(direction) ? DirectionDisplayNames[direction] : direction,
                                error = ex.Message
                            });
                        }
                    }

                    t.Commit();
                }
                catch
                {
                    if (t.GetStatus() == TransactionStatus.Started)
                        t.RollBack();
                    throw;
                }
            }
        }

        private void CreateSectionViews(Document doc, Room room, BoundingBoxXYZ bb, XYZ center,
            double width, double depth, double height, double offsetFt, ViewFamilyType vft,
            Level level, string roomName, string roomNumber, string levelName,
            View viewTemplate, List<string> directions, List<object> createdViews, ref int successCount)
        {
            foreach (var direction in directions)
            {
                using (var t = new Transaction(doc, $"Create Section {direction} for Room {roomNumber}"))
                {
                    t.Start();
                    try
                    {
                        string dirDisplay = DirectionDisplayNames[direction];

                        var sectionBox = new BoundingBoxXYZ();
                        Transform transform = Transform.Identity;

                        // Section views: the viewer looks in the direction specified
                        // e.g., "north" means the section looks north (viewer stands south, looking north)
                        switch (direction.ToLower())
                        {
                            case "north":
                                // Looking north: viewer at south side
                                transform.Origin = new XYZ(center.X, bb.Min.Y - offsetFt, center.Z);
                                transform.BasisX = XYZ.BasisX;
                                transform.BasisY = XYZ.BasisZ;
                                transform.BasisZ = -XYZ.BasisY;
                                sectionBox.Min = new XYZ(-(width / 2 + offsetFt), -(height / 2 + offsetFt), 0);
                                sectionBox.Max = new XYZ(width / 2 + offsetFt, height / 2 + offsetFt, depth + 2 * offsetFt);
                                break;

                            case "south":
                                // Looking south: viewer at north side
                                transform.Origin = new XYZ(center.X, bb.Max.Y + offsetFt, center.Z);
                                transform.BasisX = -XYZ.BasisX;
                                transform.BasisY = XYZ.BasisZ;
                                transform.BasisZ = XYZ.BasisY;
                                sectionBox.Min = new XYZ(-(width / 2 + offsetFt), -(height / 2 + offsetFt), 0);
                                sectionBox.Max = new XYZ(width / 2 + offsetFt, height / 2 + offsetFt, depth + 2 * offsetFt);
                                break;

                            case "east":
                                // Looking east: viewer at west side
                                transform.Origin = new XYZ(bb.Min.X - offsetFt, center.Y, center.Z);
                                transform.BasisX = -XYZ.BasisY;
                                transform.BasisY = XYZ.BasisZ;
                                transform.BasisZ = XYZ.BasisX;
                                sectionBox.Min = new XYZ(-(depth / 2 + offsetFt), -(height / 2 + offsetFt), 0);
                                sectionBox.Max = new XYZ(depth / 2 + offsetFt, height / 2 + offsetFt, width + 2 * offsetFt);
                                break;

                            case "west":
                                // Looking west: viewer at east side
                                transform.Origin = new XYZ(bb.Max.X + offsetFt, center.Y, center.Z);
                                transform.BasisX = XYZ.BasisY;
                                transform.BasisY = XYZ.BasisZ;
                                transform.BasisZ = -XYZ.BasisX;
                                sectionBox.Min = new XYZ(-(depth / 2 + offsetFt), -(height / 2 + offsetFt), 0);
                                sectionBox.Max = new XYZ(depth / 2 + offsetFt, height / 2 + offsetFt, width + 2 * offsetFt);
                                break;
                        }

                        sectionBox.Transform = transform;
                        var sectionView = ViewSection.CreateSection(doc, vft.Id, sectionBox);

                        if (sectionView == null)
                        {
                            t.RollBack();
                            continue;
                        }

                        // Set view scale
                        if (Scale > 0) sectionView.Scale = Scale;

                        // Apply view template
                        if (viewTemplate != null)
                            sectionView.ViewTemplateId = viewTemplate.Id;

                        // Set view name
                        string viewName = BuildViewName(roomName, roomNumber, dirDisplay, levelName);
                        try { sectionView.Name = viewName; } catch { /* name collision */ }

                        t.Commit();

                        successCount++;
                        createdViews.Add(new
                        {
#if REVIT2024_OR_GREATER
                            viewId = sectionView.Id.Value,
                            roomId = room.Id.Value,
#else
                            viewId = sectionView.Id.IntegerValue,
                            roomId = room.Id.IntegerValue,
#endif
                            viewName = sectionView.Name,
                            roomName,
                            direction = dirDisplay
                        });
                    }
                    catch (Exception ex)
                    {
                        if (t.GetStatus() == TransactionStatus.Started)
                            t.RollBack();

                        createdViews.Add(new
                        {
                            viewId = (long)0,
#if REVIT2024_OR_GREATER
                            roomId = room.Id.Value,
#else
                            roomId = room.Id.IntegerValue,
#endif
                            viewName = "",
                            roomName,
                            direction = DirectionDisplayNames.ContainsKey(direction) ? DirectionDisplayNames[direction] : direction,
                            error = ex.Message
                        });
                    }
                }
            }
        }

        private void SetElevationCropBox(ViewSection elevView, BoundingBoxXYZ roomBB, double offsetFt,
            string direction, double width, double depth, double height)
        {
            try
            {
                var cropBox = elevView.CropBox;
                double halfHeight = (height / 2) + offsetFt;
                double halfWidth;

                switch (direction.ToLower())
                {
                    case "north":
                    case "south":
                        halfWidth = (width / 2) + offsetFt;
                        break;
                    default: // east, west
                        halfWidth = (depth / 2) + offsetFt;
                        break;
                }

                cropBox.Min = new XYZ(-halfWidth, -halfHeight, cropBox.Min.Z);
                cropBox.Max = new XYZ(halfWidth, halfHeight, cropBox.Max.Z);
                elevView.CropBox = cropBox;
            }
            catch { /* crop box adjustment failed, use default */ }
        }

        private string BuildViewName(string roomName, string roomNumber, string direction, string levelName)
        {
            return NamingPattern
                .Replace("{RoomName}", roomName)
                .Replace("{RoomNumber}", roomNumber)
                .Replace("{Direction}", direction)
                .Replace("{Level}", levelName);
        }

        public string GetName() => "Create Elevations From Rooms";
    }
}
