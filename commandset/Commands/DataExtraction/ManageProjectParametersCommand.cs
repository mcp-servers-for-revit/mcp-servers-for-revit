using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Services.DataExtraction;
using RevitMCPSDK.API.Base;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RevitMCPCommandSet.Commands.DataExtraction
{
    public class ManageProjectParametersCommand : ExternalEventCommandBase
    {
        private ManageProjectParametersEventHandler _handler => (ManageProjectParametersEventHandler)Handler;

        public override string CommandName => "manage_project_parameters";

        public ManageProjectParametersCommand(UIApplication uiApp)
            : base(new ManageProjectParametersEventHandler(), uiApp)
        {
        }

        public override object Execute(JObject parameters, string requestId)
        {
            try
            {
                string action = parameters?["action"]?.Value<string>() ?? "list";
                string parameterName = parameters?["parameterName"]?.Value<string>() ?? "";
                string dataType = parameters?["dataType"]?.Value<string>() ?? "Text";
                string groupUnder = parameters?["groupUnder"]?.Value<string>() ?? "PG_IDENTITY_DATA";
                bool isInstance = parameters?["isInstance"]?.Value<bool>() ?? true;
                bool isShared = parameters?["isShared"]?.Value<bool>() ?? false;

                var categoriesArray = parameters?["categories"] as JArray;
                var categories = categoriesArray != null
                    ? categoriesArray.Select(c => c.Value<string>()).Where(c => !string.IsNullOrWhiteSpace(c)).ToList()
                    : new List<string>();

                _handler.SetParameters(action, parameterName, dataType, groupUnder, isInstance, categories, isShared);

                if (RaiseAndWaitForCompletion(30000))
                    return _handler.Result;

                throw new TimeoutException("Manage project parameters timed out");
            }
            catch (Exception ex)
            {
                throw new Exception($"Manage project parameters failed: {ex.Message}");
            }
        }
    }
}
