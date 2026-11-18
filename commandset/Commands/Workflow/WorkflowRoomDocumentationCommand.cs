using System;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Services.Workflow;
using RevitMCPSDK.API.Base;

namespace RevitMCPCommandSet.Commands.Workflow
{
    public class WorkflowRoomDocumentationCommand : ExternalEventCommandBase
    {
        private WorkflowRoomDocumentationEventHandler _handler => (WorkflowRoomDocumentationEventHandler)Handler;
        public override string CommandName => "workflow_room_documentation";

        public WorkflowRoomDocumentationCommand(UIApplication uiApp)
            : base(new WorkflowRoomDocumentationEventHandler(), uiApp) { }

        public override object Execute(JObject parameters, string requestId)
        {
            try
            {
                _handler.SetParameters(
                    levelName: parameters?["levelName"]?.Value<string>() ?? "",
                    createSections: parameters?["createSections"]?.Value<bool>() ?? true,
                    offset: parameters?["offset"]?.Value<double>() ?? 300
                );
                if (RaiseAndWaitForCompletion(120000))
                    return _handler.Result;
                throw new TimeoutException("Workflow room documentation timed out");
            }
            catch (Exception ex)
            {
                throw new Exception($"Workflow room documentation failed: {ex.Message}");
            }
        }
    }
}
