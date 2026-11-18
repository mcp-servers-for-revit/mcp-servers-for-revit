using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Services.DataExtraction;
using RevitMCPSDK.API.Base;
using System;
using System.Collections.Generic;

namespace RevitMCPCommandSet.Commands.DataExtraction
{
    public class GetElementsInSpatialVolumeCommand : ExternalEventCommandBase
    {
        private static readonly object _executionLock = new object();
        private GetElementsInSpatialVolumeEventHandler _handler => (GetElementsInSpatialVolumeEventHandler)Handler;

        public override string CommandName => "get_elements_in_spatial_volume";

        public GetElementsInSpatialVolumeCommand(UIApplication uiApp)
            : base(new GetElementsInSpatialVolumeEventHandler(), uiApp) { }

        public override object Execute(JObject parameters, string requestId)
        {
            lock (_executionLock)
            {
                try
                {
                    _handler.VolumeIds = parameters?["volumeIds"]?.ToObject<List<long>>() ?? new List<long>();
                    _handler.VolumeType = parameters?["volumeType"]?.Value<string>() ?? "room";
                    _handler.CategoryFilter = parameters?["categoryFilter"]?.ToObject<List<string>>() ?? new List<string>();
                    _handler.MaxElementsPerVolume = parameters?["maxElementsPerVolume"]?.Value<int>() ?? 100;
                    _handler.CustomMinX = parameters?["customMinX"]?.Value<double>() ?? 0;
                    _handler.CustomMinY = parameters?["customMinY"]?.Value<double>() ?? 0;
                    _handler.CustomMinZ = parameters?["customMinZ"]?.Value<double>() ?? 0;
                    _handler.CustomMaxX = parameters?["customMaxX"]?.Value<double>() ?? 0;
                    _handler.CustomMaxY = parameters?["customMaxY"]?.Value<double>() ?? 0;
                    _handler.CustomMaxZ = parameters?["customMaxZ"]?.Value<double>() ?? 0;

                    _handler.SetParameters();

                    if (RaiseAndWaitForCompletion(30000))
                        return _handler.Result;

                    throw new TimeoutException("Get elements in spatial volume timed out");
                }
                catch (Exception ex)
                {
                    throw new Exception($"Get elements in spatial volume failed: {ex.Message}");
                }
            }
        }
    }
}
