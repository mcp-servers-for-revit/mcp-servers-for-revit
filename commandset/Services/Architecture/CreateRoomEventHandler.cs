using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using RevitMCPCommandSet.Models.Architecture;
using RevitMCPCommandSet.Models.Common;
using RevitMCPCommandSet.Utils;
using RevitMCPSDK.API.Interfaces;

namespace RevitMCPCommandSet.Services.Architecture
{
    /// <summary>
    /// Failure preprocessor that handles duplicate room number warnings by allowing them to proceed.
    /// Revit auto-assigns room numbers when creating rooms, which may conflict with existing numbers.
    /// We handle uniqueness ourselves after creation, so we suppress these warnings.
    /// </summary>
    public class DuplicateRoomNumberFailurePreprocessor : IFailuresPreprocessor
    {
        public FailureProcessingResult PreprocessFailures(FailuresAccessor failuresAccessor)
        {
            IList<FailureMessageAccessor> failures = failuresAccessor.GetFailureMessages();

            foreach (FailureMessageAccessor failure in failures)
            {
                // Handle room number duplicate warnings by deleting the warning
                // This allows the transaction to proceed, and we'll set a unique number afterwards
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
    /// Event handler for creating rooms in Revit
    /// </summary>
    public class CreateRoomEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        private UIApplication _uiApp;
        private UIDocument _uiDoc => _uiApp.ActiveUIDocument;
        private Document _doc => _uiDoc.Document;

        /// <summary>
        /// Event wait object for synchronization
        /// </summary>
        private readonly ManualResetEvent _resetEvent = new ManualResetEvent(false);

        /// <summary>
        /// Room creation data (input)
        /// </summary>
        public List<RoomCreationInfo> RoomData { get; private set; }

        /// <summary>
        /// Execution result (output)
        /// </summary>
        public AIResult<List<RoomResultInfo>> Result { get; private set; }

        /// <summary>
        /// Set the room creation parameters
        /// </summary>
        public void SetParameters(List<RoomCreationInfo> data)
        {
            RoomData = data;
            _resetEvent.Reset();
        }

        public void Execute(UIApplication uiapp)
        {
            _uiApp = uiapp;

            try
            {
                var createdRooms = new List<RoomResultInfo>();

                // Get all existing room numbers to avoid duplicates
                HashSet<string> existingRoomNumbers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var existingRooms = new FilteredElementCollector(_doc)
                    .OfCategory(BuiltInCategory.OST_Rooms)
                    .WhereElementIsNotElementType()
                    .Cast<Room>()
                    .ToList();

                foreach (var existingRoom in existingRooms)
                {
                    if (!string.IsNullOrEmpty(existingRoom.Number))
                    {
                        existingRoomNumbers.Add(existingRoom.Number);
                    }
                }

                foreach (var roomInfo in RoomData)
                {
                    using (Transaction tx = new Transaction(_doc, "Create Room"))
                    {
                        // Set up failure handling to completely suppress duplicate room number warnings
                        // Revit auto-assigns numbers which may conflict; we set unique numbers afterwards
                        FailureHandlingOptions failureOptions = tx.GetFailureHandlingOptions();
                        failureOptions.SetFailuresPreprocessor(new DuplicateRoomNumberFailurePreprocessor());
                        failureOptions.SetClearAfterRollback(true);
                        failureOptions.SetDelayedMiniWarnings(false);
                        tx.SetFailureHandlingOptions(failureOptions);

                        tx.Start();
                        try
                        {

                        // Step 1: Find or determine the level
                        Level level = null;
                        if (roomInfo.LevelId > 0)
                        {
                            // Use specified level ID
                            level = _doc.GetElement(new ElementId(roomInfo.LevelId)) as Level;
                        }

                        if (level == null && roomInfo.Location != null)
                        {
                            // Find nearest level to the Z coordinate
                            double zInFeet = roomInfo.Location.Z / 304.8;
                            level = FindNearestLevel(zInFeet);
                        }

                        if (level == null)
                        {
                            // Use the first available level
                            level = new FilteredElementCollector(_doc)
                                .OfClass(typeof(Level))
                                .Cast<Level>()
                                .OrderBy(l => l.Elevation)
                                .FirstOrDefault();
                        }

                        if (level == null)
                        {
                            // Skip if no level found
                            continue;
                        }

                        // Step 2: Create the room at the specified location
                        Room room = null;

                        if (roomInfo.Location != null)
                        {
                            // Convert mm to feet for UV coordinates (2D point in plan)
                            double xInFeet = roomInfo.Location.X / 304.8;
                            double yInFeet = roomInfo.Location.Y / 304.8;
                            UV locationUV = new UV(xInFeet, yInFeet);

                            // Create room at the specified UV location on the level
                            room = _doc.Create.NewRoom(level, locationUV);
                        }

                        if (room == null)
                        {
                            // If location-based creation failed, create an unplaced room
                            // This can happen if the point is not inside an enclosed area
                            tx.RollBack();
                            continue;
                        }

                        // Step 3: Set room properties
                        // Set room name
                        if (!string.IsNullOrEmpty(roomInfo.Name))
                        {
                            Parameter nameParam = room.get_Parameter(BuiltInParameter.ROOM_NAME);
                            if (nameParam != null && !nameParam.IsReadOnly)
                            {
                                nameParam.Set(roomInfo.Name);
                            }
                        }

                        // Set room number (ensuring uniqueness)
                        // IMPORTANT: Generate unique number BEFORE relying on Revit's auto-assigned number
                        // to prevent any duplicate number warnings
                        string roomNumber = roomInfo.Number;
                        if (!string.IsNullOrEmpty(roomNumber))
                        {
                            // User provided a number - make it unique if it already exists
                            roomNumber = GetUniqueRoomNumber(roomNumber, existingRoomNumbers);
                        }
                        else
                        {
                            // No number provided - generate next available number (don't use room.Number)
                            roomNumber = GetNextAvailableRoomNumber(existingRoomNumbers);
                        }

                        Parameter numberParam = room.get_Parameter(BuiltInParameter.ROOM_NUMBER);
                        if (numberParam != null && !numberParam.IsReadOnly)
                        {
                            numberParam.Set(roomNumber);
                            // Add to tracking set to avoid duplicates in same batch
                            existingRoomNumbers.Add(roomNumber);
                        }

                        // Set upper limit if specified
                        if (roomInfo.UpperLimitId > 0)
                        {
                            Parameter upperLimitParam = room.get_Parameter(BuiltInParameter.ROOM_UPPER_LEVEL);
                            if (upperLimitParam != null && !upperLimitParam.IsReadOnly)
                            {
                                upperLimitParam.Set(new ElementId(roomInfo.UpperLimitId));
                            }
                        }

                        // Set limit offset if specified (convert mm to feet)
                        if (roomInfo.LimitOffset > 0)
                        {
                            Parameter limitOffsetParam = room.get_Parameter(BuiltInParameter.ROOM_UPPER_OFFSET);
                            if (limitOffsetParam != null && !limitOffsetParam.IsReadOnly)
                            {
                                limitOffsetParam.Set(roomInfo.LimitOffset / 304.8);
                            }
                        }

                        // Set base offset if specified (convert mm to feet)
                        if (roomInfo.BaseOffset != 0)
                        {
                            Parameter baseOffsetParam = room.get_Parameter(BuiltInParameter.ROOM_LOWER_OFFSET);
                            if (baseOffsetParam != null && !baseOffsetParam.IsReadOnly)
                            {
                                baseOffsetParam.Set(roomInfo.BaseOffset / 304.8);
                            }
                        }

                        // Set department if provided
                        if (!string.IsNullOrEmpty(roomInfo.Department))
                        {
                            Parameter deptParam = room.get_Parameter(BuiltInParameter.ROOM_DEPARTMENT);
                            if (deptParam != null && !deptParam.IsReadOnly)
                            {
                                deptParam.Set(roomInfo.Department);
                            }
                        }

                        // Set comments if provided
                        if (!string.IsNullOrEmpty(roomInfo.Comments))
                        {
                            Parameter commentsParam = room.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
                            if (commentsParam != null && !commentsParam.IsReadOnly)
                            {
                                commentsParam.Set(roomInfo.Comments);
                            }
                        }

                        tx.Commit();

                        // Add to result list
                        createdRooms.Add(new RoomResultInfo
                        {
                            Id = room.Id.GetIntValue(),
                            UniqueId = room.UniqueId,
                            Name = roomInfo.Name ?? "Room",
                            Number = roomNumber, // Use the actual assigned number (may differ from requested if made unique)
                            RequestedNumber = roomInfo.Number, // Original requested number
                            LevelName = level.Name,
                            Area = room.Area,
                            Perimeter = room.Perimeter
                        });
                        }
                        catch
                        {
                            if (tx.GetStatus() == TransactionStatus.Started)
                                tx.RollBack();
                            throw;
                        }
                    }
                }

                Result = new AIResult<List<RoomResultInfo>>
                {
                    Success = true,
                    Message = $"Successfully created {createdRooms.Count} room(s)",
                    Response = createdRooms
                };
            }
            catch (Exception ex)
            {
                Result = new AIResult<List<RoomResultInfo>>
                {
                    Success = false,
                    Message = $"Error creating rooms: {ex.Message}",
                };
            }
            finally
            {
                _resetEvent.Set(); // Signal that the operation is complete
            }
        }

        /// <summary>
        /// Find the nearest level to a given elevation
        /// </summary>
        private Level FindNearestLevel(double elevationInFeet)
        {
            var levels = new FilteredElementCollector(_doc)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .ToList();

            Level nearestLevel = null;
            double minDistance = double.MaxValue;

            foreach (var level in levels)
            {
                double distance = Math.Abs(level.Elevation - elevationInFeet);
                if (distance < minDistance)
                {
                    minDistance = distance;
                    nearestLevel = level;
                }
            }

            return nearestLevel;
        }

        /// <summary>
        /// Get the next available room number by finding the highest existing number and incrementing
        /// </summary>
        /// <param name="existingNumbers">Set of existing room numbers</param>
        /// <returns>A guaranteed unique room number</returns>
        private string GetNextAvailableRoomNumber(HashSet<string> existingNumbers)
        {
            // Find the highest numeric room number and increment from there
            int maxNumber = 0;
            foreach (string num in existingNumbers)
            {
                // Try to parse the entire string as a number
                if (int.TryParse(num, out int parsed))
                {
                    if (parsed > maxNumber) maxNumber = parsed;
                }
                else
                {
                    // Try to extract trailing digits (e.g., "Room 101" -> 101)
                    string digits = "";
                    for (int i = num.Length - 1; i >= 0; i--)
                    {
                        if (char.IsDigit(num[i]))
                            digits = num[i] + digits;
                        else if (digits.Length > 0)
                            break;
                    }
                    if (digits.Length > 0 && int.TryParse(digits, out int trailingNum))
                    {
                        if (trailingNum > maxNumber) maxNumber = trailingNum;
                    }
                }
            }

            // Start from maxNumber + 1 and find next available
            for (int i = maxNumber + 1; i < maxNumber + 10000; i++)
            {
                string candidate = i.ToString();
                if (!existingNumbers.Contains(candidate))
                {
                    return candidate;
                }
            }

            // Fallback (should never reach here)
            return (maxNumber + 1).ToString();
        }

        /// <summary>
        /// Get a unique room number by adding a suffix if the number already exists
        /// </summary>
        /// <param name="baseNumber">The desired room number</param>
        /// <param name="existingNumbers">Set of existing room numbers</param>
        /// <returns>A unique room number</returns>
        private string GetUniqueRoomNumber(string baseNumber, HashSet<string> existingNumbers)
        {
            if (string.IsNullOrEmpty(baseNumber))
            {
                baseNumber = "1";
            }

            // If the number doesn't exist, use it as-is
            if (!existingNumbers.Contains(baseNumber))
            {
                return baseNumber;
            }

            // Try to extract numeric portion and increment
            // Handle cases like "101", "101A", "Room 101", etc.
            string prefix = "";
            string numericPart = "";
            string suffix = "";

            // Find the last sequence of digits in the string
            int lastDigitEnd = -1;
            int lastDigitStart = -1;
            for (int i = baseNumber.Length - 1; i >= 0; i--)
            {
                if (char.IsDigit(baseNumber[i]))
                {
                    if (lastDigitEnd == -1) lastDigitEnd = i;
                    lastDigitStart = i;
                }
                else if (lastDigitEnd != -1)
                {
                    break;
                }
            }

            if (lastDigitStart != -1)
            {
                prefix = baseNumber.Substring(0, lastDigitStart);
                numericPart = baseNumber.Substring(lastDigitStart, lastDigitEnd - lastDigitStart + 1);
                suffix = baseNumber.Substring(lastDigitEnd + 1);

                // Try incrementing the numeric part
                if (int.TryParse(numericPart, out int num))
                {
                    int maxAttempts = 1000;
                    for (int i = 1; i <= maxAttempts; i++)
                    {
                        string candidate = prefix + (num + i).ToString().PadLeft(numericPart.Length, '0') + suffix;
                        if (!existingNumbers.Contains(candidate))
                        {
                            return candidate;
                        }
                    }
                }
            }

            // Fallback: append a letter suffix (A, B, C, ...)
            for (char c = 'A'; c <= 'Z'; c++)
            {
                string candidate = baseNumber + c;
                if (!existingNumbers.Contains(candidate))
                {
                    return candidate;
                }
            }

            // Last resort: append a number
            for (int i = 2; i <= 1000; i++)
            {
                string candidate = baseNumber + "-" + i;
                if (!existingNumbers.Contains(candidate))
                {
                    return candidate;
                }
            }

            // Should never reach here, but just in case
            return baseNumber + "-" + Guid.NewGuid().ToString().Substring(0, 4);
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
            return "Create Room";
        }
    }

    /// <summary>
    /// Result information for a created room
    /// </summary>
    public class RoomResultInfo
    {
        public int Id { get; set; }
        public string UniqueId { get; set; }
        public string Name { get; set; }
        public string Number { get; set; }
        public string RequestedNumber { get; set; } // Original requested number (may differ from Number if made unique)
        public string LevelName { get; set; }
        public double Area { get; set; }
        public double Perimeter { get; set; }
    }
}
