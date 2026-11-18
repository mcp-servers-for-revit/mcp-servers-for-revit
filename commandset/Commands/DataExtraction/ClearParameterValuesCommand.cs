using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Services.DataExtraction;
using RevitMCPSDK.API.Base;
using System;
using System.Collections.Generic;

namespace RevitMCPCommandSet.Commands.DataExtraction
{
    public class ClearParameterValuesCommand : ExternalEventCommandBase
    {
        private static readonly object _executionLock = new object();
        private ClearParameterValuesEventHandler _handler => (ClearParameterValuesEventHandler)Handler;

        public override string CommandName => "clear_parameter_values";

        public ClearParameterValuesCommand(UIApplication uiApp)
            : base(new ClearParameterValuesEventHandler(), uiApp) { }

        public override object Execute(JObject parameters, string requestId)
        {
            lock (_executionLock)
            {
                try
                {
                    _handler.ParameterName = parameters?["parameterName"]?.Value<string>() ?? "";
                    _handler.Categories = parameters?["categories"]?.ToObject<List<string>>() ?? new List<string>();
                    _handler.Scope = parameters?["scope"]?.Value<string>() ?? "whole_model";
                    _handler.FilterValue = parameters?["filterValue"]?.Value<string>() ?? "";
                    _handler.ParameterType = parameters?["parameterType"]?.Value<string>() ?? "instance";
                    _handler.DryRun = parameters?["dryRun"]?.Value<bool>() ?? false;

                    _handler.SetParameters();

                    if (RaiseAndWaitForCompletion(120000)) // 2-minute timeout for large model scans
                        return _handler.Result;

                    throw new TimeoutException("Clear parameter values timed out — try narrowing scope with categories or elementIds");
                }
                catch (Exception ex)
                {
                    throw new Exception($"Clear parameter values failed: {ex.Message}");
                }
            }
        }
    }
}
