using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Services;
using RevitMCPSDK.API.Base;

namespace RevitMCPCommandSet.Commands.Access
{
    public class GetLinkedElementsCommand : ExternalEventCommandBase
    {
        private GetLinkedElementsEventHandler _handler => (GetLinkedElementsEventHandler)Handler;
        public override string CommandName => "get_linked_elements";

        public GetLinkedElementsCommand(UIApplication uiApp)
            : base(new GetLinkedElementsEventHandler(), uiApp) { }

        public override object Execute(JObject parameters, string requestId)
        {
            try
            {
                var categories = (parameters?["categories"] as JArray)?
                    .Select(t => t.ToString()).Where(s => !string.IsNullOrEmpty(s)).ToList()
                    ?? new List<string>();
                var parameterNames = (parameters?["parameterNames"] as JArray)?
                    .Select(t => t.ToString()).Where(s => !string.IsNullOrEmpty(s)).ToList()
                    ?? new List<string>();

                _handler.SetParameters(
                    linkName: parameters?["linkName"]?.ToString() ?? "",
                    categories: categories,
                    parameterNames: parameterNames,
                    maxElements: parameters?["maxElements"]?.Value<int>() ?? 5000
                );

                if (RaiseAndWaitForCompletion(60000))
                    return _handler.Result;

                throw new TimeoutException("Get linked elements timed out");
            }
            catch (Exception ex)
            {
                throw new Exception($"Get linked elements failed: {ex.Message}");
            }
        }
    }
}
