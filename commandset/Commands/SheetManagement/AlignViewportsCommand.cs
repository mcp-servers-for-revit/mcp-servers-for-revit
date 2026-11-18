using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Services.SheetManagement;
using RevitMCPSDK.API.Base;
using System;
using System.Collections.Generic;

namespace RevitMCPCommandSet.Commands.SheetManagement
{
    public class AlignViewportsCommand : ExternalEventCommandBase
    {
        private static readonly object _executionLock = new object();
        private AlignViewportsEventHandler _handler => (AlignViewportsEventHandler)Handler;

        public override string CommandName => "align_viewports";

        public AlignViewportsCommand(UIApplication uiApp)
            : base(new AlignViewportsEventHandler(), uiApp) { }

        public override object Execute(JObject parameters, string requestId)
        {
            lock (_executionLock)
            {
                try
                {
                    _handler.SourceViewportId = parameters?["sourceViewportId"]?.Value<long>() ?? throw new ArgumentException("sourceViewportId is required");
                    _handler.TargetViewportIds = parameters?["targetViewportIds"]?.ToObject<List<long>>() ?? throw new ArgumentException("targetViewportIds is required");
                    _handler.AlignMode = parameters?["alignMode"]?.Value<string>() ?? "placement";

                    _handler.SetParameters();

                    if (RaiseAndWaitForCompletion(15000))
                        return _handler.Result;

                    throw new TimeoutException("Align viewports timed out");
                }
                catch (Exception ex)
                {
                    throw new Exception($"Align viewports failed: {ex.Message}");
                }
            }
        }
    }
}
