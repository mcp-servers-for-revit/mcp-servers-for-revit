using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Services.DataExtraction;
using RevitMCPSDK.API.Base;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RevitMCPCommandSet.Commands.DataExtraction
{
    public class ExportElementsDataCommand : ExternalEventCommandBase
    {
        private ExportElementsDataEventHandler _handler => (ExportElementsDataEventHandler)Handler;

        public override string CommandName => "export_elements_data";

        public ExportElementsDataCommand(UIApplication uiApp)
            : base(new ExportElementsDataEventHandler(), uiApp)
        {
        }

        public override object Execute(JObject parameters, string requestId)
        {
            try
            {
                // Categories: optional string[]
                var categoriesToken = parameters?["categories"] as JArray;
                var categories = categoriesToken != null
                    ? categoriesToken.Select(t => t.ToString()).Where(s => !string.IsNullOrEmpty(s)).ToList()
                    : new List<string>();

                // ParameterNames: optional string[]
                var paramNamesToken = parameters?["parameterNames"] as JArray;
                var parameterNames = paramNamesToken != null
                    ? paramNamesToken.Select(t => t.ToString()).Where(s => !string.IsNullOrEmpty(s)).ToList()
                    : new List<string>();

                bool includeTypeParameters = parameters?["includeTypeParameters"]?.Value<bool>() ?? false;
                bool includeElementId = parameters?["includeElementId"]?.Value<bool>() ?? true;
                string outputFormat = parameters?["outputFormat"]?.ToString() ?? "json";
                int maxElements = parameters?["maxElements"]?.Value<int>() ?? 100;
                string filterParameterName = parameters?["filterParameterName"]?.ToString() ?? "";
                string filterValue = parameters?["filterValue"]?.ToString() ?? "";
                string filterOperator = parameters?["filterOperator"]?.ToString() ?? "equals";

                _handler.SetParameters(
                    categories,
                    parameterNames,
                    includeTypeParameters,
                    includeElementId,
                    outputFormat,
                    maxElements,
                    filterParameterName,
                    filterValue,
                    filterOperator);

                if (RaiseAndWaitForCompletion(60000))
                    return _handler.Result;

                throw new TimeoutException("Export elements data timed out");
            }
            catch (Exception ex)
            {
                throw new Exception($"Export elements data failed: {ex.Message}");
            }
        }
    }
}
