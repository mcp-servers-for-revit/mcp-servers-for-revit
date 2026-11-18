using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCPCommandSet.Models.Common;
using RevitMCPSDK.API.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace RevitMCPCommandSet.Services.ViewManagement
{
    public class CreateColorLegendEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        public string ParameterName { get; set; } = "";
        public List<string> Categories { get; set; } = new List<string>();
        public string ColorScheme { get; set; } = "auto"; // auto, gradient, custom
        public List<ColorMapping> CustomColors { get; set; } = new List<ColorMapping>();
        public bool CreateLegendView { get; set; } = true;
        public string LegendTitle { get; set; } = "Color Legend";
        public long TargetViewId { get; set; } = 0;

        public AIResult<object> Result { get; private set; }
        public bool TaskCompleted { get; private set; }
        private readonly ManualResetEvent _resetEvent = new ManualResetEvent(false);

        public void SetParameters() { TaskCompleted = false; _resetEvent.Reset(); }
        public bool WaitForCompletion(int timeoutMilliseconds = 60000) { return _resetEvent.WaitOne(timeoutMilliseconds); }

        public class ColorMapping
        {
            public string Value { get; set; }
            public int R { get; set; }
            public int G { get; set; }
            public int B { get; set; }
        }

        public void Execute(UIApplication app)
        {
            try
            {
                var doc = app.ActiveUIDocument.Document;

                // Resolve target view
                View targetView = null;
                if (TargetViewId != 0)
                {
#if REVIT2024_OR_GREATER
                    targetView = doc.GetElement(new ElementId(TargetViewId)) as View;
#else
                    targetView = doc.GetElement(new ElementId((int)TargetViewId)) as View;
#endif
                }
                if (targetView == null)
                    targetView = app.ActiveUIDocument.ActiveView;

                if (targetView == null)
                    throw new InvalidOperationException("No valid target view found");

                // Find solid fill pattern
                var solidFillPattern = new FilteredElementCollector(doc)
                    .OfClass(typeof(FillPatternElement))
                    .Cast<FillPatternElement>()
                    .FirstOrDefault(fp => fp.GetFillPattern().IsSolidFill);

                ElementId solidFillPatternId = solidFillPattern?.Id ?? ElementId.InvalidElementId;

                // Collect elements from specified categories
                var allElements = new List<Element>();
                var resolvedCategories = ResolveCategories(doc, Categories);

                foreach (var builtInCat in resolvedCategories)
                {
                    var collector = new FilteredElementCollector(doc, targetView.Id)
                        .OfCategory(builtInCat)
                        .WhereElementIsNotElementType()
                        .ToList();
                    allElements.AddRange(collector);
                }

                if (allElements.Count == 0)
                    throw new InvalidOperationException($"No elements found for categories: {string.Join(", ", Categories)}");

                // Group elements by parameter value
                var groups = new Dictionary<string, List<Element>>();
                foreach (var elem in allElements)
                {
                    string paramValue = GetParameterValue(elem, ParameterName);
                    if (paramValue == null) continue;

                    if (!groups.ContainsKey(paramValue))
                        groups[paramValue] = new List<Element>();
                    groups[paramValue].Add(elem);
                }

                if (groups.Count == 0)
                    throw new InvalidOperationException($"Parameter '{ParameterName}' not found on elements or all values are empty");

                // Generate color assignments
                var colorAssignments = GenerateColorAssignments(groups.Keys.ToList(), ColorScheme, CustomColors);

                // Apply overrides and track result
                var colorMappingResults = new List<object>();
                int totalColored = 0;
                long legendViewId = 0;

                using (var tg = new TransactionGroup(doc, "Create Color Legend"))
                {
                    tg.Start();

                    // Apply graphic overrides
                    using (var t = new Transaction(doc, "Apply Color Overrides"))
                    {
                        t.Start();
                        try
                        {

                        foreach (var kvp in groups)
                        {
                            string value = kvp.Key;
                            if (!colorAssignments.ContainsKey(value)) continue;

                            var (r, g, b) = colorAssignments[value];
                            var color = new Color((byte)r, (byte)g, (byte)b);

                            var ogs = new OverrideGraphicSettings();
                            ogs.SetSurfaceForegroundPatternColor(color);
                            if (solidFillPatternId != ElementId.InvalidElementId)
                                ogs.SetSurfaceForegroundPatternId(solidFillPatternId);
                            ogs.SetProjectionLineColor(color);

                            foreach (var elem in kvp.Value)
                            {
                                try
                                {
                                    targetView.SetElementOverrides(elem.Id, ogs);
                                    totalColored++;
                                }
                                catch { /* skip elements that cannot be overridden */ }
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

                    // Build color mapping result list
                    foreach (var kvp in colorAssignments)
                    {
                        int elemCount = groups.ContainsKey(kvp.Key) ? groups[kvp.Key].Count : 0;
                        colorMappingResults.Add(new
                        {
                            value = kvp.Key,
                            r = kvp.Value.Item1,
                            g = kvp.Value.Item2,
                            b = kvp.Value.Item3,
                            hex = $"#{kvp.Value.Item1:X2}{kvp.Value.Item2:X2}{kvp.Value.Item3:X2}",
                            elementCount = elemCount
                        });
                    }

                    // Create legend view
                    if (CreateLegendView)
                    {
                        using (var t = new Transaction(doc, "Create Legend View"))
                        {
                            t.Start();
                            try
                            {
                                var legendView = CreateLegendViewInternal(doc, colorAssignments, groups, solidFillPatternId);
                                if (legendView != null)
                                {
#if REVIT2024_OR_GREATER
                                    legendViewId = legendView.Id.Value;
#else
                                    legendViewId = legendView.Id.IntegerValue;
#endif
                                }
                                t.Commit();
                            }
                            catch
                            {
                                t.RollBack();
                            }
                        }
                    }

                    tg.Assimilate();
                }

                var guidance = new List<string>
                {
                    $"Applied color overrides to {totalColored} elements grouped by '{ParameterName}'",
                    "Use override_graphics to adjust individual element colors if needed",
                    "Run create_color_legend again with a different colorScheme to update the colors",
                    "To reset overrides, use override_graphics with resetToDefault=true on affected elements"
                };

                if (legendViewId != 0)
                    guidance.Add($"Legend view created (id={legendViewId}) - place it on a sheet using place_viewport");

                if (ColorScheme == "gradient" && groups.Count > 0)
                    guidance.Add("Gradient is blue (lowest) to red (highest) - ensure parameter values are numeric for best results");

                Result = new AIResult<object>
                {
                    Success = true,
                    Message = $"Colored {totalColored} elements across {groups.Count} unique values of '{ParameterName}'",
                    Response = new
                    {
                        totalColored,
                        uniqueValues = groups.Count,
                        parameterName = ParameterName,
                        colorScheme = ColorScheme,
#if REVIT2024_OR_GREATER
                        targetViewId = targetView.Id.Value,
#else
                        targetViewId = targetView.Id.IntegerValue,
#endif
                        targetViewName = targetView.Name,
                        legendViewId,
                        colorMappings = colorMappingResults,
                        guidance,
                        nextSteps = new[]
                        {
                            legendViewId != 0
                                ? $"Place the legend view on a sheet: place_viewport with viewId={legendViewId}"
                                : "Run again with createLegendView=true to generate a legend sheet",
                            "Use create_schedule to create a schedule matching the parameter values",
                            "Use create_view_filter if you want to isolate specific value groups"
                        }
                    }
                };
            }
            catch (Exception ex)
            {
                Result = new AIResult<object> { Success = false, Message = $"Create color legend failed: {ex.Message}" };
            }
            finally
            {
                TaskCompleted = true;
                _resetEvent.Set();
            }
        }

        // -----------------------------------------------------------------------
        // Helpers
        // -----------------------------------------------------------------------

        private List<BuiltInCategory> ResolveCategories(Document doc, List<string> categoryNames)
        {
            var resolved = new List<BuiltInCategory>();
            var categoryMap = new Dictionary<string, BuiltInCategory>(StringComparer.OrdinalIgnoreCase)
            {
                { "Rooms",          BuiltInCategory.OST_Rooms },
                { "Walls",          BuiltInCategory.OST_Walls },
                { "Floors",         BuiltInCategory.OST_Floors },
                { "Ceilings",       BuiltInCategory.OST_Ceilings },
                { "Doors",          BuiltInCategory.OST_Doors },
                { "Windows",        BuiltInCategory.OST_Windows },
                { "Columns",        BuiltInCategory.OST_Columns },
                { "StructuralColumns", BuiltInCategory.OST_StructuralColumns },
                { "StructuralFraming",  BuiltInCategory.OST_StructuralFraming },
                { "Furniture",      BuiltInCategory.OST_Furniture },
                { "FurnitureSystems", BuiltInCategory.OST_FurnitureSystems },
                { "MechanicalEquipment", BuiltInCategory.OST_MechanicalEquipment },
                { "Roofs",          BuiltInCategory.OST_Roofs },
                { "Areas",          BuiltInCategory.OST_Areas },
                { "Spaces",         BuiltInCategory.OST_MEPSpaces },
                { "Pipes",          BuiltInCategory.OST_PipeCurves },
                { "Ducts",          BuiltInCategory.OST_DuctCurves },
                { "GenericModels",  BuiltInCategory.OST_GenericModel },
            };

            foreach (var name in categoryNames)
            {
                if (categoryMap.TryGetValue(name, out var bic))
                    resolved.Add(bic);
            }

            // Default to Rooms if nothing matched
            if (resolved.Count == 0)
                resolved.Add(BuiltInCategory.OST_Rooms);

            return resolved;
        }

        private string GetParameterValue(Element elem, string paramName)
        {
            // Try instance parameter first
            var param = elem.LookupParameter(paramName);
            if (param == null)
            {
                // Try type parameter
                var typeId = elem.GetTypeId();
                if (typeId != null && typeId != ElementId.InvalidElementId)
                {
                    var elemType = elem.Document.GetElement(typeId);
                    param = elemType?.LookupParameter(paramName);
                }
            }
            if (param == null) return null;

            switch (param.StorageType)
            {
                case StorageType.String:
                    return param.AsString() ?? "";
                case StorageType.Integer:
                    return param.AsInteger().ToString();
                case StorageType.Double:
                    return Math.Round(param.AsDouble(), 4).ToString();
                case StorageType.ElementId:
#if REVIT2024_OR_GREATER
                    return param.AsElementId().Value.ToString();
#else
                    return param.AsElementId().IntegerValue.ToString();
#endif
                default:
                    return param.AsValueString() ?? "";
            }
        }

        private Dictionary<string, (int R, int G, int B)> GenerateColorAssignments(
            List<string> values,
            string scheme,
            List<ColorMapping> customColors)
        {
            var result = new Dictionary<string, (int, int, int)>();

            if (scheme == "custom" && customColors != null && customColors.Count > 0)
            {
                foreach (var cm in customColors)
                    result[cm.Value] = (Clamp(cm.R), Clamp(cm.G), Clamp(cm.B));

                // Fill unspecified values with auto colors
                var unspecified = values.Where(v => !result.ContainsKey(v)).ToList();
                var autoFill = GenerateAutoColors(unspecified);
                foreach (var kvp in autoFill)
                    result[kvp.Key] = kvp.Value;

                return result;
            }

            if (scheme == "gradient")
            {
                // Try to sort values numerically; fall back to alphabetical
                var sortedValues = values
                    .Select(v => (value: v, numeric: double.TryParse(v, out double n) ? (double?)n : null))
                    .OrderBy(x => x.numeric ?? double.MaxValue)
                    .ThenBy(x => x.value)
                    .Select(x => x.value)
                    .ToList();

                int count = sortedValues.Count;
                for (int i = 0; i < count; i++)
                {
                    double t = count > 1 ? (double)i / (count - 1) : 0.5;
                    // Blue (low) -> Cyan -> Green -> Yellow -> Red (high)
                    int r, g, b;
                    if (t < 0.25)
                    {
                        double s = t / 0.25;
                        r = 0; g = (int)(255 * s); b = 255;
                    }
                    else if (t < 0.5)
                    {
                        double s = (t - 0.25) / 0.25;
                        r = 0; g = 255; b = (int)(255 * (1 - s));
                    }
                    else if (t < 0.75)
                    {
                        double s = (t - 0.5) / 0.25;
                        r = (int)(255 * s); g = 255; b = 0;
                    }
                    else
                    {
                        double s = (t - 0.75) / 0.25;
                        r = 255; g = (int)(255 * (1 - s)); b = 0;
                    }
                    result[sortedValues[i]] = (r, g, b);
                }
                return result;
            }

            // Default: auto — distinct hues via HSL rotation
            return GenerateAutoColors(values);
        }

        private Dictionary<string, (int R, int G, int B)> GenerateAutoColors(List<string> values)
        {
            var result = new Dictionary<string, (int, int, int)>();
            int count = values.Count;

            // Use a set of hand-picked visually distinct hues for small sets,
            // then fall back to golden-angle HSL rotation for larger sets.
            var presetHues = new[] { 0, 210, 120, 30, 270, 180, 45, 300, 150, 60, 240, 15 };

            for (int i = 0; i < count; i++)
            {
                double hue = i < presetHues.Length
                    ? presetHues[i]
                    : (presetHues.Length * 137.508 + (i - presetHues.Length) * 137.508) % 360;

                var (r, g, b) = HslToRgb(hue, 0.65, 0.55);
                result[values[i]] = (r, g, b);
            }
            return result;
        }

        private (int R, int G, int B) HslToRgb(double h, double s, double l)
        {
            double c = (1 - Math.Abs(2 * l - 1)) * s;
            double x = c * (1 - Math.Abs((h / 60) % 2 - 1));
            double m = l - c / 2;

            double r1, g1, b1;
            if      (h < 60)  { r1 = c; g1 = x; b1 = 0; }
            else if (h < 120) { r1 = x; g1 = c; b1 = 0; }
            else if (h < 180) { r1 = 0; g1 = c; b1 = x; }
            else if (h < 240) { r1 = 0; g1 = x; b1 = c; }
            else if (h < 300) { r1 = x; g1 = 0; b1 = c; }
            else              { r1 = c; g1 = 0; b1 = x; }

            return (
                (int)Math.Round((r1 + m) * 255),
                (int)Math.Round((g1 + m) * 255),
                (int)Math.Round((b1 + m) * 255)
            );
        }

        private int Clamp(int v) => Math.Max(0, Math.Min(255, v));

        private View CreateLegendViewInternal(
            Document doc,
            Dictionary<string, (int R, int G, int B)> colorAssignments,
            Dictionary<string, List<Element>> groups,
            ElementId solidFillPatternId)
        {
            // Create a Drafting view to serve as the legend
            // (Legend views cannot be created programmatically in most Revit versions,
            //  so we use a Drafting view with the same visual content)
            var draftingVft = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewFamilyType))
                .Cast<ViewFamilyType>()
                .FirstOrDefault(v => v.ViewFamily == ViewFamily.Drafting);

            if (draftingVft == null) return null;

            var legendView = ViewDrafting.Create(doc, draftingVft.Id);

            // Set a unique name
            string baseName = string.IsNullOrEmpty(LegendTitle) ? "Color Legend" : LegendTitle;
            string legendName = baseName;
            int suffix = 1;
            while (true)
            {
                try
                {
                    legendView.Name = legendName;
                    break;
                }
                catch
                {
                    legendName = $"{baseName} {suffix++}";
                    if (suffix > 100) break;
                }
            }

            // Layout parameters
            double swatchSize = 3.0 / 12.0;        // 3 inches in feet
            double rowHeight = 4.0 / 12.0;          // 4 inches row spacing
            double textOffset = 4.5 / 12.0;         // offset from swatch left to text
            double startX = 0.0;
            double startY = 0.0;
            double titleOffset = rowHeight * 1.5;

            // Add title text note
            var titleOptions = new TextNoteOptions
            {
                HorizontalAlignment = HorizontalTextAlignment.Left,
                TypeId = GetDefaultTextNoteTypeId(doc)
            };
            TextNote.Create(doc, legendView.Id,
                new XYZ(startX, startY, 0),
                $"{LegendTitle} — {ParameterName}",
                titleOptions);

            // Add a row for each color mapping
            var sortedKeys = colorAssignments.Keys.OrderBy(k => k).ToList();
            int rowIndex = 0;

            foreach (var value in sortedKeys)
            {
                if (!colorAssignments.TryGetValue(value, out var rgb)) continue;

                double rowY = startY - titleOffset - rowIndex * rowHeight;

                // Draw a filled region "swatch"
                try
                {
                    var swatchColor = new Color((byte)rgb.R, (byte)rgb.G, (byte)rgb.B);
                    CreateColorSwatch(doc, legendView, solidFillPatternId,
                        new XYZ(startX, rowY - swatchSize / 2, 0),
                        new XYZ(startX + swatchSize, rowY + swatchSize / 2, 0),
                        swatchColor);
                }
                catch { /* skip swatch if region creation fails */ }

                // Add text note beside the swatch
                int elemCount = groups.ContainsKey(value) ? groups[value].Count : 0;
                string label = $"{value}  ({elemCount} element{(elemCount == 1 ? "" : "s")})";

                var textOptions = new TextNoteOptions
                {
                    HorizontalAlignment = HorizontalTextAlignment.Left,
                    TypeId = GetDefaultTextNoteTypeId(doc)
                };
                TextNote.Create(doc, legendView.Id,
                    new XYZ(startX + textOffset, rowY, 0),
                    label,
                    textOptions);

                rowIndex++;
            }

            return legendView;
        }

        private void CreateColorSwatch(
            Document doc,
            View legendView,
            ElementId fillPatternId,
            XYZ min,
            XYZ max,
            Color color)
        {
            if (fillPatternId == ElementId.InvalidElementId) return;

            // Find or create a filled region type for this color
            var regionType = new FilteredElementCollector(doc)
                .OfClass(typeof(FilledRegionType))
                .Cast<FilledRegionType>()
                .FirstOrDefault();

            if (regionType == null) return;

            // Duplicate type and set color
            var dupType = regionType.Duplicate($"_ColorLegend_{color.Red}_{color.Green}_{color.Blue}") as FilledRegionType;
            if (dupType != null)
            {
                dupType.ForegroundPatternId = fillPatternId;
                dupType.ForegroundPatternColor = color;
                dupType.BackgroundPatternColor = color;
            }

            var dupTypeId = dupType?.Id ?? regionType.Id;

            // Build boundary loop
            var loop = new CurveLoop();
            loop.Append(Line.CreateBound(new XYZ(min.X, min.Y, 0), new XYZ(max.X, min.Y, 0)));
            loop.Append(Line.CreateBound(new XYZ(max.X, min.Y, 0), new XYZ(max.X, max.Y, 0)));
            loop.Append(Line.CreateBound(new XYZ(max.X, max.Y, 0), new XYZ(min.X, max.Y, 0)));
            loop.Append(Line.CreateBound(new XYZ(min.X, max.Y, 0), new XYZ(min.X, min.Y, 0)));

            FilledRegion.Create(doc, dupTypeId, legendView.Id, new List<CurveLoop> { loop });
        }

        private ElementId GetDefaultTextNoteTypeId(Document doc)
        {
            var textNoteType = new FilteredElementCollector(doc)
                .OfClass(typeof(TextNoteType))
                .Cast<TextNoteType>()
                .FirstOrDefault();
            return textNoteType?.Id ?? ElementId.InvalidElementId;
        }

        public string GetName() => "Create Color Legend";
    }
}
