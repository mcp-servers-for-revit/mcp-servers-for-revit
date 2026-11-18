using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Services;
using RevitMCPSDK.API.Base;

namespace RevitMCPCommandSet.Commands
{
    public class ExportScheduleCommand : ExternalEventCommandBase
    {
        private static readonly object _executionLock = new object();
        private ExportScheduleEventHandler _handler => (ExportScheduleEventHandler)Handler;

        public override string CommandName => "export_schedule";

        public ExportScheduleCommand(UIApplication uiApp)
            : base(new ExportScheduleEventHandler(), uiApp)
        {
        }

        public override object Execute(JObject parameters, string requestId)
        {
            lock (_executionLock)
            {
                try
                {
                    var scheduleId = parameters?.Value<long>("scheduleId") ?? 0;
                    if (scheduleId <= 0)
                        throw new ArgumentException("A valid scheduleId is required");

                    _handler.ScheduleId = scheduleId;
                    _handler.ExportPath = parameters?.Value<string>("exportPath");
                    _handler.Delimiter = parameters?.Value<string>("delimiter") ?? "Tab";
                    _handler.IncludeHeaders = parameters?.Value<bool?>("includeHeaders") ?? true;

                    if (RaiseAndWaitForCompletion(15000))
                    {
                        return _handler.Result;
                    }
                    else
                    {
                        throw new TimeoutException("Export schedule timed out");
                    }
                }
                catch (Exception ex)
                {
                    throw new Exception($"Export schedule failed: {ex.Message}");
                }
            }
        }
    }
}
