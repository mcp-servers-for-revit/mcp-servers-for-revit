using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Services;
using RevitMCPSDK.API.Base;

namespace RevitMCPCommandSet.Commands.Delete
{
    public class WipeEmptyTagsCommand : ExternalEventCommandBase
    {
        private static readonly object _executionLock = new object();
        private WipeEmptyTagsEventHandler _handler => (WipeEmptyTagsEventHandler)Handler;

        public override string CommandName => "wipe_empty_tags";

        public WipeEmptyTagsCommand(UIApplication uiApp)
            : base(new WipeEmptyTagsEventHandler(), uiApp)
        {
        }

        public override object Execute(JObject parameters, string requestId)
        {
            lock (_executionLock)
            {
                try
                {
                bool dryRun = parameters?["dryRun"]?.Value<bool>() ?? true;
                int? viewId = parameters?["viewId"]?.Value<int>();
                List<string> categories = parameters?["categories"]?.ToObject<List<string>>() ?? new List<string>();

                _handler.DryRun = dryRun;
                _handler.ViewId = viewId;
                _handler.Categories = categories;

                if (RaiseAndWaitForCompletion(30000))
                {
                    if (_handler.ErrorMessage != null)
                        throw new Exception(_handler.ErrorMessage);
                    return _handler.Result;
                }
                else
                {
                    throw new TimeoutException("Wipe empty tags timed out");
                }
            }
                catch (Exception ex)
                {
                    throw new Exception($"Failed to wipe empty tags: {ex.Message}");
                }
            }
        }
    }
}
