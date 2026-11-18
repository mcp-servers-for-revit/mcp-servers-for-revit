using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using RevitMCPCommandSet.Utils;
using RevitMCPSDK.API.Interfaces;

namespace RevitMCPCommandSet.Services
{
    /// <summary>
    /// Failure preprocessor that handles duplicate room number warnings during tagging.
    /// This suppresses warnings that might occur if the model has rooms with conflicting numbers.
    /// </summary>
    public class TagRoomFailurePreprocessor : IFailuresPreprocessor
    {
        public FailureProcessingResult PreprocessFailures(FailuresAccessor failuresAccessor)
        {
            IList<FailureMessageAccessor> failures = failuresAccessor.GetFailureMessages();

            foreach (FailureMessageAccessor failure in failures)
            {
                // Handle room-related warnings by deleting them
                string description = failure.GetDescriptionText();
                if (description.Contains("Number") ||
                    description.Contains("number") ||
                    description.Contains("duplicate") ||
                    description.Contains("Duplicate"))
                {
                    failuresAccessor.DeleteWarning(failure);
                }
            }

            return FailureProcessingResult.Continue;
        }
    }

    /// <summary>
    /// Event handler for creating room tags in Revit
    /// </summary>
    public class TagRoomsEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        private UIApplication _uiApp;
        private UIDocument _uiDoc => _uiApp.ActiveUIDocument;
        private Document _doc => _uiDoc.Document;

        /// <summary>
        /// Event wait object for synchronization
        /// </summary>
        private readonly ManualResetEvent _resetEvent = new ManualResetEvent(false);

        /// <summary>
        /// Tagging result data
        /// </summary>
        public object TaggingResults { get; private set; }

        private bool _useLeader;
        private string _tagTypeId;
        private List<int> _roomIds;

        /// <summary>
        /// Set the tagging parameters
        /// </summary>
        public void SetParameters(bool useLeader, string tagTypeId, List<int> roomIds = null)
        {
            _useLeader = useLeader;
            _tagTypeId = tagTypeId;
            _roomIds = roomIds;
            _resetEvent.Reset();
        }

