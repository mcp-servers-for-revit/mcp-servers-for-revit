using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Services;
using RevitMCPSDK.API.Base;

namespace RevitMCPCommandSet.Commands
{
    public class ApplyViewTemplateCommand : ExternalEventCommandBase
    {
        private static readonly object _executionLock = new object();
        private ApplyViewTemplateEventHandler _handler => (ApplyViewTemplateEventHandler)Handler;

        public override string CommandName => "apply_view_template";

        public ApplyViewTemplateCommand(UIApplication uiApp)
            : base(new ApplyViewTemplateEventHandler(), uiApp)
        {
        }

        public override object Execute(JObject parameters, string requestId)
        {
            lock (_executionLock)
            {
                try
                {
                    _handler.ViewIds = parameters?["viewIds"]?.ToObject<List<long>>() ?? new List<long>();
                    _handler.TemplateId = parameters?["templateId"]?.Value<long>() ?? 0;
                    _handler.TemplateName = parameters?["templateName"]?.Value<string>() ?? "";
                    _handler.Action = parameters?["action"]?.Value<string>() ?? "apply";

                    if (RaiseAndWaitForCompletion(15000))
                    {
                        return _handler.Result;
                    }
                    else
                    {
                        throw new TimeoutException("Apply view template timed out");
                    }
                }
                catch (Exception ex)
                {
                    throw new Exception($"Apply view template failed: {ex.Message}");
                }
            }
        }
    }
}
