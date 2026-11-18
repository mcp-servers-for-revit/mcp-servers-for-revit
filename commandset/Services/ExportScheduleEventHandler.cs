using System.IO;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCPCommandSet.Models.Common;
using RevitMCPSDK.API.Interfaces;

namespace RevitMCPCommandSet.Services
{
    public class ExportScheduleEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        private readonly ManualResetEvent _resetEvent = new ManualResetEvent(false);

        public long ScheduleId { get; set; }
        public string ExportPath { get; set; }
        public string Delimiter { get; set; }
        public bool IncludeHeaders { get; set; } = true;
        public AIResult<object> Result { get; private set; }

        public void SetParameters(long scheduleId, string exportPath, string delimiter, bool includeHeaders)
        {
            ScheduleId = scheduleId;
            ExportPath = exportPath;
            Delimiter = delimiter;
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

#if REVIT2024_OR_GREATER
                var elementId = new ElementId(ScheduleId);
#else
                var elementId = new ElementId((int)ScheduleId);
#endif

                var schedule = doc.GetElement(elementId) as ViewSchedule;
                if (schedule == null)
                {
                    Result = new AIResult<object>
                    {
                        Success = false,
                        Message = $"No schedule found with ID {ScheduleId}"
                    };
                    return;
                }

                var exportPath = ExportPath;
                if (string.IsNullOrEmpty(exportPath))
                    exportPath = Path.Combine(Path.GetTempPath(), $"schedule_{ScheduleId}.txt");

                string directory = Path.GetDirectoryName(exportPath);
                string filename = Path.GetFileName(exportPath);

                if (!Directory.Exists(directory))
                    Directory.CreateDirectory(directory);

                // Read schedule data manually using TableSectionData
                var tableData = schedule.GetTableData();
                var bodyData = tableData.GetSectionData(SectionType.Body);
                var headerData = tableData.GetSectionData(SectionType.Header);

                string delimChar = "\t";
                switch (Delimiter?.ToLower())
                {
                    case "comma": delimChar = ","; break;
                    case "space": delimChar = " "; break;
                    case "semicolon": delimChar = ";"; break;
                }

                var lines = new List<string>();

                // Add column headers
                if (IncludeHeaders)
                {
                    var definition = schedule.Definition;
                    var headers = new List<string>();
                    for (int col = 0; col < definition.GetFieldCount(); col++)
                    {
                        var field = definition.GetField(col);
                        if (!field.IsHidden)
                            headers.Add(field.ColumnHeading);
                    }
                    lines.Add(string.Join(delimChar, headers));
                }

                // Add body rows
                int rowCount = bodyData.NumberOfRows;
                int colCount = bodyData.NumberOfColumns;
                for (int row = bodyData.FirstRowNumber; row < bodyData.FirstRowNumber + rowCount; row++)
                {
                    var cells = new List<string>();
                    for (int col = bodyData.FirstColumnNumber; col < bodyData.FirstColumnNumber + colCount; col++)
                    {
                        try
                        {
                            cells.Add(bodyData.GetCellText(row, col));
                        }
                        catch
                        {
                            cells.Add("");
                        }
                    }
                    lines.Add(string.Join(delimChar, cells));
                }

                File.WriteAllLines(exportPath, lines);

                Result = new AIResult<object>
                {
                    Success = true,
                    Message = $"Successfully exported schedule '{schedule.Name}' ({lines.Count} lines) to '{exportPath}'",
                    Response = new
                    {
                        exportPath = exportPath,
                        scheduleName = schedule.Name,
                        rowCount = rowCount,
                        lineCount = lines.Count
                    }
                };
            }
            catch (Exception ex)
            {
                Result = new AIResult<object>
                {
                    Success = false,
                    Message = $"Failed to export schedule: {ex.Message}"
                };
            }
            finally
            {
                _resetEvent.Set();
            }
        }

        public string GetName() => "Export Schedule";
    }
}
