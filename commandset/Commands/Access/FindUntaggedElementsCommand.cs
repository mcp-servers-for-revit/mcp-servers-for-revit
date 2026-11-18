using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Services;
using RevitMCPSDK.API.Base;

namespace RevitMCPCommandSet.Commands.Access
{
    public class FindUntaggedElementsCommand : ExternalEventCommandBase
    {
        private static readonly object _executionLock = new object();
        private FindUntaggedElementsEventHandler _handler => (FindUntaggedElementsEventHandler)Handler;

        public override string CommandName => "find_untagged_elements";

        public FindUntaggedElementsCommand(UIApplication uiApp)
            : base(new FindUntaggedElementsEventHandler(), uiApp)
        {
        }

        public override object Execute(JObject parameters, string requestId)
        {
            lock (_executionLock)
            {
                try
                {
                List<string> categories = parameters?["categories"]?.ToObject<List<string>>() ?? new List<string>();
                int? viewId = parameters?["viewId"]?.Value<int>();
                int limit = parameters?["limit"]?.Value<int>() ?? 500;

                _handler.Categories = categories;
                _handler.ViewId = viewId;
                _handler.Limit = limit;

                if (RaiseAndWaitForCompletion(30000))
                {
                    if (_handler.ErrorMessage != null)
                        throw new Exception(_handler.ErrorMessage);
                    return _handler.Result;
                }
                else
                {
                    throw new TimeoutException("Find untagged elements timed out");
                }
            }
                catch (Exception ex)
                {
                    throw new Exception($"Failed to find untagged elements: {ex.Message}");
                }
            }
        }
    }
}
