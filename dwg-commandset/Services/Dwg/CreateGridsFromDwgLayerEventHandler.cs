using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCPCommandSet.Services.Dwg;
using RevitMCPCommandSet.Utils.Dwg;
using RevitMCPSDK.API.Interfaces;

namespace RevitMCPCommandSet.Services.Dwg
{
    /// <summary>
    /// Mutating handler that creates Revit grids from long straight lines on
    /// one DWG layer. Lines are ordered by angle bucket then position and
    /// labeled with a single alphabetic/numeric sequence. Non-line curves
    /// (bubbles, circles) and short ticks are skipped and reported.
    /// </summary>
    public class CreateGridsFromDwgLayerEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        private readonly ManualResetEvent _resetEvent = new ManualResetEvent(false);

        public string DwgNameOrId { get; set; }
        public string Layer { get; set; }
        public double MinLengthFt { get; set; } = 5.0;
        public string NamingStyle { get; set; } = "alphabetic";
        public string StartLabel { get; set; }
        public long LevelId { get; set; } = -1;

        public object Result { get; private set; }

        public bool WaitForCompletion(int timeoutMilliseconds = 120000)
        {
            _resetEvent.Reset();
            return _resetEvent.WaitOne(timeoutMilliseconds);
        }

        public void Execute(UIApplication app)
        {
            try
            {
                var doc = app.ActiveUIDocument?.Document;
                if (doc == null)
                {
                    Result = new Dictionary<string, object> { ["error"] = "No active document" };
                    return;
                }

                if (string.IsNullOrWhiteSpace(DwgNameOrId) || string.IsNullOrWhiteSpace(Layer))
                {
                    Result = new Dictionary<string, object> { ["error"] = "dwgNameOrId and layer are required" };
                    return;
                }

                Element target = DwgCurveSource.ResolveDwg(doc, DwgNameOrId, out string targetName, out string targetKind);
                if (target == null)
                {
                    Result = new Dictionary<string, object> { ["error"] = $"No imported or linked DWG matching '{DwgNameOrId}'" };
                    return;
                }

                var allCurves = DwgCurveSource.CollectLayerCurves(doc, target, Layer.Trim());

                // Filter: straight lines long enough to be grid datum.
                var gridLines = new List<Line>();
                int skippedNonLine = 0, skippedTooShort = 0;
                foreach (var cv in allCurves)
                {
                    if (!(cv is Line ln))
                    {
                        skippedNonLine++;
                        continue;
                    }
                    double len = 0;
                    try { len = ln.ApproximateLength; } catch { }
                    if (len < MinLengthFt)
                    {
                        skippedTooShort++;
                        continue;
                    }
                    gridLines.Add(ln);
                }

                if (gridLines.Count == 0)
                {
                    Result = new Dictionary<string, object>
                    {
                        ["error"] = $"No eligible grid lines on layer '{Layer}' of '{targetName}'",
                        ["skippedNonLine"] = skippedNonLine,
                        ["skippedTooShort"] = skippedTooShort,
                        ["hint"] = "Grids require straight lines; lower minLengthFt if needed"
                    };
                    return;
                }

                // Order: angle bucket (15 deg, normalized to [0,180)) then midpoint position.
                var ordered = gridLines
                    .Select(ln =>
                    {
                        var s = ln.GetEndPoint(0);
                        var e = ln.GetEndPoint(1);
                        double ang = Math.Atan2(e.Y - s.Y, e.X - s.X) * 180.0 / Math.PI;
                        ang = ((ang % 180.0) + 180.0) % 180.0;
                        var mid = (s + e) * 0.5;
                        return new { Line = ln, AngBucket = (int)Math.Round(ang / 15.0), Mid = mid };
                    })
                    .OrderBy(x => x.AngBucket)
                    .ThenBy(x => Math.Round(x.Mid.X, 6))
                    .ThenBy(x => Math.Round(x.Mid.Y, 6))
                    .Select(x => x.Line)
                    .ToList();

                // Level: explicit id, else nearest to layer median Z.
                double z = DwgCurveSource.MedianZ(allCurves);
                Level level = null;
                if (LevelId > 0)
                    level = doc.GetElement(new ElementId((int)LevelId)) as Level;
                if (level == null)
                {
                    level = new FilteredElementCollector(doc)
                        .OfClass(typeof(Level))
                        .Cast<Level>()
                        .OrderBy(l => Math.Abs(l.Elevation - z))
                        .FirstOrDefault();
                }
                if (level == null)
                {
                    Result = new Dictionary<string, object> { ["error"] = "No level found in document" };
                    return;
                }

                // Existing grid names for dedup.
                var existingNames = new FilteredElementCollector(doc)
                    .OfClass(typeof(Grid))
                    .Cast<Grid>()
                    .Select(g => g.Name)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                // Generate labels.
                int count = ordered.Count;
                var labels = GenerateLabels(count, NamingStyle, StartLabel);

                var preprocessor = new SilentWarningsPreprocessor();
                var createdIds = new List<long>();
                var createdNames = new List<string>();
                int created = 0;

                using (var trans = new Transaction(doc, "Create Grids from DWG Layer"))
                {
                    var fo = trans.GetFailureHandlingOptions();
                    trans.SetFailureHandlingOptions(fo.SetFailuresPreprocessor(preprocessor));
                    trans.Start();

                    foreach (var ln in ordered)
                    {
                        string label = labels[created];
                        string unique = GetUniqueName(label, existingNames);
                        try
                        {
                            var grid = Grid.Create(doc, ln);
                            grid.Name = unique;
                            existingNames.Add(unique);
                            createdIds.Add(DwgCurveSource.IdValue(grid));
                            created++;
                        }
                        catch
                        {
                            // skip failures silently; report count
                        }
                    }

                    trans.Commit();
                }

                Result = new Dictionary<string, object>
                {
                    ["source"] = new Dictionary<string, object>
                    {
                        ["id"] = DwgCurveSource.IdValue(target),
                        ["name"] = targetName,
                        ["kind"] = targetKind
                    },
                    ["layer"] = Layer.Trim(),
                    ["level"] = new Dictionary<string, object> { ["id"] = DwgCurveSource.IdValue(level), ["name"] = level.Name, ["elevation"] = level.Elevation },
                    ["medianZ"] = z,
                    ["candidateLines"] = gridLines.Count,
                    ["created"] = created,
                    ["skippedNonLine"] = skippedNonLine,
                    ["skippedTooShort"] = skippedTooShort,
                    ["minLengthFt"] = MinLengthFt,
                    ["namingStyle"] = NamingStyle,
                    ["createdIds"] = createdIds,
                    ["createdNames"] = labels.Take(created).ToList(),
                    ["suppressedMessages"] = preprocessor.Log
                };
            }
            catch (Exception ex)
            {
                Result = new Dictionary<string, object> { ["error"] = ex.Message, ["stack"] = ex.StackTrace };
            }
            finally
            {
                _resetEvent.Set();
            }
        }

