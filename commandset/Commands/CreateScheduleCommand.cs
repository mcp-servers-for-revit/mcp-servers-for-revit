using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Models.Views;
using RevitMCPCommandSet.Services;
using RevitMCPSDK.API.Base;

namespace RevitMCPCommandSet.Commands
{
    public class CreateScheduleCommand : ExternalEventCommandBase
    {
        private static readonly object _executionLock = new object();
        private CreateScheduleEventHandler _handler => (CreateScheduleEventHandler)Handler;

        public override string CommandName => "create_schedule";

        public CreateScheduleCommand(UIApplication uiApp)
            : base(new CreateScheduleEventHandler(), uiApp)
        {
        }

        public override object Execute(JObject parameters, string requestId)
        {
            lock (_executionLock)
            {
                try
                {
                    var scheduleInfo = parameters?.ToObject<ScheduleCreationInfo>();
                    if (scheduleInfo == null)
                        throw new ArgumentException("Schedule creation info is required");

                    _handler.ScheduleInfo = scheduleInfo;

                    if (RaiseAndWaitForCompletion(120000))
                    {
                        return _handler.Result;
                    }
                    else
                    {
                        throw new TimeoutException("Create schedule timed out");
                    }
                }
                catch (Exception ex)
                {
                    throw new Exception($"Create schedule failed: {ex.Message}");
                }
            }
        }
    }
}
