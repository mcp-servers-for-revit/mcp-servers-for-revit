using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Services.ViewManagement;
using RevitMCPSDK.API.Base;
using System;
using System.Collections.Generic;

namespace RevitMCPCommandSet.Commands.ViewManagement
{
    public class ManageUnplacedViewsCommand : ExternalEventCommandBase
    {
        private static readonly object _executionLock = new object();
        private ManageUnplacedViewsEventHandler _handler => (ManageUnplacedViewsEventHandler)Handler;

        public override string CommandName => "manage_unplaced_views";

        public ManageUnplacedViewsCommand(UIApplication uiApp)
            : base(new ManageUnplacedViewsEventHandler(), uiApp) { }

        public override object Execute(JObject parameters, string requestId)
        {
            lock (_executionLock)
            {
                try
                {
                    _handler.SetParameters(
                        parameters?["action"]?.Value<string>() ?? "list",
                        parameters?["viewTypes"]?.ToObject<List<string>>() ?? new List<string>(),
                        parameters?["filterName"]?.Value<string>() ?? "",
                        parameters?["excludeNames"]?.ToObject<List<string>>() ?? new List<string>(),
                        parameters?["dryRun"]?.Value<bool>() ?? true,
                        parameters?["maxResults"]?.Value<int>() ?? 500
                    );

                    if (RaiseAndWaitForCompletion(30000))
                        return _handler.Result;

                    throw new TimeoutException("Manage unplaced views timed out");
                }
                catch (Exception ex)
                {
                    throw new Exception($"Manage unplaced views failed: {ex.Message}");
                }
            }
        }
    }
}