        public void Execute(UIApplication uiapp)
        {
            _uiApp = uiapp;
            string viewSwitchMessage = null;

            try
            {
                View activeView = _doc.ActiveView;

                // First, determine the target level from the rooms we need to tag
                Level targetLevel = null;
                if (_roomIds != null && _roomIds.Count > 0)
                {
                    // Get level from specified rooms
                    var firstRoom = _doc.GetElement(new ElementId(_roomIds[0])) as Room;
                    if (firstRoom != null)
                    {
                        targetLevel = _doc.GetElement(firstRoom.LevelId) as Level;
                    }
                }
                else
                {
                    // Get level from any room in the project
                    var anyRoom = new FilteredElementCollector(_doc)
                        .OfCategory(BuiltInCategory.OST_Rooms)
                        .WhereElementIsNotElementType()
                        .Cast<Room>()
                        .FirstOrDefault(r => r.Area > 0);

                    if (anyRoom != null)
                    {
                        targetLevel = _doc.GetElement(anyRoom.LevelId) as Level;
                    }
                }

                // Check if current view supports room tags AND is on the correct level
                bool isCorrectViewType = activeView.ViewType == ViewType.FloorPlan ||
                                         activeView.ViewType == ViewType.CeilingPlan;

                bool isCorrectLevel = false;
                if (isCorrectViewType && activeView is ViewPlan viewPlan && targetLevel != null)
                {
                    isCorrectLevel = viewPlan.GenLevel != null && viewPlan.GenLevel.Id == targetLevel.Id;
                }

                bool needsViewSwitch = !isCorrectViewType || !isCorrectLevel;

                if (needsViewSwitch)
                {
                    if (targetLevel != null)
                    {
                        // Find a floor plan view for this level
                        var floorPlanView = new FilteredElementCollector(_doc)
                            .OfClass(typeof(ViewPlan))
                            .Cast<ViewPlan>()
                            .FirstOrDefault(v => v.ViewType == ViewType.FloorPlan &&
                                                 !v.IsTemplate &&
                                                 v.GenLevel != null &&
                                                 v.GenLevel.Id == targetLevel.Id);

                        if (floorPlanView != null)
                        {
                            string previousViewName = activeView.Name;
                            _uiDoc.ActiveView = floorPlanView;
                            activeView = floorPlanView;
                            viewSwitchMessage = $"Switched from '{previousViewName}' to '{floorPlanView.Name}' for room tagging";
                        }
                        else
                        {
                            // No suitable floor plan found
                            TaggingResults = new
                            {
                                success = false,
                                message = $"Cannot tag rooms: Current view '{activeView.Name}' ({activeView.ViewType}) does not support room tags, and no floor plan view was found for level '{targetLevel.Name}'."
                            };
                            return;
                        }
                    }
                    else
                    {
                        TaggingResults = new
                        {
                            success = false,
                            message = $"Cannot tag rooms: Current view '{activeView.Name}' ({activeView.ViewType}) does not support room tags, and no rooms were found to determine the appropriate level."
                        };
                        return;
                    }
                }

                // Get rooms to tag
                ICollection<Element> rooms;

                if (_roomIds != null && _roomIds.Count > 0)
                {
                    // Get specific rooms by ID
                    rooms = _roomIds
                        .Select(id => _doc.GetElement(new ElementId(id)))
                        .Where(e => e != null && e is Room)
                        .ToList();
                }
                else
                {
                    // Get all rooms in the current view
                    FilteredElementCollector roomCollector = new FilteredElementCollector(_doc, activeView.Id);
                    rooms = roomCollector.OfCategory(BuiltInCategory.OST_Rooms)
                                         .WhereElementIsNotElementType()
                                         .ToElements();
                }

                // Create room tags
                List<object> createdTags = new List<object>();
                List<string> errors = new List<string>();
                List<object> skippedRooms = new List<object>();

                // Get existing room tags in the view to avoid duplicates
                HashSet<long> roomsWithExistingTags = new HashSet<long>();
                FilteredElementCollector existingTagCollector = new FilteredElementCollector(_doc, activeView.Id);
                var existingRoomTags = existingTagCollector.OfCategory(BuiltInCategory.OST_RoomTags)
                                                          .WhereElementIsNotElementType()
                                                          .Cast<RoomTag>()
                                                          .ToList();

                foreach (RoomTag existingTag in existingRoomTags)
                {
                    if (existingTag.Room != null)
                    {
                        roomsWithExistingTags.Add(existingTag.Room.Id.GetValue());
                    }
                }

                using (Transaction tran = new Transaction(_doc, "Tag Rooms"))
                {
                    // Set up failure handling to completely suppress any room-related warnings
                    FailureHandlingOptions failureOptions = tran.GetFailureHandlingOptions();
                    failureOptions.SetFailuresPreprocessor(new TagRoomFailurePreprocessor());
                    failureOptions.SetClearAfterRollback(true);
                    failureOptions.SetDelayedMiniWarnings(false);
                    tran.SetFailureHandlingOptions(failureOptions);

                    tran.Start();
                    try
                    {
                    // Find the room tag type
                    FamilySymbol roomTagType = FindRoomTagType(_doc);

                    if (roomTagType == null)
                    {
                        TaggingResults = new
                        {
                            success = false,
                            message = "No room tag family type found in the project"
                        };
                        tran.RollBack();
                        return;
                    }

                    // Ensure tag type is active
                    if (!roomTagType.IsActive)
                    {
                        roomTagType.Activate();
                        _doc.Regenerate();
                    }

                    // Create tags for each room
                    foreach (Element element in rooms)
                    {
                        Room room = element as Room;
                        if (room == null) continue;

                        // Skip unplaced or not enclosed rooms
                        if (room.Area <= 0)
                        {
                            skippedRooms.Add(new
                            {
                                roomId = room.Id.GetValue().ToString(),
                                roomName = room.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString() ?? "Room",
                                roomNumber = room.Number,
                                reason = "Room has Area=0 (not enclosed by walls or not placed)"
                            });
                            continue;
                        }

                        // Skip rooms that already have tags
                        if (roomsWithExistingTags.Contains(room.Id.GetValue()))
                        {
                            skippedRooms.Add(new
                            {
                                roomId = room.Id.GetValue().ToString(),
                                roomName = room.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString() ?? "Room",
                                roomNumber = room.Number,
                                reason = "Room already has a tag in this view"
                            });
                            continue;
                        }

                        try
                        {
                            // Get the room's location point
                            LocationPoint locPoint = room.Location as LocationPoint;
                            XYZ roomCenter;

                            if (locPoint != null)
                            {
                                roomCenter = locPoint.Point;
                            }
                            else
                            {
                                // Fallback: get center from bounding box
                                BoundingBoxXYZ bbox = room.get_BoundingBox(activeView);
                                if (bbox == null) continue;
                                roomCenter = (bbox.Min + bbox.Max) / 2;
                            }

                            // Create UV point for room tag
                            UV tagPoint = new UV(roomCenter.X, roomCenter.Y);

                            // Create the room tag
                            RoomTag tag = _doc.Create.NewRoomTag(
                                new LinkElementId(room.Id),
                                tagPoint,
                                activeView.Id);

                            if (tag != null)
                            {
                                // Set leader if requested
                                if (_useLeader)
                                {
                                    tag.HasLeader = true;
                                }

                                createdTags.Add(new
                                {
                                    tagId = tag.Id.GetValue().ToString(),
                                    roomId = room.Id.GetValue().ToString(),
                                    roomName = room.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString() ?? "Room",
                                    roomNumber = room.Number,
                                    location = new
                                    {
                                        x = roomCenter.X * 304.8, // Convert to mm
                                        y = roomCenter.Y * 304.8,
                                        z = roomCenter.Z * 304.8
                                    }
                                });
                            }
                        }
                        catch (Exception ex)
                        {
                            errors.Add($"Error tagging room {room.Id.GetValue()}: {ex.Message}");
                        }
                    }

                    tran.Commit();

                    string resultMessage = skippedRooms.Count > 0
                        ? $"Created {createdTags.Count} tags. Skipped {skippedRooms.Count} rooms that already had tags."
                        : $"Successfully created {createdTags.Count} room tags.";

                    if (viewSwitchMessage != null)
                    {
                        resultMessage = viewSwitchMessage + "\n" + resultMessage;
                    }

                    TaggingResults = new
                    {
                        success = true,
                        totalRooms = rooms.Count,
                        taggedRooms = createdTags.Count,
                        skippedCount = skippedRooms.Count,
                        tags = createdTags,
                        skippedRooms = skippedRooms.Count > 0 ? skippedRooms : null,
                        errors = errors.Count > 0 ? errors : null,
                        viewSwitched = viewSwitchMessage != null,
                        message = resultMessage
                    };
                    }
                    catch
                    {
                        if (tran.GetStatus() == TransactionStatus.Started)
                            tran.RollBack();
                        throw;
                    }
                }
            }
            catch (Exception ex)
            {
                TaggingResults = new
                {
                    success = false,
                    message = $"Error occurred: {ex.Message}"
                };
            }
            finally
            {
                _resetEvent.Set(); // Signal that the operation is complete
            }
        }

