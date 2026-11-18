using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Services.ViewManagement;
using RevitMCPSDK.API.Base;
using System;
using System.Collections.Generic;

namespace RevitMCPCommandSet.Commands.ViewManagement
{
    public class CreateViewsFromRoomsCommand : ExternalEventCommandBase
    {
        private static readonly object _executionLock = new object();
        private CreateViewsFromRoomsEventHandler _handler => (CreateViewsFromRoomsEventHandler)Handler;

        public override string CommandName => "create_views_from_rooms";

        public CreateViewsFromRoomsCommand(UIApplication uiApp)
            : base(new CreateViewsFromRoomsEventHandler(), uiApp) { }

        public override object Execute(JObject parameters, string requestId)
        {
            lock (_executionLock)
            {
                try
                {
                    _handler.RoomIds = parameters?["roomIds"]?.ToObject<List<long>>() ?? new List<long>();
                    _handler.AllRooms = parameters?["allRooms"]?.Value<bool>() ?? (_handler.RoomIds.Count == 0);
                    _handler.ViewType = parameters?["viewType"]?.Value<string>() ?? "callout";
                    _handler.OffsetMm = parameters?["offsetMm"]?.Value<double>() ?? 500;
                    _handler.Scale = parameters?["scale"]?.Value<int>() ?? 50;
                    _handler.DetailLevel = parameters?["detailLevel"]?.Value<string>() ?? "Medium";
                    _handler.ViewTemplateName = parameters?["viewTemplateName"]?.Value<string>() ?? "";
                    _handler.NamingPattern = parameters?["namingPattern"]?.Value<string>() ?? "{RoomNumber} - {RoomName}";

                    _handler.SetParameters();

                    if (RaiseAndWaitForCompletion(60000))
                        return _handler.Result;

                    throw new TimeoutException("Create views from rooms timed out");
                }
                catch (Exception ex)
                {
                    throw new Exception($"Create views from rooms failed: {ex.Message}");
                }
            }
        }
    }
}
