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
    public class CalculateRaiEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        public List<long> RoomIds { get; set; } = new List<long>();
        public List<string> RoomNumbers { get; set; } = new List<string>();
        public string LevelName { get; set; } = "";
        public double MinRatio { get; set; } = 0.125;
        public bool IncludeServiceRooms { get; set; } = false;
        public string PhaseName { get; set; } = "";
        public Dictionary<string, double> RatioOverrides { get; set; } = new Dictionary<string, double>();

        public AIResult<object> Result { get; private set; }
        public bool TaskCompleted { get; private set; }
        private readonly ManualResetEvent _resetEvent = new ManualResetEvent(false);

        public void SetParameters() { TaskCompleted = false; _resetEvent.Reset(); }
        public bool WaitForCompletion(int timeoutMilliseconds = 30000) { return _resetEvent.WaitOne(timeoutMilliseconds); }

        private const double FT_TO_M = 0.3048;
        private const double SQFT_TO_M2 = 0.09290304;

        public void Execute(UIApplication app)
        {
            try
            {
                var doc = app.ActiveUIDocument.Document;

                // ── 1. RESOLVE PHASE ────────────────────────────────────
                // User-specified > active view phase > last phase
                Phase phase = null;
                if (!string.IsNullOrEmpty(PhaseName))
                {
                    phase = doc.Phases.Cast<Phase>()
                        .FirstOrDefault(p => p.Name.IndexOf(PhaseName, StringComparison.OrdinalIgnoreCase) >= 0);
                }
                if (phase == null)
                {
                    // Try active view's phase
                    var activeView = app.ActiveUIDocument.ActiveView;
                    var phaseParam = activeView?.get_Parameter(BuiltInParameter.VIEW_PHASE);
                    if (phaseParam != null)
                    {
                        var phaseId = phaseParam.AsElementId();
                        if (phaseId != ElementId.InvalidElementId)
                            phase = doc.GetElement(phaseId) as Phase;
                    }
                }
                if (phase == null)
                    phase = doc.Phases.Cast<Phase>().Last();

                string phaseName = phase.Name;

                // ── 2. COLLECT ROOMS (primary entity) ───────────────────
                List<Room> targetRooms;
                if (RoomIds.Count > 0)
                {
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

                // ── 3. FILTER SERVICE ROOMS ─────────────────────────────
                if (!IncludeServiceRooms)
                {
                    var serviceKeywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                    {
                        "bagno", "wc", "toilette", "antibagno",
                        "corridoio", "disimpegno", "ingresso", "atrio",
                        "ripostiglio", "sgabuzzino", "cantina",
                        "garage", "autorimessa", "box auto",
                        "lavanderia", "locale tecnico",
                        "vano scala", "scala", "ascensore"
                    };
                    targetRooms = targetRooms.Where(r =>
                    {
                        var name = (r.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString() ?? "").Trim();
                        return !serviceKeywords.Any(kw => name.IndexOf(kw, StringComparison.OrdinalIgnoreCase) >= 0);
                    }).ToList();
                }

                if (targetRooms.Count == 0)
                {
                    Result = new AIResult<object>
                    {
                        Success = true,
                        Message = "No matching rooms found",
                        Response = new
                        {
                            summary = new { totalRooms = 0, compliant = 0, nonCompliant = 0, minRatioThreshold = MinRatio, phase = phaseName },
                            rooms = new List<object>()
                        }
                    };
                    return;
                }

                // ── 4. COLLECT WINDOWS ONCE, MAP TO ROOMS ───────────────
                // Build room lookup set
                var roomIdSet = new HashSet<long>(targetRooms.Select(r => r.Id.GetValue()));
                var roomWindows = new Dictionary<long, List<FamilyInstance>>();
                foreach (var r in targetRooms)
                    roomWindows[r.Id.GetValue()] = new List<FamilyInstance>();

                // Single pass over all windows, using the resolved phase for room association
                foreach (FamilyInstance win in new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_Windows)
                    .WhereElementIsNotElementType())
                {
                    // get_FromRoom/get_ToRoom with the correct phase
                    var fromRoom = win.get_FromRoom(phase);
                    var toRoom = win.get_ToRoom(phase);

                    long fromId = fromRoom?.Id.GetValue() ?? -1;
                    long toId = toRoom?.Id.GetValue() ?? -1;

                    // A window illuminates the room it faces — count for both sides
                    if (fromId > 0 && roomIdSet.Contains(fromId))
                        roomWindows[fromId].Add(win);
                    if (toId > 0 && toId != fromId && roomIdSet.Contains(toId))
                        roomWindows[toId].Add(win);
                }

                // ── 5. TYPE CACHE FOR WINDOW DIMENSIONS ─────────────────
                var typeCache = new Dictionary<long, (string familyName, string typeName, double widthM, double heightM, double areaM2)>();

                // ── 6. PER-ROOM RAI CALCULATION ─────────────────────────
                var results = new List<object>();
                int totalCompliant = 0, totalNonCompliant = 0;

                foreach (var room in targetRooms)
                {
                    var rid = room.Id.GetValue();
                    var roomName = (room.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString() ?? "").Trim();
                    var roomNumber = room.get_Parameter(BuiltInParameter.ROOM_NUMBER)?.AsString() ?? "";
                    var levelName = room.Level?.Name ?? "";
                    double floorAreaM2 = Math.Round(room.Area * SQFT_TO_M2, 2);

                    // Get room's created phase for output info
                    var roomPhaseParam = room.get_Parameter(BuiltInParameter.ROOM_PHASE);
                    var roomPhaseName = roomPhaseParam != null
                        ? (doc.GetElement(roomPhaseParam.AsElementId()) as Phase)?.Name ?? phaseName
                        : phaseName;

                    // Process windows for this room
                    var windows = roomWindows[rid];
                    var windowList = new List<object>();
                    double totalWindowAreaM2 = 0;

                    foreach (var win in windows)
                    {
                        var typeId = win.GetTypeId().GetValue();
                        if (!typeCache.ContainsKey(typeId))
                        {
                            var sym = win.Symbol;

                            double widthFt = sym.get_Parameter(BuiltInParameter.WINDOW_WIDTH)?.AsDouble() ?? 0;
                            if (widthFt == 0) widthFt = sym.LookupParameter("Width")?.AsDouble() ?? 0;
                            if (widthFt == 0) widthFt = sym.LookupParameter("Rough Width")?.AsDouble() ?? 0;
                            if (widthFt == 0) widthFt = win.get_Parameter(BuiltInParameter.FAMILY_WIDTH_PARAM)?.AsDouble() ?? 0;

                            double heightFt = sym.get_Parameter(BuiltInParameter.WINDOW_HEIGHT)?.AsDouble() ?? 0;
                            if (heightFt == 0) heightFt = sym.LookupParameter("Height")?.AsDouble() ?? 0;
                            if (heightFt == 0) heightFt = sym.LookupParameter("Rough Height")?.AsDouble() ?? 0;
                            if (heightFt == 0) heightFt = win.get_Parameter(BuiltInParameter.FAMILY_HEIGHT_PARAM)?.AsDouble() ?? 0;

                            double widthM = widthFt * FT_TO_M;
                            double heightM = heightFt * FT_TO_M;
                            double areaM2 = widthM * heightM;

                            typeCache[typeId] = (sym.FamilyName, sym.Name, widthM, heightM, areaM2);
                        }

                        var cached = typeCache[typeId];
                        totalWindowAreaM2 += cached.areaM2;

                        windowList.Add(new
                        {
                            elementId = win.Id.GetValue(),
                            familyName = cached.familyName,
                            typeName = cached.typeName,
                            widthM = Math.Round(cached.widthM, 3),
                            heightM = Math.Round(cached.heightM, 3),
                            areaM2 = Math.Round(cached.areaM2, 3)
                        });
                    }

                    totalWindowAreaM2 = Math.Round(totalWindowAreaM2, 2);
                    double ratio = floorAreaM2 > 0 ? totalWindowAreaM2 / floorAreaM2 : 0;

                    // Determine applicable threshold
                    double effectiveMinRatio = MinRatio;
                    string overrideMatch = null;
                    if (RatioOverrides != null && RatioOverrides.Count > 0)
                    {
                        foreach (var kvp in RatioOverrides)
                        {
                            if (roomName.IndexOf(kvp.Key, StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                effectiveMinRatio = kvp.Value;
                                overrideMatch = kvp.Key;
                                break;
                            }
                        }
                    }

                    bool compliant = ratio >= effectiveMinRatio;
                    string ratioFraction = ratio > 0 ? $"1/{Math.Round(1.0 / ratio, 2)}" : "0";
                    string effectiveFraction = effectiveMinRatio > 0 ? $"1/{Math.Round(1.0 / effectiveMinRatio)}" : "0";
                    double minimumWindowAreaM2 = Math.Round(floorAreaM2 * effectiveMinRatio, 2);
                    double deficit = compliant ? 0 : Math.Round(minimumWindowAreaM2 - totalWindowAreaM2, 2);

                    if (compliant) totalCompliant++; else totalNonCompliant++;

                    results.Add(new
                    {
                        roomId = rid,
                        roomName,
                        roomNumber,
                        level = levelName,
                        phase = roomPhaseName,
                        floorAreaM2,
                        totalWindowAreaM2,
                        ratio = Math.Round(ratio, 4),
                        ratioFraction,
                        compliant,
                        appliedThreshold = effectiveMinRatio,
                        appliedThresholdFraction = effectiveFraction,
                        thresholdOverride = overrideMatch,
                        minimumWindowAreaM2,
                        deficit,
                        windowCount = windows.Count,
                        windows = windowList
                    });
                }

                string minFraction = MinRatio > 0 ? $"1/{Math.Round(1.0 / MinRatio)}" : "0";

                Result = new AIResult<object>
                {
                    Success = true,
                    Message = $"RAI calculated for {targetRooms.Count} room(s): {totalCompliant} compliant, {totalNonCompliant} non-compliant (threshold {minFraction}, phase '{phaseName}')",
                    Response = new
                    {
                        summary = new
                        {
                            totalRooms = targetRooms.Count,
                            compliant = totalCompliant,
                            nonCompliant = totalNonCompliant,
                            minRatioThreshold = MinRatio,
                            minRatioFraction = minFraction,
                            serviceRoomsExcluded = !IncludeServiceRooms,
                            phase = phaseName
                        },
                        rooms = results
                    }
                };
            }
            catch (Exception ex)
            {
                Result = new AIResult<object> { Success = false, Message = $"RAI calculation failed: {ex.Message}" };
            }
            finally
            {
                TaskCompleted = true;
                _resetEvent.Set();
            }
        }

        public string GetName() => "Calculate RAI";
    }
}
