using System;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Services;
using RevitMCPSDK.API.Base;

namespace RevitMCPCommandSet.Commands.Access
{
    public class ExportSharedParameterFileCommand : ExternalEventCommandBase
    {
        private ExportSharedParameterFileEventHandler _handler => (ExportSharedParameterFileEventHandler)Handler;
        public override string CommandName => "export_shared_parameter_file";

        public ExportSharedParameterFileCommand(UIApplication uiApp)
            : base(new ExportSharedParameterFileEventHandler(), uiApp) { }

        public override object Execute(JObject parameters, string requestId)
        {
            try
            {
                _handler.SetParameters(
                    filePath: parameters?["filePath"]?.ToString() ?? ""
                );

                if (RaiseAndWaitForCompletion(30000))
                    return _handler.Result;

                throw new TimeoutException("Export shared parameter file timed out");
            }
            catch (Exception ex)
            {
                throw new Exception($"Export shared parameter file failed: {ex.Message}");
            }
        }
    }
}
