using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Services.DataExtraction;
using RevitMCPSDK.API.Base;
using System;

namespace RevitMCPCommandSet.Commands.DataExtraction
{
    public class ListSchedulableFieldsCommand : ExternalEventCommandBase
    {
        private static readonly object _executionLock = new object();
        private ListSchedulableFieldsEventHandler _handler => (ListSchedulableFieldsEventHandler)Handler;

        public override string CommandName => "list_schedulable_fields";

        public ListSchedulableFieldsCommand(UIApplication uiApp)
            : base(new ListSchedulableFieldsEventHandler(), uiApp) { }

        public override object Execute(JObject parameters, string requestId)
        {
            lock (_executionLock)
            {
                try
                {
                    _handler.CategoryName = parameters?["categoryName"]?.Value<string>() ?? "OST_Rooms";
                    _handler.ScheduleType = parameters?["scheduleType"]?.Value<string>() ?? "regular";

                    _handler.SetParameters();

                    if (RaiseAndWaitForCompletion(30000))
                        return _handler.Result;

                    throw new TimeoutException("List schedulable fields timed out");
                }
                catch (Exception ex)
                {
                    throw new Exception($"List schedulable fields failed: {ex.Message}");
                }
            }
        }
    }
}
