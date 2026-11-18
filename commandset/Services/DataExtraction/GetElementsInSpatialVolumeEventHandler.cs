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
    public class GetElementsInSpatialVolumeEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        public List<long> VolumeIds { get; set; } = new List<long>();
        public string VolumeType { get; set; } = "room"; // room, area, custom
        public List<string> CategoryFilter { get; set; } = new List<string>(); // filter elements by category
        public int MaxElementsPerVolume { get; set; } = 100;
        public double CustomMinX { get; set; }
        public double CustomMinY { get; set; }
        public double CustomMinZ { get; set; }
        public double CustomMaxX { get; set; }
        public double CustomMaxY { get; set; }
        public double CustomMaxZ { get; set; }

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
                var volumeResults = new List<object>();
                int totalElements = 0;

                if (VolumeType.ToLower() == "custom")
                {
                    // Custom bounding box
                    double minXFt = CustomMinX / 304.8, minYFt = CustomMinY / 304.8, minZFt = CustomMinZ / 304.8;
                    double maxXFt = CustomMaxX / 304.8, maxYFt = CustomMaxY / 304.8, maxZFt = CustomMaxZ / 304.8;

                    var outline = new Outline(new XYZ(minXFt, minYFt, minZFt), new XYZ(maxXFt, maxYFt, maxZFt));
                    var bbFilter = new BoundingBoxIntersectsFilter(outline);
                    var collector = new FilteredElementCollector(doc).WherePasses(bbFilter).WhereElementIsNotElementType();

                    var elements = FilterByCategories(doc, collector, CategoryFilter);

                    int totalInVolume = elements.Count;
                    bool volumeTruncated = elements.Count > MaxElementsPerVolume;
                    if (volumeTruncated)
                        elements = elements.Take(MaxElementsPerVolume).ToList();

                    totalElements += totalInVolume;
                    volumeResults.Add(new
                    {
                        volumeType = "custom",
                        volumeId = (long)0,
                        volumeName = "Custom Bounding Box",
                        elementCount = elements.Count,
                        totalElementCount = totalInVolume,
                        truncated = volumeTruncated,
                        elements = elements.Select(e => FormatElement(e)).ToList()
                    });
                }
                else
                {
                    // Get spatial elements (rooms or areas)
                    var spatialElements = new List<Element>();
                    BuiltInCategory bic = VolumeType.ToLower() == "area" ? BuiltInCategory.OST_Areas : BuiltInCategory.OST_Rooms;

                    if (VolumeIds.Count > 0)
                    {
                        foreach (var id in VolumeIds)
                        {
#if REVIT2024_OR_GREATER
                            var elem = doc.GetElement(new ElementId(id));
#else
                            var elem = doc.GetElement(new ElementId((int)id));
#endif
                            if (elem != null) spatialElements.Add(elem);
                        }
                    }
                    else
                    {
                        spatialElements = new FilteredElementCollector(doc)
                            .OfCategory(bic)
                            .WhereElementIsNotElementType()
                            .ToList();
                    }

                    foreach (var spatial in spatialElements)
                    {
                        var bb = spatial.get_BoundingBox(null);
                        if (bb == null) continue;

                        // Check if room/area has valid area
                        if (spatial is Room room && room.Area <= 0) continue;

                        var outline = new Outline(bb.Min, bb.Max);
                        var bbFilter = new BoundingBoxIntersectsFilter(outline);
                        var collector = new FilteredElementCollector(doc)
                            .WherePasses(bbFilter)
                            .WhereElementIsNotElementType();

                        var elements = FilterByCategories(doc, collector, CategoryFilter);

                        // Exclude the spatial element itself
#if REVIT2024_OR_GREATER
                        elements = elements.Where(e => e.Id.Value != spatial.Id.Value).ToList();
#else
                        elements = elements.Where(e => e.Id.IntegerValue != spatial.Id.IntegerValue).ToList();
#endif

                        string name = "";
                        string number = "";
                        if (spatial is Room r)
                        {
                            name = r.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString() ?? "";
                            number = r.Number;
                        }

                        int totalInVolume = elements.Count;
                        bool volumeTruncated = elements.Count > MaxElementsPerVolume;
                        if (volumeTruncated)
                            elements = elements.Take(MaxElementsPerVolume).ToList();

                        totalElements += totalInVolume;
                        volumeResults.Add(new
                        {
                            volumeType = VolumeType,
#if REVIT2024_OR_GREATER
                            volumeId = spatial.Id.Value,
#else
                            volumeId = spatial.Id.IntegerValue,
#endif
                            volumeName = !string.IsNullOrEmpty(number) ? $"{number} - {name}" : spatial.Name,
                            elementCount = elements.Count,
                            totalElementCount = totalInVolume,
                            truncated = volumeTruncated,
                            elements = elements.Select(e => FormatElement(e)).ToList()
                        });
                    }
                }

                Result = new AIResult<object>
                {
                    Success = true,
                    Message = $"Found {totalElements} elements across {volumeResults.Count} volumes",
                    Response = new
                    {
                        totalElements,
                        volumeCount = volumeResults.Count,
                        categoryFilter = CategoryFilter,
                        volumes = volumeResults
                    }
                };
            }
            catch (Exception ex)
            {
                Result = new AIResult<object> { Success = false, Message = $"Get elements in volume failed: {ex.Message}" };
            }
            finally
            {
                TaskCompleted = true;
                _resetEvent.Set();
            }
        }

        private List<Element> FilterByCategories(Document doc, FilteredElementCollector collector, List<string> categories)
        {
            var elements = collector.ToList();
            if (categories != null && categories.Count > 0)
            {
                // Resolve category names to ElementIds upfront (language-independent)
                var resolvedIds = categories
                    .Select(c => CategoryResolver.ResolveToId(doc, c))
                    .Where(id => id != null)
                    .ToHashSet();

                elements = elements
                    .Where(e => e.Category != null && resolvedIds.Contains(e.Category.Id))
                    .ToList();
            }
            return elements;
        }

        private object FormatElement(Element e)
        {
            return new
            {
#if REVIT2024_OR_GREATER
                elementId = e.Id.Value,
#else
                elementId = e.Id.IntegerValue,
#endif
                name = e.Name,
                category = e.Category?.Name ?? "Unknown",
                familyName = (e as FamilyInstance)?.Symbol?.FamilyName ?? "",
                typeName = (e as FamilyInstance)?.Symbol?.Name ?? ""
            };
        }

        public string GetName() => "Get Elements In Spatial Volume";
    }
}
