using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Services.DataExtraction;
using RevitMCPSDK.API.Base;
using System;
using System.Collections.Generic;

namespace RevitMCPCommandSet.Commands.DataExtraction
{
    public class TransferParametersCommand : ExternalEventCommandBase
    {
        private static readonly object _executionLock = new object();
        private TransferParametersEventHandler _handler => (TransferParametersEventHandler)Handler;

        public override string CommandName => "transfer_parameters";

        public TransferParametersCommand(UIApplication uiApp)
            : base(new TransferParametersEventHandler(), uiApp) { }

        public override object Execute(JObject parameters, string requestId)
        {
            lock (_executionLock)
            {
                try
                {
                    _handler.SourceElementId = parameters?["sourceElementId"]?.Value<long>() ?? throw new ArgumentException("sourceElementId is required");
                    _handler.TargetElementIds = parameters?["targetElementIds"]?.ToObject<List<long>>() ?? throw new ArgumentException("targetElementIds is required");
                    _handler.ParameterNames = parameters?["parameterNames"]?.ToObject<List<string>>() ?? new List<string>();
                    _handler.IncludeType = parameters?["includeType"]?.Value<bool>() ?? false;
                    _handler.DryRun = parameters?["dryRun"]?.Value<bool>() ?? false;

                    _handler.SetParameters();

                    if (RaiseAndWaitForCompletion(30000))
                        return _handler.Result;

                    throw new TimeoutException("Transfer parameters timed out");
                }
                catch (Exception ex)
                {
                    throw new Exception($"Transfer parameters failed: {ex.Message}");
                }
            }
        }
    }
}
