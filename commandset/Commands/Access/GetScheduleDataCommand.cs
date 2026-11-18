using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Services;
using RevitMCPSDK.API.Base;

namespace RevitMCPCommandSet.Commands.Access
{
    public class GetScheduleDataCommand : ExternalEventCommandBase
    {
        private static readonly object _executionLock = new object();
        private GetScheduleDataEventHandler _handler => (GetScheduleDataEventHandler)Handler;

        public override string CommandName => "get_schedule_data";

        public GetScheduleDataCommand(UIApplication uiApp)
            : base(new GetScheduleDataEventHandler(), uiApp)
        {
        }

        public override object Execute(JObject parameters, string requestId)
        {
            lock (_executionLock)
            {
                try
                {
                    _handler.ScheduleId = parameters?["scheduleId"]?.Value<long>() ?? 0;
                    _handler.MaxRows = parameters?["maxRows"]?.Value<int>() ?? 500;

                    if (RaiseAndWaitForCompletion(15000))
                    {
                        return _handler.Result;
                    }
                    else
                    {
                        throw new TimeoutException("Get schedule data timed out");
                    }
                }
                catch (Exception ex)
                {
                    throw new Exception($"Get schedule data failed: {ex.Message}");
                }
            }
        }
    }
}
