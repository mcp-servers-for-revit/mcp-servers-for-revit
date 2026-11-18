using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Services.Workflow;
using RevitMCPSDK.API.Base;

namespace RevitMCPCommandSet.Commands.Workflow
{
    public class WorkflowSheetSetCommand : ExternalEventCommandBase
    {
        private WorkflowSheetSetEventHandler _handler => (WorkflowSheetSetEventHandler)Handler;
        public override string CommandName => "workflow_sheet_set";

        public WorkflowSheetSetCommand(UIApplication uiApp)
            : base(new WorkflowSheetSetEventHandler(), uiApp) { }

        public override object Execute(JObject parameters, string requestId)
        {
            try
            {
                var sheetsArray = parameters?["sheets"] as JArray;
                var sheetDefs = new List<WorkflowSheetSetEventHandler.SheetDefinition>();

                if (sheetsArray != null)
                {
                    foreach (var item in sheetsArray)
                    {
                        sheetDefs.Add(new WorkflowSheetSetEventHandler.SheetDefinition
                        {
                            Number = item["number"]?.Value<string>() ?? "",
                            Name = item["name"]?.Value<string>() ?? ""
                        });
                    }
                }

                _handler.SetParameters(
                    sheets: sheetDefs,
                    titleBlockName: parameters?["titleBlockName"]?.Value<string>() ?? ""
                );
                if (RaiseAndWaitForCompletion(60000))
                    return _handler.Result;
                throw new TimeoutException("Workflow sheet set timed out");
            }
            catch (Exception ex)
            {
                throw new Exception($"Workflow sheet set failed: {ex.Message}");
            }
        }
    }
}
