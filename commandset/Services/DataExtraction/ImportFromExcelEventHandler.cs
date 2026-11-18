using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using ClosedXML.Excel;
using RevitMCPSDK.API.Interfaces;

namespace RevitMCPCommandSet.Services.DataExtraction
{
    public class ImportFromExcelEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        private readonly ManualResetEvent _resetEvent = new ManualResetEvent(false);

        public string FilePath { get; set; } = "";
        public string SheetName { get; set; } = "";
        public bool DryRun { get; set; } = false;
        public object Result { get; private set; }

        public void SetParameters(string filePath, string sheetName, bool dryRun)
        {
            FilePath = filePath;
            SheetName = sheetName;
            DryRun = dryRun;
            _resetEvent.Reset();
        }

        public bool WaitForCompletion(int timeoutMilliseconds = 60000)
        {
            return _resetEvent.WaitOne(timeoutMilliseconds);
        }

        public void Execute(UIApplication app)
        {
            try
            {
                var doc = app.ActiveUIDocument.Document;

                if (!File.Exists(FilePath))
                {
                    Result = new { success = false, error = $"File not found: {FilePath}" };
                    return;
                }

                using (var workbook = new XLWorkbook(FilePath))
                {
                    var worksheet = string.IsNullOrEmpty(SheetName)
                        ? workbook.Worksheets.First()
                        : workbook.Worksheets.Worksheet(SheetName);

                    // Read header row to find column mapping
                    var headers = new Dictionary<int, string>();
                    int lastCol = worksheet.LastColumnUsed()?.ColumnNumber() ?? 0;
                    for (int c = 1; c <= lastCol; c++)
                    {
                        string header = worksheet.Cell(1, c).GetString().Trim();
                        if (!string.IsNullOrEmpty(header))
                            headers[c] = header;
                    }

                    // Find ElementId column
                    int idCol = headers.FirstOrDefault(h =>
                        h.Value.Equals("ElementId", StringComparison.OrdinalIgnoreCase)).Key;

                    if (idCol == 0)
                    {
                        Result = new { success = false, error = "No 'ElementId' column found in the Excel file. Export with includeElementId=true first." };
                        return;
                    }

                    // Read data rows
                    int lastRow = worksheet.LastRowUsed()?.RowNumber() ?? 1;
                    int updated = 0;
                    int skipped = 0;
                    int failed = 0;
                    var errors = new List<string>();

                    using (var tx = DryRun ? null : new Transaction(doc, "Import from Excel"))
                    {
                        tx?.Start();

                        for (int r = 2; r <= lastRow; r++)
                        {
                            string idStr = worksheet.Cell(r, idCol).GetString().Trim();
                            if (string.IsNullOrEmpty(idStr)) { skipped++; continue; }

                            if (!long.TryParse(idStr, out long idVal)) { skipped++; continue; }

#if REVIT2024_OR_GREATER
                            var elemId = new ElementId(idVal);
#else
                            var elemId = new ElementId((int)idVal);
#endif
                            var elem = doc.GetElement(elemId);
                            if (elem == null) { skipped++; continue; }

                            bool anySet = false;
                            foreach (var kvp in headers)
                            {
                                if (kvp.Key == idCol) continue;
                                string paramName = kvp.Value;
                                if (paramName == "Category" || paramName == "Family" || paramName == "Type") continue;

                                string cellValue = worksheet.Cell(r, kvp.Key).GetString().Trim();
                                var param = elem.LookupParameter(paramName);
                                if (param == null || param.IsReadOnly) continue;

                                if (!DryRun)
                                {
                                    try
                                    {
                                        bool setOk = SetParameterValue(param, cellValue);
                                        if (setOk) anySet = true;
                                    }
                                    catch (Exception ex)
                                    {
                                        errors.Add($"Row {r}, param '{paramName}': {ex.Message}");
                                        failed++;
                                    }
                                }
                                else
                                {
                                    anySet = true; // dry run counts as success
                                }
                            }

                            if (anySet) updated++;
                        }

                        tx?.Commit();
                    }

                    Result = new
                    {
                        success = true,
                        dryRun = DryRun,
                        totalRows = lastRow - 1,
                        updated,
                        skipped,
                        failed,
                        errors = errors.Take(20).ToList(),
                        message = DryRun
                            ? $"Dry run: {updated} elements would be updated, {skipped} skipped"
                            : $"Updated {updated} elements, {skipped} skipped, {failed} errors"
                    };
                }
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

        private bool SetParameterValue(Parameter param, string value)
        {
            if (string.IsNullOrEmpty(value)) return false;

            switch (param.StorageType)
            {
                case StorageType.String:
                    param.Set(value);
                    return true;
                case StorageType.Integer:
                    if (int.TryParse(value, out int intVal)) { param.Set(intVal); return true; }
                    return false;
                case StorageType.Double:
                    if (double.TryParse(value, System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out double dblVal))
                    {
                        param.Set(dblVal);
                        return true;
                    }
                    return false;
                default:
                    return false;
            }
        }

        public string GetName() => "Import From Excel";
    }
}
