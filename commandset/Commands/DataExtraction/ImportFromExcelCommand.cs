using System;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Services.DataExtraction;
using RevitMCPSDK.API.Base;

namespace RevitMCPCommandSet.Commands.DataExtraction
{
    public class ImportFromExcelCommand : ExternalEventCommandBase
    {
        private ImportFromExcelEventHandler _handler => (ImportFromExcelEventHandler)Handler;
        public override string CommandName => "import_from_excel";

        public ImportFromExcelCommand(UIApplication uiApp)
            : base(new ImportFromExcelEventHandler(), uiApp) { }

        public override object Execute(JObject parameters, string requestId)
        {
            try
            {
                _handler.SetParameters(
                    filePath: parameters?["filePath"]?.ToString() ?? "",
                    sheetName: parameters?["sheetName"]?.ToString() ?? "",
                    dryRun: parameters?["dryRun"]?.Value<bool>() ?? false
                );

                if (RaiseAndWaitForCompletion(120000))
                    return _handler.Result;

                throw new TimeoutException("Import from Excel timed out");
            }
            catch (Exception ex)
            {
                throw new Exception($"Import from Excel failed: {ex.Message}");
            }
        }
    }
}
