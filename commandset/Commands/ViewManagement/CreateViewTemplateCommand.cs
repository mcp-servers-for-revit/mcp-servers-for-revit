using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Services.ViewManagement;
using RevitMCPSDK.API.Base;
using System;

namespace RevitMCPCommandSet.Commands.ViewManagement
{
    public class CreateViewTemplateCommand : ExternalEventCommandBase
    {
        private static readonly object _executionLock = new object();
        private CreateViewTemplateEventHandler _handler => (CreateViewTemplateEventHandler)Handler;
        public override string CommandName => "create_view_template";

        public CreateViewTemplateCommand(UIApplication uiApp)
            : base(new CreateViewTemplateEventHandler(), uiApp) { }

        public override object Execute(JObject parameters, string requestId)
        {
            lock (_executionLock)
            {
                try
                {
                    _handler.SourceViewId = parameters?["sourceViewId"]?.Value<long>();
                    _handler.SourceViewName = parameters?["sourceViewName"]?.Value<string>();
                    _handler.TemplateName = parameters?["templateName"]?.Value<string>();

                    if (string.IsNullOrWhiteSpace(_handler.TemplateName))
                        throw new ArgumentException("templateName is required");

                    _handler.SetParameters();
                    if (RaiseAndWaitForCompletion(120000))
                        return _handler.Result;
                    throw new TimeoutException("Create view template timed out");
                }
                catch (Exception ex) { throw new Exception($"Create view template failed: {ex.Message}"); }
            }
        }
    }
}
