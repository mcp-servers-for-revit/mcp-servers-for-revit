using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using ClosedXML.Excel;
using RevitMCPSDK.API.Interfaces;

namespace RevitMCPCommandSet.Services.Workflow
{
    public class WorkflowDataRoundtripEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        private readonly ManualResetEvent _resetEvent = new ManualResetEvent(false);

        // Input parameters
        public List<string> Categories { get; set; } = new();
        public List<string> ParameterNames { get; set; } = new();
        public bool IncludeTypeParameters { get; set; }
        public string FilePath { get; set; } = "";

        // Output
        public object Result { get; private set; }

        public void SetParameters(List<string> categories, List<string> parameterNames,
            bool includeTypeParameters, string filePath)
        {
            Categories = categories;
            ParameterNames = parameterNames;
            IncludeTypeParameters = includeTypeParameters;
            FilePath = filePath;
            _resetEvent.Reset();
        }

        public bool WaitForCompletion(int timeoutMilliseconds = 120000)
        {
            return _resetEvent.WaitOne(timeoutMilliseconds);
        }

        public void Execute(UIApplication app)
        {
            try
            {
                var doc = app.ActiveUIDocument.Document;

                // Resolve output path
                if (string.IsNullOrEmpty(FilePath))
                {
                    string desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                    FilePath = Path.Combine(desktop, $"RevitRoundtrip_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx");
                }

                // Collect elements by category
                var elements = new List<Element>();
                if (Categories.Count > 0)
                {
                    foreach (var cat in Categories)
                    {
                        var builtInCat = GetBuiltInCategory(doc, cat);
                        if (builtInCat != BuiltInCategory.INVALID)
                        {
                            var catElements = new FilteredElementCollector(doc)
                                .OfCategory(builtInCat)
                                .WhereElementIsNotElementType()
                                .Take(10000)
                                .ToList();
                            elements.AddRange(catElements);
                        }
                    }
                }
                else
                {
                    elements = new FilteredElementCollector(doc)
                        .WhereElementIsNotElementType()
                        .Take(10000)
                        .ToList();
                }

                if (elements.Count == 0)
                {
                    Result = new
                    {
                        success = true,
                        filePath = FilePath,
                        elementCount = 0,
                        parameterCount = 0,
                        instructions = "No elements found for the specified categories.",
                        message = "No elements found"
                    };
                    return;
                }

                // Discover parameters
                var paramInfos = DiscoverParameters(doc, elements);

                // Create Excel workbook
                using (var workbook = new XLWorkbook())
                {
                    var worksheet = workbook.Worksheets.Add("Roundtrip");

                    // Write header row
                    int col = 1;
                    worksheet.Cell(1, col).Value = "ElementId";
                    col++;
                    worksheet.Cell(1, col).Value = "Category";
                    col++;
                    worksheet.Cell(1, col).Value = "Family";
                    col++;
                    worksheet.Cell(1, col).Value = "Type";
                    col++;

                    var paramColumns = new List<(string Name, bool IsType, bool IsReadOnly, int Column)>();
                    foreach (var pi in paramInfos)
                    {
                        worksheet.Cell(1, col).Value = pi.Name;

                        var headerCell = worksheet.Cell(1, col);
                        if (pi.IsReadOnly)
                            headerCell.Style.Fill.BackgroundColor = XLColor.FromHtml("#FFCCCC"); // red = read-only
                        else if (pi.IsType)
                            headerCell.Style.Fill.BackgroundColor = XLColor.FromHtml("#FFFFCC"); // yellow = type
                        else
                            headerCell.Style.Fill.BackgroundColor = XLColor.FromHtml("#CCFFCC"); // green = instance (editable)

                        paramColumns.Add((pi.Name, pi.IsType, pi.IsReadOnly, col));
                        col++;
                    }

                    // Style header
                    var headerRange = worksheet.Range(1, 1, 1, col - 1);
                    headerRange.Style.Font.Bold = true;
                    headerRange.Style.Border.BottomBorder = XLBorderStyleValues.Thin;

                    // Write data rows
                    int row = 2;
                    foreach (var elem in elements)
                    {
                        col = 1;
#if REVIT2024_OR_GREATER
                        worksheet.Cell(row, col).Value = elem.Id.Value;
#else
                        worksheet.Cell(row, col).Value = elem.Id.IntegerValue;
#endif
                        col++;
                        worksheet.Cell(row, col).Value = elem.Category?.Name ?? "";
                        col++;
                        worksheet.Cell(row, col).Value = (elem as FamilyInstance)?.Symbol?.Family?.Name ?? elem.GetType().Name;
                        col++;
                        worksheet.Cell(row, col).Value = elem.Name;
                        col++;

                        foreach (var pc in paramColumns)
                        {
                            string val = "";
                            if (pc.IsType)
                            {
                                var typeId = elem.GetTypeId();
                                if (typeId != ElementId.InvalidElementId)
                                {
                                    var typeElem = doc.GetElement(typeId);
                                    var p = typeElem?.LookupParameter(pc.Name);
                                    if (p != null && p.HasValue)
                                        val = p.AsValueString() ?? p.AsString() ?? "";
                                }
                            }
                            else
                            {
                                var p = elem.LookupParameter(pc.Name);
                                if (p != null && p.HasValue)
                                    val = p.AsValueString() ?? p.AsString() ?? "";
                            }
                            worksheet.Cell(row, pc.Column).Value = val;
                        }
                        row++;
                    }

                    // Auto-fit columns
                    worksheet.Columns().AdjustToContents(1, 50);

                    // Save
                    Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                    workbook.SaveAs(FilePath);
                }

                string instructions =
                    $"Excel file exported to: {FilePath}\n" +
                    $"Elements: {elements.Count} | Parameters: {paramInfos.Count}\n\n" +
                    "ROUNDTRIP INSTRUCTIONS:\n" +
                    "1. Open the Excel file and edit the GREEN columns (editable instance parameters)\n" +
                    "2. YELLOW columns are type parameters — changes affect all instances of that type\n" +
                    "3. RED columns are read-only — do not edit\n" +
                    "4. Do NOT change the ElementId column — it is used to match rows back to Revit elements\n" +
                    "5. When done editing, ask me to import the file back using sync_csv_parameters or import_table";

                Result = new
                {
                    success = true,
                    filePath = FilePath,
                    elementCount = elements.Count,
                    parameterCount = paramInfos.Count,
                    instructions
                };
            }
            catch (Exception ex)
            {
                Result = new { success = false, error = ex.Message };
            }
            finally
            {
                _resetEvent.Set();
            }
        }

