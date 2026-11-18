using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using RevitMCPCommandSet.Models.Common;
using RevitMCPCommandSet.Utils;
using RevitMCPSDK.API.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace RevitMCPCommandSet.Services.DataExtraction
{
    public class GetRoomOpeningsEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        public List<long> RoomIds { get; set; } = new List<long>();
        public List<string> RoomNumbers { get; set; } = new List<string>();
        public string LevelName { get; set; } = "";
        public string ElementType { get; set; } = "both";
        public bool IncludeRoomParams { get; set; } = false;
        public bool IncludeElementParams { get; set; } = false;
        public List<string> ParameterNames { get; set; } = new List<string>();
        public int MaxElementsPerRoom { get; set; } = 100;

        public AIResult<object> Result { get; private set; }
        public bool TaskCompleted { get; private set; }
        private readonly ManualResetEvent _resetEvent = new ManualResetEvent(false);

        public void SetParameters() { TaskCompleted = false; _resetEvent.Reset(); }
        public bool WaitForCompletion(int timeoutMilliseconds = 30000) { return _resetEvent.WaitOne(timeoutMilliseconds); }

        public void Execute(UIApplication app)
        {
            try
            {
                var doc = app.ActiveUIDocument.Document;
                Phase phase = doc.Phases.Cast<Phase>().Last();

                // ── Resolve target rooms ─────────────────────────────────
                List<Room> targetRooms;
                if (RoomIds.Count > 0)
                {
                    // Fast path: direct ID lookup
                    targetRooms = new List<Room>();
                    foreach (var id in RoomIds)
                    {
                        var elem = doc.GetElement(RevitMCPCommandSet.Utils.ElementIdExtensions.FromLong(id)) as Room;
                        if (elem != null && elem.Area > 0) targetRooms.Add(elem);
                    }
                }
                else
                {
                    var allRooms = new FilteredElementCollector(doc)
                        .OfCategory(BuiltInCategory.OST_Rooms)
                        .WhereElementIsNotElementType()
                        .Cast<Room>()
                        .Where(r => r.Area > 0);

                    if (RoomNumbers.Count > 0)
                    {
                        var numSet = new HashSet<string>(RoomNumbers, StringComparer.OrdinalIgnoreCase);
                        allRooms = allRooms.Where(r => numSet.Contains(
                            r.get_Parameter(BuiltInParameter.ROOM_NUMBER)?.AsString() ?? ""));
                    }

                    if (!string.IsNullOrEmpty(LevelName))
                    {
                        allRooms = allRooms.Where(r =>
                            r.Level != null && r.Level.Name.IndexOf(LevelName, StringComparison.OrdinalIgnoreCase) >= 0);
                    }

                    targetRooms = allRooms.ToList();
                }

                if (targetRooms.Count == 0)
                {
                    Result = new AIResult<object>
                    {
                        Success = true,
                        Message = "No matching rooms found",
                        Response = new { totalRooms = 0, totalDoors = 0, totalWindows = 0, rooms = new List<object>() }
                    };
                    return;
                }

                var roomIdSet = new HashSet<long>(targetRooms.Select(r => r.Id.GetValue()));

                // ── Collect and map doors/windows ────────────────────────
                bool includeDoors = ElementType == "doors" || ElementType == "both";
                bool includeWindows = ElementType == "windows" || ElementType == "both";

                var roomDoors = new Dictionary<long, List<FamilyInstance>>();
                var roomWindows = new Dictionary<long, List<FamilyInstance>>();
                foreach (var r in targetRooms)
                {
                    roomDoors[r.Id.GetValue()] = new List<FamilyInstance>();
                    roomWindows[r.Id.GetValue()] = new List<FamilyInstance>();
                }

                if (includeDoors)
                {
                    var collector = new FilteredElementCollector(doc)
                        .OfCategory(BuiltInCategory.OST_Doors)
                        .WhereElementIsNotElementType();


                    foreach (FamilyInstance door in collector)
                    {
                        long fromId = door.get_FromRoom(phase)?.Id.GetValue() ?? -1;
                        long toId = door.get_ToRoom(phase)?.Id.GetValue() ?? -1;

                        if (fromId > 0 && roomIdSet.Contains(fromId))
                            roomDoors[fromId].Add(door);
                        if (toId > 0 && toId != fromId && roomIdSet.Contains(toId))
                            roomDoors[toId].Add(door);
                    }
                }

                if (includeWindows)
                {
                    var collector = new FilteredElementCollector(doc)
                        .OfCategory(BuiltInCategory.OST_Windows)
                        .WhereElementIsNotElementType();


                    foreach (FamilyInstance win in collector)
                    {
                        long fromId = win.get_FromRoom(phase)?.Id.GetValue() ?? -1;
                        long toId = win.get_ToRoom(phase)?.Id.GetValue() ?? -1;

                        if (fromId > 0 && roomIdSet.Contains(fromId))
                            roomWindows[fromId].Add(win);
                        if (toId > 0 && toId != fromId && roomIdSet.Contains(toId))
                            roomWindows[toId].Add(win);
                    }
                }

                // ── Build results ────────────────────────────────────────
                var results = new List<object>();
                int totalDoors = 0, totalWindows = 0;

                // Pre-cache type info to avoid repeated lookups
                var typeCache = new Dictionary<long, (string familyName, string typeName, string width, string height)>();

                foreach (var room in targetRooms)
                {
                    var rid = room.Id.GetValue();
                    var doors = roomDoors[rid];
                    var windows = roomWindows[rid];

                    if (doors.Count > MaxElementsPerRoom) doors = doors.Take(MaxElementsPerRoom).ToList();
                    if (windows.Count > MaxElementsPerRoom) windows = windows.Take(MaxElementsPerRoom).ToList();

                    totalDoors += doors.Count;
                    totalWindows += windows.Count;

                    object roomParams = IncludeRoomParams ? ExtractParams(room) : null;

                    var doorList = includeDoors ? doors.Select(d => BuildOpeningInfo(d, phase, typeCache)).ToList() : null;
                    var winList = includeWindows ? windows.Select(w => BuildOpeningInfo(w, phase, typeCache)).ToList() : null;

                    results.Add(new
                    {
                        roomId = rid,
                        roomName = room.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString() ?? "",
                        roomNumber = room.get_Parameter(BuiltInParameter.ROOM_NUMBER)?.AsString() ?? "",
                        level = room.Level?.Name ?? "",
                        area = Math.Round(room.Area * 0.09290304, 2),
                        roomParameters = roomParams,
                        doorCount = doors.Count,
                        doors = (object)doorList,
                        windowCount = windows.Count,
                        windows = (object)winList
                    });
                }

                Result = new AIResult<object>
                {
                    Success = true,
                    Message = $"Found {totalDoors} doors and {totalWindows} windows across {targetRooms.Count} rooms",
                    Response = new { totalRooms = targetRooms.Count, totalDoors, totalWindows, rooms = results }
                };
            }
            catch (Exception ex)
            {
                Result = new AIResult<object> { Success = false, Message = $"Get room openings failed: {ex.Message}" };
            }
            finally
            {
                TaskCompleted = true;
                _resetEvent.Set();
            }
        }

        /// <summary>
        /// Builds a lean info object for a door/window. Always includes dimensions from the type.
        /// </summary>
        private object BuildOpeningInfo(FamilyInstance fi, Phase phase,
            Dictionary<long, (string familyName, string typeName, string width, string height)> typeCache)
        {
            // Type-level info (cached)
            var typeId = fi.GetTypeId().GetValue();
            if (!typeCache.ContainsKey(typeId))
            {
                var sym = fi.Symbol;
                var w = sym.get_Parameter(BuiltInParameter.DOOR_WIDTH)?.AsValueString()
                     ?? sym.get_Parameter(BuiltInParameter.WINDOW_WIDTH)?.AsValueString()
                     ?? sym.LookupParameter("Width")?.AsValueString()
                     ?? sym.LookupParameter("Rough Width")?.AsValueString() ?? "";
                var h = sym.get_Parameter(BuiltInParameter.DOOR_HEIGHT)?.AsValueString()
                     ?? sym.get_Parameter(BuiltInParameter.WINDOW_HEIGHT)?.AsValueString()
                     ?? sym.LookupParameter("Height")?.AsValueString()
                     ?? sym.LookupParameter("Rough Height")?.AsValueString() ?? "";
                typeCache[typeId] = (sym.FamilyName, sym.Name, w, h);
            }
            var cached = typeCache[typeId];

            // Instance-level: room association + key dimensions
            var fromRoom = fi.get_FromRoom(phase);
            var toRoom = fi.get_ToRoom(phase);
            string sillHeight = fi.get_Parameter(BuiltInParameter.INSTANCE_SILL_HEIGHT_PARAM)?.AsValueString()
                             ?? fi.LookupParameter("Sill Height")?.AsValueString() ?? "";
            string headHeight = fi.LookupParameter("Head Height")?.AsValueString() ?? "";
            string mark = fi.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.AsValueString() ?? "";

            // Extra params only when requested
            object extraParams = null;
            if (IncludeElementParams)
                extraParams = ExtractParams(fi);

            return new
            {
                elementId = fi.Id.GetValue(),
                familyName = cached.familyName,
                typeName = cached.typeName,
                width = cached.width,
                height = cached.height,
                sillHeight,
                headHeight,
                mark,
                level = fi.LevelId != ElementId.InvalidElementId ? fi.Document.GetElement(fi.LevelId)?.Name ?? "" : "",
                fromRoomNumber = fromRoom?.get_Parameter(BuiltInParameter.ROOM_NUMBER)?.AsString() ?? "",
                toRoomNumber = toRoom?.get_Parameter(BuiltInParameter.ROOM_NUMBER)?.AsString() ?? "",
                parameters = extraParams
            };
        }

        private object ExtractParams(Element elem)
        {
            var dict = new Dictionary<string, object>();

            if (ParameterNames.Count > 0)
            {
                foreach (var name in ParameterNames)
                {
                    var param = elem.LookupParameter(name);
                    if (param != null && param.HasValue)
                        dict[name] = GetParamValue(param);
                }
            }
            else
            {
                foreach (Parameter param in elem.Parameters)
                {
                    if (!param.HasValue) continue;
                    if (param.IsReadOnly && param.Definition.Name == "Category") continue;
                    var val = GetParamValue(param);
                    if (val != null && val.ToString() != "" && val.ToString() != "0" && val.ToString() != "-1")
                        dict[param.Definition.Name] = val;
                }
            }
            return dict;
        }

        private object GetParamValue(Parameter param)
        {
            switch (param.StorageType)
            {
                case StorageType.String: return param.AsString() ?? "";
                case StorageType.Integer: return param.AsValueString() ?? param.AsInteger().ToString();
                case StorageType.Double: return param.AsValueString() ?? param.AsDouble().ToString("F4");
                case StorageType.ElementId: return param.AsValueString() ?? param.AsElementId().GetValue().ToString();
                default: return null;
            }
        }

        public string GetName() => "Get Room Openings";
    }
}
