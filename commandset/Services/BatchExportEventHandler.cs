using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCPCommandSet.Models.Common;
using RevitMCPSDK.API.Interfaces;

namespace RevitMCPCommandSet.Services
{
    public class BatchExportEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        private readonly ManualResetEvent _resetEvent = new ManualResetEvent(false);

        public string Format { get; set; } = "PDF";
        public List<long> SheetIds { get; set; } = new List<long>();
        public List<long> ViewIds { get; set; } = new List<long>();
        public string ExportPath { get; set; } = "";
        public string PaperSize { get; set; } = "A4";
        public AIResult<object> Result { get; private set; }

        public bool WaitForCompletion(int timeoutMilliseconds = 10000)
        {
            _resetEvent.Reset();
            return _resetEvent.WaitOne(timeoutMilliseconds);
        }

        public void Execute(UIApplication app)
        {
            try
            {
                var doc = app.ActiveUIDocument.Document;

                string exportPath = string.IsNullOrEmpty(ExportPath)
                    ? System.IO.Path.Combine(System.IO.Path.GetTempPath(), "RevitExport_" + System.DateTime.Now.ToString("yyyyMMdd_HHmmss"))
                    : ExportPath;

                if (!System.IO.Directory.Exists(exportPath))
                    System.IO.Directory.CreateDirectory(exportPath);

                switch (Format.ToUpper())
                {
                    case "PDF":
                        ExportPdf(doc, exportPath);
                        break;
                    case "DWG":
                        ExportDwg(doc, exportPath);
                        break;
                    case "IFC":
                        ExportIfc(doc, exportPath);
                        break;
                    default:
                        throw new ArgumentException($"Unsupported format: {Format}");
                }
            }
            catch (Exception ex)
            {
                Result = new AIResult<object>
                {
                    Success = false,
                    Message = $"Batch export failed: {ex.Message}"
                };
            }
            finally
            {
                _resetEvent.Set();
            }
        }

        private void ExportPdf(Document doc, string exportPath)
        {
            var viewIds = GetViewOrSheetIds(doc);
            if (viewIds.Count == 0)
                throw new Exception("No sheets/views to export");

            var options = new PDFExportOptions();
            options.FileName = "Export";
            options.Combine = false;

            switch (PaperSize)
            {
                case "A3": options.PaperFormat = ExportPaperFormat.ISO_A3; break;
                case "A2": options.PaperFormat = ExportPaperFormat.ISO_A2; break;
                case "A1": options.PaperFormat = ExportPaperFormat.ISO_A1; break;
                case "A0": options.PaperFormat = ExportPaperFormat.ISO_A0; break;
                case "Letter": options.PaperFormat = ExportPaperFormat.ANSI_A; break;
                case "Tabloid": options.PaperFormat = ExportPaperFormat.ANSI_B; break;
                default: options.PaperFormat = ExportPaperFormat.ISO_A4; break;
            }

            doc.Export(exportPath, viewIds, options);
            Result = BuildExportResult("PDF", "*.pdf", exportPath);
        }

        private void ExportDwg(Document doc, string exportPath)
        {
            var viewIds = GetViewOrSheetIds(doc);
            if (viewIds.Count == 0)
                throw new Exception("No sheets/views to export");

            var options = new DWGExportOptions();
            options.MergedViews = true;
            options.FileVersion = ACADVersion.R2018;

            doc.Export(exportPath, "Export", viewIds, options);
            Result = BuildExportResult("DWG", "*.dwg", exportPath);
        }

        private void ExportIfc(Document doc, string exportPath)
        {
            var options = new IFCExportOptions();

            using (var transaction = new Transaction(doc, "Export IFC"))
            {
                transaction.Start();
                doc.Export(exportPath, doc.Title + ".ifc", options);
                transaction.Commit();
            }

            Result = BuildExportResult("IFC", "*.ifc", exportPath);
        }

        private static AIResult<object> BuildExportResult(string format, string searchPattern, string exportPath)
        {
            var exportedFiles = System.IO.Directory.GetFiles(exportPath, searchPattern);
            return new AIResult<object>
            {
                Success = true,
                Message = $"Exported {exportedFiles.Length} {format} file(s) to '{exportPath}'",
                Response = new
                {
                    format,
                    exportPath,
                    fileCount = exportedFiles.Length,
                    files = exportedFiles.Select(f => System.IO.Path.GetFileName(f)).ToList()
                }
            };
        }

        private IList<ElementId> GetViewOrSheetIds(Document doc)
        {
            var ids = new List<ElementId>();

            if (SheetIds.Count > 0)
            {
                foreach (var id in SheetIds)
                    ids.Add(ToElementId(id));
                return ids;
            }

            if (ViewIds.Count > 0)
            {
                foreach (var id in ViewIds)
                    ids.Add(ToElementId(id));
                return ids;
            }

            // Export all sheets if none specified
            var allSheets = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSheet))
                .Cast<ViewSheet>()
                .Where(s => !s.IsPlaceholder)
                .ToList();

            foreach (var sheet in allSheets)
                ids.Add(sheet.Id);

            return ids;
        }

        private ElementId ToElementId(long id)
        {
#if REVIT2024_OR_GREATER
            return new ElementId(id);
#else
            return new ElementId((int)id);
#endif
        }

        public string GetName() => "Batch Export";
    }
}