        private List<ParamInfo> DiscoverParameters(Document doc, List<Element> elements)
        {
            var result = new Dictionary<string, ParamInfo>();
            var sample = elements.Take(50).ToList();

            foreach (var elem in sample)
            {
                // Instance parameters
                foreach (Parameter p in elem.Parameters)
                {
                    if (p.Definition == null) continue;
                    string name = p.Definition.Name;
                    if (ParameterNames.Count > 0 && !ParameterNames.Contains(name)) continue;
                    if (!result.ContainsKey(name))
                        result[name] = new ParamInfo { Name = name, IsType = false, IsReadOnly = p.IsReadOnly };
                }

                // Type parameters
                if (IncludeTypeParameters)
                {
                    var typeId = elem.GetTypeId();
                    if (typeId != ElementId.InvalidElementId)
                    {
                        var typeElem = doc.GetElement(typeId);
                        if (typeElem != null)
                        {
                            foreach (Parameter p in typeElem.Parameters)
                            {
                                if (p.Definition == null) continue;
                                string name = p.Definition.Name;
                                if (ParameterNames.Count > 0 && !ParameterNames.Contains(name)) continue;
                                string key = $"TYPE:{name}";
                                if (!result.ContainsKey(key))
                                    result[key] = new ParamInfo { Name = name, IsType = true, IsReadOnly = p.IsReadOnly };
                            }
                        }
                    }
                }
            }

            return result.Values.OrderBy(p => p.IsType).ThenBy(p => p.Name).ToList();
        }

        private BuiltInCategory GetBuiltInCategory(Document doc, string categoryName)
        {
            foreach (Category cat in doc.Settings.Categories)
            {
                if (cat.Name.Equals(categoryName, StringComparison.OrdinalIgnoreCase))
#if REVIT2024_OR_GREATER
                    return (BuiltInCategory)cat.Id.Value;
#else
                    return (BuiltInCategory)cat.Id.IntegerValue;
#endif
            }
            return BuiltInCategory.INVALID;
        }

        public string GetName() => "Workflow Data Roundtrip";

        private class ParamInfo
        {
            public string Name { get; set; }
            public bool IsType { get; set; }
            public bool IsReadOnly { get; set; }
        }
    }
}
