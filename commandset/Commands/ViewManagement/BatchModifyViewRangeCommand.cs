using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Services.ViewManagement;
using RevitMCPSDK.API.Base;
using System;
using System.Collections.Generic;

namespace RevitMCPCommandSet.Commands.ViewManagement
{
    public class BatchModifyViewRangeCommand : ExternalEventCommandBase
    {
        private static readonly object _executionLock = new object();
        private BatchModifyViewRangeEventHandler _handler => (BatchModifyViewRangeEventHandler)Handler;

        public override string CommandName => "batch_modify_view_range";

        public BatchModifyViewRangeCommand(UIApplication uiApp)
            : base(new BatchModifyViewRangeEventHandler(), uiApp) { }

        public override object Execute(JObject parameters, string requestId)
        {
            lock (_executionLock)
            {
                try
                {
                    _handler.ViewIds = parameters?["viewIds"]?.ToObject<List<long>>() ?? throw new ArgumentException("viewIds is required");
                    _handler.TopOffsetMm = parameters?["topOffsetMm"]?.Value<double?>();
                    _handler.CutPlaneOffsetMm = parameters?["cutPlaneOffsetMm"]?.Value<double?>();
                    _handler.BottomOffsetMm = parameters?["bottomOffsetMm"]?.Value<double?>();
                    _handler.ViewDepthOffsetMm = parameters?["viewDepthOffsetMm"]?.Value<double?>();

                    _handler.SetParameters();

                    if (RaiseAndWaitForCompletion(15000))
                        return _handler.Result;

                    throw new TimeoutException("Batch modify view range timed out");
                }
                catch (Exception ex)
                {
                    throw new Exception($"Batch modify view range failed: {ex.Message}");
                }
            }
        }
    }
}
