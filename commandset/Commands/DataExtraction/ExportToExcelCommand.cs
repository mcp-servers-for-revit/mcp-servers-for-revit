using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Services.DataExtraction;
using RevitMCPSDK.API.Base;

namespace RevitMCPCommandSet.Commands.DataExtraction
{
    public class ExportToExcelCommand : ExternalEventCommandBase
    {
        private ExportToExcelEventHandler _handler => (ExportToExcelEventHandler)Handler;
        public override string CommandName => "export_to_excel";

        public ExportToExcelCommand(UIApplication uiApp)
            : base(new ExportToExcelEventHandler(), uiApp) { }

        public override object Execute(JObject parameters, string requestId)
        {
            try
            {
                var categories = (parameters?["categories"] as JArray)?
                    .Select(t => t.ToString()).Where(s => !string.IsNullOrEmpty(s)).ToList()
                    ?? new List<string>();
                var parameterNames = (parameters?["parameterNames"] as JArray)?
                    .Select(t => t.ToString()).Where(s => !string.IsNullOrEmpty(s)).ToList()
                    ?? new List<string>();

                _handler.SetParameters(
                    categories,
                    parameterNames,
                    includeTypeParameters: parameters?["includeTypeParameters"]?.Value<bool>() ?? false,
                    includeElementId: parameters?["includeElementId"]?.Value<bool>() ?? true,
                    filePath: parameters?["filePath"]?.ToString() ?? "",
                    sheetName: parameters?["sheetName"]?.ToString() ?? "Export",
                    colorCodeColumns: parameters?["colorCodeColumns"]?.Value<bool>() ?? true,
                    maxElements: parameters?["maxElements"]?.Value<int>() ?? 10000
                );

                if (RaiseAndWaitForCompletion(120000))
                    return _handler.Result;

                throw new TimeoutException("Export to Excel timed out");
            }
            catch (Exception ex)
            {
                throw new Exception($"Export to Excel failed: {ex.Message}");
            }
        }
    }
}