        private static List<string> GenerateLabels(int count, string namingStyle, string startLabel)
        {
            var labels = new List<string>();
            if (namingStyle == "numeric")
            {
                int start = 1;
                if (!string.IsNullOrWhiteSpace(startLabel) && int.TryParse(startLabel.Trim(), out int sn)) start = sn;
                for (int i = 0; i < count; i++) labels.Add((start + i).ToString());
            }
            else
            {
                char start = 'A';
                if (!string.IsNullOrWhiteSpace(startLabel))
                {
                    char c = char.ToUpper(startLabel.Trim()[0]);
                    if (char.IsLetter(c)) start = c;
                }
                for (int i = 0; i < count; i++) labels.Add(AlphabeticLabel(start, i));
            }
            return labels;
        }

        private static string AlphabeticLabel(char startChar, int offset)
        {
            int idx = (startChar - 'A') + offset;
            string result = "";
            while (idx >= 0)
            {
                int mod = idx % 26;
                result = (char)('A' + mod) + result;
                idx = (idx / 26) - 1;
            }
            return result;
        }

        private static string GetUniqueName(string baseName, HashSet<string> existing)
        {
            string candidate = baseName;
            int counter = 1;
            while (existing.Contains(candidate))
            {
                candidate = baseName + counter;
                counter++;
            }
            return candidate;
        }

        public string GetName()
        {
            return "Create Grids from DWG Layer";
        }
    }
}

