using System;
using System.Collections.Generic;
using Autodesk.Revit.UI;
using RevitMCPSDK.API.Base;
using Newtonsoft.Json.Linq;
using revit_mcp_plugin.Core;

namespace revit_mcp_plugin.Commands
{
    /// <summary>
    /// Built-in hot-reload command for command sets. Byte-loads matching
    /// command set dll files (from the Commands directory, not the Revit
    /// process), re-instantiates their IRevitCommand handlers and replaces
    /// them in the registry. Lets the toolbox iterate on command set code
    /// without restarting Revit.
    /// </summary>
    public class ReloadCommandSetsCommand : ExternalEventCommandBase
    {
        private ReloadCommandSetsEventHandler _handler => (ReloadCommandSetsEventHandler)Handler;

        public override string CommandName => "reload_command_set";

        public ReloadCommandSetsCommand(UIApplication uiApp)
            : base(new ReloadCommandSetsEventHandler(), uiApp)
        {
        }

        public override object Execute(JObject parameters, string requestId)
        {
            try
            {
                string assemblyName = parameters?["assemblyName"]?.ToString();
                _handler.AssemblyNameFilter = string.IsNullOrWhiteSpace(assemblyName) ? null : assemblyName.Trim();

                if (RaiseAndWaitForCompletion(60000))
                {
                    return _handler.Result;
                }
                throw new TimeoutException("reload_command_set timed out");
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to reload command sets: {ex.Message}", ex);
            }
        }
    }
}
