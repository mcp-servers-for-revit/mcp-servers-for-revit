using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Services;
using RevitMCPSDK.API.Base;
using System;
using System.Collections.Generic;

namespace RevitMCPCommandSet.Commands
{
    public class FilterByParameterValueCommand : ExternalEventCommandBase
    {
        private FilterByParameterValueEventHandler _handler => (FilterByParameterValueEventHandler)Handler;

        public override string CommandName => "filter_by_parameter_value";

        public FilterByParameterValueCommand(UIApplication uiApp)
            : base(new FilterByParameterValueEventHandler(), uiApp) { }

        public override object Execute(JObject parameters, string requestId)
        {
            try
            {
                _handler.Categories = parameters?["categories"]?.ToObject<List<string>>() ?? new List<string>();
                _handler.ParameterName = parameters?["parameterName"]?.Value<string>() ?? "";
                _handler.Condition = parameters?["condition"]?.Value<string>() ?? "equals";
                _handler.Value = parameters?["value"]?.Value<string>() ?? "";
                _handler.CaseSensitive = parameters?["caseSensitive"]?.Value<bool>() ?? false;
                _handler.Scope = parameters?["scope"]?.Value<string>() ?? "whole_model";
                _handler.ParameterType = parameters?["parameterType"]?.Value<string>() ?? "both";
                _handler.ReturnParameters = parameters?["returnParameters"]?.ToObject<List<string>>() ?? new List<string>();

                _handler.SetParameters();

                if (RaiseAndWaitForCompletion(30000))
                    return _handler.Result;

                throw new TimeoutException("Filter by parameter value timed out");
            }
            catch (Exception ex)
            {
                throw new Exception($"Filter by parameter value failed: {ex.Message}");
            }
        }
    }
}
