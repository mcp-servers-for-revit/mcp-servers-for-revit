using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCPCommandSet.Models.Common;
using RevitMCPSDK.API.Interfaces;
using System.IO;

namespace RevitMCPCommandSet.Services
{
    public class ImportTableEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        private readonly ManualResetEvent _resetEvent = new ManualResetEvent(false);

        public string FilePath { get; private set; }
        public string Delimiter { get; private set; }
        public string ViewType { get; private set; }
        public string ViewName { get; private set; }
        public int Scale { get; private set; }
        public double TextSize { get; private set; }
        public bool IncludeHeaders { get; private set; }
        public AIResult<object> Result { get; private set; }

        public void SetParameters(string filePath, string delimiter, string viewType, string viewName, int scale, double textSize, bool includeHeaders)
        {
            FilePath = filePath;
            Delimiter = delimiter;
            ViewType = viewType;
            ViewName = viewName;
            Scale = scale;
            TextSize = textSize;
            IncludeHeaders = includeHeaders;
            _resetEvent.Reset();
        }

        public bool WaitForCompletion(int timeoutMilliseconds = 10000)
        {
            return _resetEvent.WaitOne(timeoutMilliseconds);
        }

        public void Execute(UIApplication app)
        {
            try
            {
                var doc = app.ActiveUIDocument.Document;

                // Read and parse the file
                var lines = File.ReadAllLines(FilePath);
                if (lines.Length == 0)
                    throw new ArgumentException("File is empty");

                // Resolve the actual delimiter character
                var delimChar = Delimiter == "\\t" ? '\t' : Delimiter[0];

                // Parse rows
                var rows = new List<string[]>();
                foreach (var line in lines)
                {
                    if (!string.IsNullOrWhiteSpace(line))
                        rows.Add(line.Split(delimChar));
                }

                if (rows.Count == 0)
                    throw new ArgumentException("No data rows found in file");

                int columnCount = rows.Max(r => r.Length);

                // Normalize all rows to same column count
                for (int i = 0; i < rows.Count; i++)
                {
                    if (rows[i].Length < columnCount)
                    {
                        var padded = new string[columnCount];
                        Array.Copy(rows[i], padded, rows[i].Length);
                        for (int j = rows[i].Length; j < columnCount; j++)
                            padded[j] = "";
                        rows[i] = padded;
                    }
                }

                using (var transaction = new Transaction(doc, "Import Table"))
                {
                    transaction.Start();
                    try
                    {
                        // Create the target view
                        View targetView = CreateTargetView(doc);

                        // Set view scale
                        if (Scale > 0)
                            targetView.Scale = Scale;

                        // Convert text size from mm to feet (Revit internal units)
                        double textSizeFeet = TextSize / 304.8;

                        // Calculate column widths based on max text length per column
                        double charWidthFeet = textSizeFeet * 0.6; // approximate character width
                        double cellPaddingFeet = textSizeFeet * 1.5; // padding
                        double rowHeightFeet = textSizeFeet * 2.5; // row height

                        var colWidths = new double[columnCount];
                        for (int col = 0; col < columnCount; col++)
                        {
                            int maxLen = 1;
                            foreach (var row in rows)
                            {
                                if (col < row.Length && row[col] != null)
                                {
                                    int len = row[col].Trim().Length;
                                    if (len > maxLen) maxLen = len;
                                }
                            }
                            colWidths[col] = maxLen * charWidthFeet + cellPaddingFeet;
                        }

                        // Find or create text note types
                        var defaultTextTypeId = GetDefaultTextNoteTypeId(doc);
                        var boldTextTypeId = GetBoldTextNoteTypeId(doc, defaultTextTypeId);

                        // Create text notes for each cell
                        int textNotesCreated = 0;
                        for (int row = 0; row < rows.Count; row++)
                        {
                            double xOffset = 0;
                            for (int col = 0; col < columnCount; col++)
                            {
                                string cellText = (col < rows[row].Length) ? rows[row][col].Trim() : "";
                                if (string.IsNullOrEmpty(cellText))
                                {
                                    xOffset += colWidths[col];
                                    continue;
                                }

                                // Position: left-to-right, top-to-bottom
                                double x = xOffset;
                                double y = -row * rowHeightFeet;
                                XYZ position = new XYZ(x, y, 0);

                                // Use bold for header row
                                bool isHeader = IncludeHeaders && row == 0;
                                var typeId = isHeader && boldTextTypeId != ElementId.InvalidElementId
                                    ? boldTextTypeId
                                    : defaultTextTypeId;

                                var options = new TextNoteOptions(typeId)
                                {
                                    HorizontalAlignment = HorizontalTextAlignment.Left
                                };
                                TextNote.Create(doc, targetView.Id, position, cellText, options);
                                textNotesCreated++;
                                xOffset += colWidths[col];
                            }
                        }

                        transaction.Commit();

                        Result = new AIResult<object>
                        {
                            Success = true,
                            Message = $"Successfully imported table with {rows.Count} rows and {columnCount} columns into '{targetView.Name}'",
                            Response = new
                            {
#if REVIT2024_OR_GREATER
                                viewId = targetView.Id.Value,
#else
                                viewId = targetView.Id.IntegerValue,
#endif
                                viewName = targetView.Name,
                                rowCount = rows.Count,
                                columnCount = columnCount,
                                textNotesCreated = textNotesCreated
                            }
                        };
                    }
                    catch
                    {
                        if (transaction.GetStatus() == TransactionStatus.Started)
                            transaction.RollBack();
                        throw;
                    }
                }
            }
            catch (Exception ex)
            {
                Result = new AIResult<object>
                {
                    Success = false,
                    Message = $"Failed to import table: {ex.Message}"
                };
            }
            finally
            {
                _resetEvent.Set();
            }
        }

