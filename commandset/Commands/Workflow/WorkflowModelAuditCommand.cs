using System;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Services.Workflow;
using RevitMCPSDK.API.Base;

namespace RevitMCPCommandSet.Commands.Workflow
{
    public class WorkflowModelAuditCommand : ExternalEventCommandBase
    {
        private WorkflowModelAuditEventHandler _handler => (WorkflowModelAuditEventHandler)Handler;
        public override string CommandName => "workflow_model_audit";

        public WorkflowModelAuditCommand(UIApplication uiApp)
            : base(new WorkflowModelAuditEventHandler(), uiApp) { }

        public override object Execute(JObject parameters, string requestId)
        {
            try
            {
                _handler.SetParameters(
                    includeWarnings: parameters?["includeWarnings"]?.Value<bool>() ?? true,
                    includeFamilies: parameters?["includeFamilies"]?.Value<bool>() ?? true,
                    maxWarnings: parameters?["maxWarnings"]?.Value<int>() ?? 50
                );
                if (RaiseAndWaitForCompletion(120000))
                    return _handler.Result;
                throw new TimeoutException("Workflow model audit timed out");
            }
            catch (Exception ex)
            {
                throw new Exception($"Workflow model audit failed: {ex.Message}");
            }
        }
    }
}
