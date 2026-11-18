using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Services.DataExtraction;
using RevitMCPSDK.API.Base;
using System;
using System.Collections.Generic;

namespace RevitMCPCommandSet.Commands.DataExtraction
{
    public class GetRoomOpeningsCommand : ExternalEventCommandBase
    {
        private static readonly object _executionLock = new object();
        private GetRoomOpeningsEventHandler _handler => (GetRoomOpeningsEventHandler)Handler;

        public override string CommandName => "get_room_openings";

        public GetRoomOpeningsCommand(UIApplication uiApp)
            : base(new GetRoomOpeningsEventHandler(), uiApp) { }

        public override object Execute(JObject parameters, string requestId)
        {
            lock (_executionLock)
            {
                try
                {
                    _handler.RoomIds = parameters?["roomIds"]?.ToObject<List<long>>() ?? new List<long>();
                    _handler.RoomNumbers = parameters?["roomNumbers"]?.ToObject<List<string>>() ?? new List<string>();
                    _handler.LevelName = parameters?["levelName"]?.Value<string>() ?? "";
                    _handler.ElementType = parameters?["elementType"]?.Value<string>() ?? "both";
                    _handler.IncludeRoomParams = parameters?["includeRoomParams"]?.Value<bool>() ?? false;
                    _handler.IncludeElementParams = parameters?["includeElementParams"]?.Value<bool>() ?? false;
                    _handler.ParameterNames = parameters?["parameterNames"]?.ToObject<List<string>>() ?? new List<string>();
                    _handler.MaxElementsPerRoom = parameters?["maxElementsPerRoom"]?.Value<int>() ?? 100;

                    _handler.SetParameters();

                    if (RaiseAndWaitForCompletion(120000)) // 2 minutes
                        return _handler.Result;

                    throw new TimeoutException("Get room openings timed out — try specifying roomIds or roomNumbers to narrow scope");
                }
                catch (Exception ex)
                {
                    throw new Exception($"Get room openings failed: {ex.Message}");
                }
            }
        }
    }
}
