using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Services.DataExtraction;
using RevitMCPSDK.API.Base;
using System;
using System.Collections.Generic;

namespace RevitMCPCommandSet.Commands.DataExtraction
{
    public class BulkModifyParameterValuesCommand : ExternalEventCommandBase
    {
        private static readonly object _executionLock = new object();
        private BulkModifyParameterValuesEventHandler _handler => (BulkModifyParameterValuesEventHandler)Handler;

        public override string CommandName => "bulk_modify_parameter_values";

        public BulkModifyParameterValuesCommand(UIApplication uiApp)
            : base(new BulkModifyParameterValuesEventHandler(), uiApp) { }

        public override object Execute(JObject parameters, string requestId)
        {
            lock (_executionLock)
            {
                try
                {
                    _handler.ElementIds = parameters?["elementIds"]?.ToObject<List<long>>() ?? new List<long>();
                    _handler.CategoryName = parameters?["categoryName"]?.Value<string>() ?? "";
                    _handler.ParameterName = parameters?["parameterName"]?.Value<string>() ?? "";
                    _handler.Operation = parameters?["operation"]?.Value<string>() ?? "set";
                    _handler.Value = parameters?["value"]?.Value<string>() ?? "";
                    _handler.FindText = parameters?["findText"]?.Value<string>() ?? "";
                    _handler.ReplaceText = parameters?["replaceText"]?.Value<string>() ?? "";
                    _handler.OnlyEmpty = parameters?["onlyEmpty"]?.Value<bool>() ?? false;
                    _handler.DryRun = parameters?["dryRun"]?.Value<bool>() ?? false;

                    _handler.SetParameters();

                    if (RaiseAndWaitForCompletion(120000)) // 2-minute timeout for large model scans
                        return _handler.Result;

                    throw new TimeoutException("Bulk modify parameter values timed out — try narrowing scope with elementIds or categoryName");
                }
                catch (Exception ex)
                {
                    throw new Exception($"Bulk modify parameter values failed: {ex.Message}");
                }
            }
        }
    }
}
