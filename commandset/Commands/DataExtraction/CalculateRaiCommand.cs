using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Services.DataExtraction;
using RevitMCPSDK.API.Base;
using System;
using System.Collections.Generic;

namespace RevitMCPCommandSet.Commands.DataExtraction
{
    public class CalculateRaiCommand : ExternalEventCommandBase
    {
        private static readonly object _executionLock = new object();
        private CalculateRaiEventHandler _handler => (CalculateRaiEventHandler)Handler;

        public override string CommandName => "calculate_rai";

        public CalculateRaiCommand(UIApplication uiApp)
            : base(new CalculateRaiEventHandler(), uiApp) { }

        public override object Execute(JObject parameters, string requestId)
        {
            lock (_executionLock)
            {
                try
                {
                    _handler.RoomIds = parameters?["roomIds"]?.ToObject<List<long>>() ?? new List<long>();
                    _handler.RoomNumbers = parameters?["roomNumbers"]?.ToObject<List<string>>() ?? new List<string>();
                    _handler.LevelName = parameters?["levelName"]?.Value<string>() ?? "";
                    _handler.MinRatio = parameters?["minRatio"]?.Value<double>() ?? 0.125;
                    _handler.IncludeServiceRooms = parameters?["includeServiceRooms"]?.Value<bool>() ?? false;
                    _handler.PhaseName = parameters?["phaseName"]?.Value<string>() ?? "";
                    _handler.RatioOverrides = parameters?["ratioOverrides"]?.ToObject<Dictionary<string, double>>()
                        ?? new Dictionary<string, double>();

                    _handler.SetParameters();

                    if (RaiseAndWaitForCompletion(120000))
                        return _handler.Result;

                    throw new TimeoutException("RAI calculation timed out");
                }
                catch (Exception ex)
                {
                    throw new Exception($"RAI calculation failed: {ex.Message}");
                }
            }
        }
    }
}
