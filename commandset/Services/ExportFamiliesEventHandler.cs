using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCPCommandSet.Models.Common;
using RevitMCPSDK.API.Interfaces;

namespace RevitMCPCommandSet.Services
{
    public class ExportFamiliesEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        private readonly ManualResetEvent _resetEvent = new ManualResetEvent(false);

        public string OutputDirectory { get; set; } = "";
        public List<string> Categories { get; set; } = new List<string>();
        public bool GroupByCategory { get; set; } = true;
        public bool Overwrite { get; set; } = false;
        public AIResult<object> Result { get; private set; }

        public void SetParameters(string outputDirectory, List<string> categories, bool groupByCategory, bool overwrite)
        {
            OutputDirectory = outputDirectory ?? "";
            Categories = categories ?? new List<string>();
            GroupByCategory = groupByCategory;
            Overwrite = overwrite;
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

                if (string.IsNullOrEmpty(OutputDirectory))
                    throw new ArgumentException("outputDirectory is required");

                if (!System.IO.Directory.Exists(OutputDirectory))
                    System.IO.Directory.CreateDirectory(OutputDirectory);

                // Collect all Family elements from the document
                var allFamilies = new FilteredElementCollector(doc)
                    .OfClass(typeof(Family))
                    .Cast<Family>()
                    .ToList();

                // Filter by categories if provided
                if (Categories != null && Categories.Count > 0)
                {
                    var categorySet = new HashSet<string>(Categories, StringComparer.OrdinalIgnoreCase);
                    allFamilies = allFamilies
                        .Where(f => f.FamilyCategory != null && categorySet.Contains(f.FamilyCategory.Name))
                        .ToList();
                }

                // Filter only editable families (system families cannot be exported)
                var editableFamilies = allFamilies.Where(f => f.IsEditable).ToList();

                var results = new List<object>();
                int exportedCount = 0;
                int skippedCount = 0;
                int errorCount = 0;

                foreach (var family in editableFamilies)
                {
                    string familyName = family.Name;
                    string categoryName = family.FamilyCategory?.Name ?? "Uncategorized";
                    string exportPath = "";

                    try
                    {
                        // Build output path
                        string targetDir = GroupByCategory
                            ? System.IO.Path.Combine(OutputDirectory, SanitizeFileName(categoryName))
                            : OutputDirectory;

                        if (!System.IO.Directory.Exists(targetDir))
                            System.IO.Directory.CreateDirectory(targetDir);

                        exportPath = System.IO.Path.Combine(targetDir, SanitizeFileName(familyName) + ".rfa");

                        // Skip if file exists and overwrite is false
                        if (!Overwrite && System.IO.File.Exists(exportPath))
                        {
                            results.Add(new
                            {
                                familyName,
                                category = categoryName,
                                exportPath,
                                status = "skipped"
                            });
                            skippedCount++;
                            continue;
                        }

                        // Open family document (no Transaction required)
                        Document familyDoc = doc.EditFamily(family);
                        if (familyDoc == null)
                        {
                            results.Add(new
                            {
                                familyName,
                                category = categoryName,
                                exportPath,
                                status = "error",
                                message = "Could not open family for editing"
                            });
                            errorCount++;
                            continue;
                        }

                        try
                        {
                            // SaveAs to export the family
                            var saveOptions = new SaveAsOptions { OverwriteExistingFile = true };
                            familyDoc.SaveAs(exportPath, saveOptions);
                            exportedCount++;

                            results.Add(new
                            {
                                familyName,
                                category = categoryName,
                                exportPath,
                                status = "exported"
                            });
                        }
                        finally
                        {
                            // Close family document without saving
                            familyDoc.Close(false);
                        }
                    }
                    catch (Exception ex)
                    {
                        results.Add(new
                        {
                            familyName,
                            category = categoryName,
                            exportPath,
                            status = "error",
                            message = ex.Message
                        });
                        errorCount++;
                    }
                }

                Result = new AIResult<object>
                {
                    Success = true,
                    Message = $"Export complete: {exportedCount} exported, {skippedCount} skipped, {errorCount} errors (out of {editableFamilies.Count} editable families, {allFamilies.Count - editableFamilies.Count} system families excluded)",
                    Response = new
                    {
                        outputDirectory = OutputDirectory,
                        groupByCategory = GroupByCategory,
                        totalFamilies = allFamilies.Count,
                        editableFamilies = editableFamilies.Count,
                        exported = exportedCount,
                        skipped = skippedCount,
                        errors = errorCount,
                        families = results
                    }
                };
            }
            catch (Exception ex)
            {
                Result = new AIResult<object>
                {
                    Success = false,
                    Message = $"Export families failed: {ex.Message}"
                };
            }
            finally
            {
                _resetEvent.Set();
            }
        }

        private static string SanitizeFileName(string name)
        {
            var invalid = System.IO.Path.GetInvalidFileNameChars();
            foreach (var c in invalid)
            {
                name = name.Replace(c, '_');
            }
            return name;
        }

        public string GetName() => "Export Families";
    }
}
