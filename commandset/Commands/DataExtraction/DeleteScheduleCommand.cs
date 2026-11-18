using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Services.DataExtraction;
using RevitMCPSDK.API.Base;
using System;

namespace RevitMCPCommandSet.Commands.DataExtraction
{
    public class DeleteScheduleCommand : ExternalEventCommandBase
    {
        private static readonly object _executionLock = new object();
        private DeleteScheduleEventHandler _handler => (DeleteScheduleEventHandler)Handler;

        public override string CommandName => "delete_schedule";

        public DeleteScheduleCommand(UIApplication uiApp)
            : base(new DeleteScheduleEventHandler(), uiApp) { }

        public override object Execute(JObject parameters, string requestId)
        {
            lock (_executionLock)
            {
                try
                {
                    _handler.ScheduleId = parameters?["scheduleId"]?.Value<long>() ?? 0;
                    _handler.ScheduleName = parameters?["scheduleName"]?.Value<string>() ?? "";
                    _handler.Confirm = parameters?["confirm"]?.Value<bool>() ?? false;

                    _handler.SetParameters();

                    if (RaiseAndWaitForCompletion(30000))
                        return _handler.Result;

                    throw new TimeoutException("Delete schedule timed out");
                }
                catch (Exception ex)
                {
                    throw new Exception($"Delete schedule failed: {ex.Message}");
                }
            }
        }
    }
}
