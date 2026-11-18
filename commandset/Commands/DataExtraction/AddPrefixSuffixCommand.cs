using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Services.DataExtraction;
using RevitMCPSDK.API.Base;
using System;
using System.Collections.Generic;

namespace RevitMCPCommandSet.Commands.DataExtraction
{
    public class AddPrefixSuffixCommand : ExternalEventCommandBase
    {
        private static readonly object _executionLock = new object();
        private AddPrefixSuffixEventHandler _handler => (AddPrefixSuffixEventHandler)Handler;

        public override string CommandName => "add_prefix_suffix";

        public AddPrefixSuffixCommand(UIApplication uiApp)
            : base(new AddPrefixSuffixEventHandler(), uiApp) { }

        public override object Execute(JObject parameters, string requestId)
        {
            lock (_executionLock)
            {
                try
                {
                    _handler.ParameterName = parameters?["parameterName"]?.Value<string>() ?? "";
                    _handler.Prefix = parameters?["prefix"]?.Value<string>() ?? "";
                    _handler.Suffix = parameters?["suffix"]?.Value<string>() ?? "";
                    _handler.Separator = parameters?["separator"]?.Value<string>() ?? "";
                    _handler.Categories = parameters?["categories"]?.ToObject<List<string>>() ?? new List<string>();
                    _handler.Scope = parameters?["scope"]?.Value<string>() ?? "whole_model";
                    _handler.SkipEmpty = parameters?["skipEmpty"]?.Value<bool>() ?? true;
                    _handler.FilterValue = parameters?["filterValue"]?.Value<string>() ?? "";
                    _handler.DryRun = parameters?["dryRun"]?.Value<bool>() ?? false;

                    _handler.SetParameters();

                    if (RaiseAndWaitForCompletion(120000)) // 2-minute timeout for large model scans
                        return _handler.Result;

                    throw new TimeoutException("Add prefix/suffix timed out — try narrowing scope with categories or elementIds");
                }
                catch (Exception ex)
                {
                    throw new Exception($"Add prefix/suffix failed: {ex.Message}");
                }
            }
        }
    }
}