        /// <summary>
        /// Wait for the operation to complete
        /// </summary>
        /// <param name="timeoutMilliseconds">Timeout in milliseconds</param>
        /// <returns>True if completed before timeout</returns>
        public bool WaitForCompletion(int timeoutMilliseconds = 10000)
        {
            return _resetEvent.WaitOne(timeoutMilliseconds);
        }

        /// <summary>
        /// IExternalEventHandler.GetName implementation
        /// </summary>
        public string GetName()
        {
            return "Tag Rooms";
        }

        /// <summary>
        /// Find the room tag type in the document
        /// </summary>
        private FamilySymbol FindRoomTagType(Document doc)
        {
            // If specific tag type ID was specified, try to use it
            if (!string.IsNullOrEmpty(_tagTypeId) && long.TryParse(_tagTypeId, out long id))
            {
                ElementId elementId = RevitMCPCommandSet.Utils.ElementIdExtensions.FromLong(id);
                Element element = doc.GetElement(elementId);

                if (element != null && element is FamilySymbol symbol &&
                    symbol.Category != null &&
                    symbol.Category.Id.GetIntValue() == (int)BuiltInCategory.OST_RoomTags)
                {
                    return symbol;
                }
            }

            // Find the first available room tag type
            FilteredElementCollector tagCollector = new FilteredElementCollector(doc);
            FamilySymbol roomTagType = tagCollector.OfClass(typeof(FamilySymbol))
                                                  .WhereElementIsElementType()
                                                  .Where(e => e.Category != null &&
                                                         e.Category.Id.GetIntValue() == (int)BuiltInCategory.OST_RoomTags)
                                                  .Cast<FamilySymbol>()
                                                  .FirstOrDefault();

            return roomTagType;
        }
    }
}
