using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Services.ViewManagement;
using RevitMCPSDK.API.Base;

namespace RevitMCPCommandSet.Commands.ViewManagement
{
    public class ManageViewTemplatesCommand : ExternalEventCommandBase
    {
        private ManageViewTemplatesEventHandler _handler => (ManageViewTemplatesEventHandler)Handler;
        public override string CommandName => "manage_view_templates";

        public ManageViewTemplatesCommand(UIApplication uiApp)
            : base(new ManageViewTemplatesEventHandler(), uiApp) { }

        public override object Execute(JObject parameters, string requestId)
        {
            try
            {
                var templateIds = (parameters?["templateIds"] as JArray)?
                    .Select(t => t.Value<long>()).ToList() ?? new List<long>();

                _handler.SetParameters(
                    action: parameters?["action"]?.ToString() ?? "list",
                    templateIds: templateIds,
                    newName: parameters?["newName"]?.ToString() ?? "",
                    findText: parameters?["findText"]?.ToString() ?? "",
                    replaceText: parameters?["replaceText"]?.ToString() ?? "",
                    filterViewType: parameters?["filterViewType"]?.ToString() ?? ""
                );

                if (RaiseAndWaitForCompletion(30000))
                    return _handler.Result;

                throw new TimeoutException("Manage view templates timed out");
            }
            catch (Exception ex)
            {
                throw new Exception($"Manage view templates failed: {ex.Message}");
            }
        }
    }
}
