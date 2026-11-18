using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Services.ViewManagement;
using RevitMCPSDK.API.Base;
using System;
using System.Collections.Generic;

namespace RevitMCPCommandSet.Commands.ViewManagement
{
    public class SectionBoxFromSelectionCommand : ExternalEventCommandBase
    {
        private static readonly object _executionLock = new object();
        private SectionBoxFromSelectionEventHandler _handler => (SectionBoxFromSelectionEventHandler)Handler;

        public override string CommandName => "section_box_from_selection";

        public SectionBoxFromSelectionCommand(UIApplication uiApp)
            : base(new SectionBoxFromSelectionEventHandler(), uiApp) { }

        public override object Execute(JObject parameters, string requestId)
        {
            lock (_executionLock)
            {
                try
                {
                    _handler.ElementIds = parameters?["elementIds"]?.ToObject<List<long>>() ?? new List<long>();
                    _handler.UseCurrentSelection = parameters?["useCurrentSelection"]?.Value<bool>() ?? (_handler.ElementIds.Count == 0);
                    _handler.OffsetMm = parameters?["offsetMm"]?.Value<double>() ?? 1000;
                    _handler.DuplicateView = parameters?["duplicateView"]?.Value<bool>() ?? true;
                    _handler.ViewName = parameters?["viewName"]?.Value<string>() ?? "";
                    _handler.IsolateElements = parameters?["isolateElements"]?.Value<bool>() ?? false;

                    _handler.SetParameters();

                    if (RaiseAndWaitForCompletion(15000))
                        return _handler.Result;

                    throw new TimeoutException("Section box from selection timed out");
                }
                catch (Exception ex)
                {
                    throw new Exception($"Section box from selection failed: {ex.Message}");
                }
            }
        }
    }
}
