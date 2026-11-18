using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Services.ViewManagement;
using RevitMCPSDK.API.Base;

namespace RevitMCPCommandSet.Commands.ViewManagement
{
    public class CreateCalloutFromRoomsCommand : ExternalEventCommandBase
    {
        private CreateCalloutFromRoomsEventHandler _handler => (CreateCalloutFromRoomsEventHandler)Handler;
        public override string CommandName => "create_callout_from_rooms";

        public CreateCalloutFromRoomsCommand(UIApplication uiApp)
            : base(new CreateCalloutFromRoomsEventHandler(), uiApp) { }

        public override object Execute(JObject parameters, string requestId)
        {
            try
            {
                var roomIds = (parameters?["roomIds"] as JArray)?
                    .Select(t => t.Value<long>()).ToList() ?? new List<long>();

                _handler.SetParameters(
                    roomIds: roomIds,
                    levelName: parameters?["levelName"]?.ToString() ?? "",
                    offset: parameters?["offset"]?.Value<double>() ?? 300,
                    viewTemplateId: parameters?["viewTemplateId"]?.ToString() ?? "",
                    scale: parameters?["scale"]?.Value<int>() ?? 50
                );

                if (RaiseAndWaitForCompletion(60000))
                    return _handler.Result;

                throw new TimeoutException("Create callout from rooms timed out");
            }
            catch (Exception ex)
            {
                throw new Exception($"Create callout from rooms failed: {ex.Message}");
            }
        }
    }
}