        private View CreateTargetView(Document doc)
        {
            if (ViewType?.ToLower() == "legend")
            {
                // Find ViewFamilyType for legend
                var legendType = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewFamilyType))
                    .Cast<ViewFamilyType>()
                    .FirstOrDefault(vft => vft.ViewFamily == ViewFamily.Legend);

                if (legendType == null)
                    throw new InvalidOperationException("No Legend ViewFamilyType found in the project");

                var legendView = ViewDrafting.Create(doc, legendType.Id);
                legendView.Name = !string.IsNullOrEmpty(ViewName)
                    ? ViewName
                    : $"Imported Table - {Path.GetFileNameWithoutExtension(FilePath)}";
                return legendView;
            }
            else
            {
                // Find ViewFamilyType for drafting
                var draftingType = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewFamilyType))
                    .Cast<ViewFamilyType>()
                    .FirstOrDefault(vft => vft.ViewFamily == ViewFamily.Drafting);

                if (draftingType == null)
                    throw new InvalidOperationException("No Drafting ViewFamilyType found in the project");

                var draftingView = ViewDrafting.Create(doc, draftingType.Id);
                draftingView.Name = !string.IsNullOrEmpty(ViewName)
                    ? ViewName
                    : $"Imported Table - {Path.GetFileNameWithoutExtension(FilePath)}";
                return draftingView;
            }
        }

        private ElementId GetDefaultTextNoteTypeId(Document doc)
        {
            var textNoteType = new FilteredElementCollector(doc)
                .OfClass(typeof(TextNoteType))
                .Cast<TextNoteType>()
                .FirstOrDefault();

            if (textNoteType == null)
                throw new InvalidOperationException("No TextNoteType found in the project");

            return textNoteType.Id;
        }

        private ElementId GetBoldTextNoteTypeId(Document doc, ElementId defaultTypeId)
        {
            // Try to find an existing bold text type
            var boldType = new FilteredElementCollector(doc)
                .OfClass(typeof(TextNoteType))
                .Cast<TextNoteType>()
                .FirstOrDefault(t =>
                {
                    var name = t.Name.ToLower();
                    return name.Contains("bold") || name.Contains("header");
                });

            if (boldType != null)
                return boldType.Id;

            // If no bold type found, try to duplicate the default and make it bold
            try
            {
                var defaultType = doc.GetElement(defaultTypeId) as TextNoteType;
                if (defaultType == null)
                    return defaultTypeId;

                var duplicated = defaultType.Duplicate("Table Header Bold") as TextNoteType;
                if (duplicated != null)
                {
                    // Set bold parameter
                    var boldParam = duplicated.get_Parameter(BuiltInParameter.TEXT_STYLE_BOLD);
                    if (boldParam != null && !boldParam.IsReadOnly)
                        boldParam.Set(1);

                    return duplicated.Id;
                }
            }
            catch
            {
                // If duplication fails, just use the default type
            }

            return defaultTypeId;
        }

        public string GetName() => "Import Table";
    }
}
