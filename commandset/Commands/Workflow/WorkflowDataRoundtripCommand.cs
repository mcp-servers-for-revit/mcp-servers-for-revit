using System;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Services.Workflow;
using RevitMCPSDK.API.Base;

namespace RevitMCPCommandSet.Commands.Workflow
{
    public class WorkflowDataRoundtripCommand : ExternalEventCommandBase
    {
        private WorkflowDataRoundtripEventHandler _handler => (WorkflowDataRoundtripEventHandler)Handler;
        public override string CommandName => "workflow_data_roundtrip";

        public WorkflowDataRoundtripCommand(UIApplication uiApp)
            : base(new WorkflowDataRoundtripEventHandler(), uiApp) { }

        public override object Execute(JObject parameters, string requestId)
        {
            try
            {
                _handler.SetParameters(
                    categories: parameters?["categories"]?.ToObject<System.Collections.Generic.List<string>>() ?? new(),
                    parameterNames: parameters?["parameterNames"]?.ToObject<System.Collections.Generic.List<string>>() ?? new(),
                    includeTypeParameters: parameters?["includeTypeParameters"]?.Value<bool>() ?? false,
                    filePath: parameters?["filePath"]?.Value<string>() ?? ""
                );
                if (RaiseAndWaitForCompletion(120000))
                    return _handler.Result;
                throw new TimeoutException("Workflow data roundtrip timed out");
            }
            catch (Exception ex)
            {
                throw new Exception($"Workflow data roundtrip failed: {ex.Message}");
            }
        }
    }
}
