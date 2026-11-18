using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Services.ViewManagement;
using RevitMCPSDK.API.Base;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RevitMCPCommandSet.Commands.ViewManagement
{
    public class CreateElevationsFromRoomsCommand : ExternalEventCommandBase
    {
        private static readonly object _executionLock = new object();
        private CreateElevationsFromRoomsEventHandler _handler => (CreateElevationsFromRoomsEventHandler)Handler;

        public override string CommandName => "create_elevations_from_rooms";

        public CreateElevationsFromRoomsCommand(UIApplication uiApp)
            : base(new CreateElevationsFromRoomsEventHandler(), uiApp) { }

        public override object Execute(JObject parameters, string requestId)
        {
            lock (_executionLock)
            {
                try
                {
                    _handler.RoomIds = parameters?["roomIds"]?.ToObject<List<long>>() ?? new List<long>();
                    _handler.ViewType = parameters?["viewType"]?.Value<string>() ?? "elevation";
                    _handler.Directions = parameters?["directions"]?.ToObject<List<string>>() ?? new List<string> { "north", "south", "east", "west" };
                    _handler.Scale = parameters?["scale"]?.Value<int>() ?? 50;
                    _handler.OffsetMm = parameters?["offset"]?.Value<double>() ?? 300;
                    _handler.ViewTemplateId = parameters?["viewTemplateId"]?.Value<long>() ?? -1;
                    _handler.NamingPattern = parameters?["namingPattern"]?.Value<string>() ?? "{RoomName} - {Direction}";

                    _handler.SetParameters();

                    if (RaiseAndWaitForCompletion(60000))
                        return _handler.Result;

                    throw new TimeoutException("Create elevations from rooms timed out");
                }
                catch (Exception ex)
                {
                    throw new Exception($"Create elevations from rooms failed: {ex.Message}");
                }
            }
        }
    }
}
