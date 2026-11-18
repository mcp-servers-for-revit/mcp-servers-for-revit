using System;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Services.Workflow;
using RevitMCPSDK.API.Base;

namespace RevitMCPCommandSet.Commands.Workflow
{
    public class WorkflowClashReviewCommand : ExternalEventCommandBase
    {
        private WorkflowClashReviewEventHandler _handler => (WorkflowClashReviewEventHandler)Handler;
        public override string CommandName => "workflow_clash_review";

        public WorkflowClashReviewCommand(UIApplication uiApp)
            : base(new WorkflowClashReviewEventHandler(), uiApp) { }

        public override object Execute(JObject parameters, string requestId)
        {
            try
            {
                _handler.SetParameters(
                    categoryA: parameters?["categoryA"]?.Value<string>() ?? "",
                    categoryB: parameters?["categoryB"]?.Value<string>() ?? "",
                    tolerance: parameters?["tolerance"]?.Value<double>() ?? 0,
                    createSectionBox: parameters?["createSectionBox"]?.Value<bool>() ?? true
                );
                if (RaiseAndWaitForCompletion(120000))
                    return _handler.Result;
                throw new TimeoutException("Workflow clash review timed out");
            }
            catch (Exception ex)
            {
                throw new Exception($"Workflow clash review failed: {ex.Message}");
            }
        }
    }
}
