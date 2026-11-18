using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Models.Views;
using RevitMCPCommandSet.Services.DataExtraction;
using RevitMCPSDK.API.Base;
using System;
using System.Collections.Generic;

namespace RevitMCPCommandSet.Commands.DataExtraction
{
    public class ModifyScheduleCommand : ExternalEventCommandBase
    {
        private static readonly object _executionLock = new object();
        private ModifyScheduleEventHandler _handler => (ModifyScheduleEventHandler)Handler;

        public override string CommandName => "modify_schedule";

        public ModifyScheduleCommand(UIApplication uiApp)
            : base(new ModifyScheduleEventHandler(), uiApp) { }

        public override object Execute(JObject parameters, string requestId)
        {
            lock (_executionLock)
            {
                try
                {
                    _handler.ScheduleId = parameters?["scheduleId"]?.Value<long>() ?? 0;
                    _handler.ScheduleName = parameters?["scheduleName"]?.Value<string>() ?? "";
                    _handler.Action = parameters?["action"]?.Value<string>() ?? "";
                    _handler.FieldNames = parameters?["fieldNames"]?.ToObject<List<string>>() ?? new List<string>();
                    _handler.Filters = parameters?["filters"]?.ToObject<List<ScheduleFilterInfo>>() ?? new List<ScheduleFilterInfo>();
                    _handler.SortFields = parameters?["sortFields"]?.ToObject<List<ScheduleSortInfo>>() ?? new List<ScheduleSortInfo>();
                    _handler.NewName = parameters?["newName"]?.Value<string>() ?? "";
                    _handler.ShowTitle = parameters?["showTitle"]?.Value<bool>();
                    _handler.ShowHeaders = parameters?["showHeaders"]?.Value<bool>();
                    _handler.ShowGridLines = parameters?["showGridLines"]?.Value<bool>();
                    _handler.IsItemized = parameters?["isItemized"]?.Value<bool>();

                    _handler.SetParameters();

                    if (RaiseAndWaitForCompletion(60000))
                        return _handler.Result;

                    throw new TimeoutException("Modify schedule timed out");
                }
                catch (Exception ex)
                {
                    throw new Exception($"Modify schedule failed: {ex.Message}");
                }
            }
        }
    }
}
