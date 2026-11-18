using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Services;
using RevitMCPSDK.API.Base;

namespace RevitMCPCommandSet.Commands
{
    public class BatchRenameCommand : ExternalEventCommandBase
    {
        private static readonly object _executionLock = new object();
        private BatchRenameEventHandler _handler => (BatchRenameEventHandler)Handler;

        public override string CommandName => "batch_rename";

        public BatchRenameCommand(UIApplication uiApp)
            : base(new BatchRenameEventHandler(), uiApp)
        {
        }

        public override object Execute(JObject parameters, string requestId)
        {
            lock (_executionLock)
            {
                try
                {
                    _handler.ElementIds = parameters?["elementIds"]?.ToObject<List<long>>() ?? new List<long>();
                    _handler.TargetCategory = parameters?["targetCategory"]?.Value<string>() ?? "";
                    _handler.FindText = parameters?["findText"]?.Value<string>() ?? "";
                    _handler.ReplaceText = parameters?["replaceText"]?.Value<string>() ?? "";
                    _handler.Prefix = parameters?["prefix"]?.Value<string>() ?? "";
                    _handler.Suffix = parameters?["suffix"]?.Value<string>() ?? "";
                    _handler.DryRun = parameters?["dryRun"]?.Value<bool>() ?? true;

                    if (RaiseAndWaitForCompletion(15000))
                    {
                        return _handler.Result;
                    }
                    else
                    {
                        throw new TimeoutException("Batch rename timed out");
                    }
                }
                catch (Exception ex)
                {
                    throw new Exception($"Batch rename failed: {ex.Message}");
                }
            }
        }
    }
}
