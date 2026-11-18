using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Services.DataExtraction;
using RevitMCPSDK.API.Base;
using System;

namespace RevitMCPCommandSet.Commands.DataExtraction
{
    public class DuplicateScheduleCommand : ExternalEventCommandBase
    {
        private static readonly object _executionLock = new object();
        private DuplicateScheduleEventHandler _handler => (DuplicateScheduleEventHandler)Handler;

        public override string CommandName => "duplicate_schedule";

        public DuplicateScheduleCommand(UIApplication uiApp)
            : base(new DuplicateScheduleEventHandler(), uiApp) { }

        public override object Execute(JObject parameters, string requestId)
        {
            lock (_executionLock)
            {
                try
                {
                    _handler.ScheduleId = parameters?["scheduleId"]?.Value<long>() ?? 0;
                    _handler.ScheduleName = parameters?["scheduleName"]?.Value<string>() ?? "";
                    _handler.NewName = parameters?["newName"]?.Value<string>() ?? "";

                    if (string.IsNullOrEmpty(_handler.NewName))
                        throw new ArgumentException("newName is required for duplicate_schedule");

                    _handler.SetParameters();

                    if (RaiseAndWaitForCompletion(30000))
                        return _handler.Result;

                    throw new TimeoutException("Duplicate schedule timed out");
                }
                catch (Exception ex)
                {
                    throw new Exception($"Duplicate schedule failed: {ex.Message}");
                }
            }
        }
    }
}
