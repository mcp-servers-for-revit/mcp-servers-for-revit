using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Services;
using RevitMCPSDK.API.Base;
using System;
using System.Collections.Generic;

namespace RevitMCPCommandSet.Commands
{
    public class RenameFamiliesCommand : ExternalEventCommandBase
    {
        private static readonly object _executionLock = new object();
        private RenameFamiliesEventHandler _handler => (RenameFamiliesEventHandler)Handler;

        public override string CommandName => "rename_families";

        public RenameFamiliesCommand(UIApplication uiApp)
            : base(new RenameFamiliesEventHandler(), uiApp)
        {
        }

        public override object Execute(JObject parameters, string requestId)
        {
            lock (_executionLock)
            {
                try
                {
                    _handler.Operation = parameters?["operation"]?.Value<string>() ?? "prefix";
                    _handler.Prefix = parameters?["prefix"]?.Value<string>() ?? "";
                    _handler.Suffix = parameters?["suffix"]?.Value<string>() ?? "";
                    _handler.FindText = parameters?["findText"]?.Value<string>() ?? "";
                    _handler.ReplaceText = parameters?["replaceText"]?.Value<string>() ?? "";
                    _handler.Categories = parameters?["categories"]?.ToObject<List<string>>() ?? new List<string>();
                    _handler.Scope = parameters?["scope"]?.Value<string>() ?? "whole_model";
                    _handler.RenameTypes = parameters?["renameTypes"]?.Value<bool>() ?? false;
                    _handler.DryRun = parameters?["dryRun"]?.Value<bool>() ?? false;

                    _handler.SetParameters();

                    if (RaiseAndWaitForCompletion(120000)) // 2-minute timeout for large model scans
                        return _handler.Result;

                    throw new TimeoutException("Rename families timed out — try narrowing scope with categories");
                }
                catch (Exception ex)
                {
                    throw new Exception($"Rename families failed: {ex.Message}");
                }
            }
        }
    }
}
