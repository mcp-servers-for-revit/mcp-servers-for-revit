using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Services;
using RevitMCPSDK.API.Base;

namespace RevitMCPCommandSet.Commands.Access
{
    public class FindUndimensionedElementsCommand : ExternalEventCommandBase
    {
        private static readonly object _executionLock = new object();
        private FindUndimensionedElementsEventHandler _handler => (FindUndimensionedElementsEventHandler)Handler;

        public override string CommandName => "find_undimensioned_elements";

        public FindUndimensionedElementsCommand(UIApplication uiApp)
            : base(new FindUndimensionedElementsEventHandler(), uiApp)
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

                if (RaiseAndWaitForCompletion(120000)) // 2-minute timeout for scanning view elements
                {
                    if (_handler.ErrorMessage != null)
                        throw new Exception(_handler.ErrorMessage);
                    return _handler.Result;
                }
                else
                {
                    throw new TimeoutException("Find undimensioned elements timed out — try specifying categories to narrow scope");
                }
            }
                catch (Exception ex)
                {
                    throw new Exception($"Failed to find undimensioned elements: {ex.Message}");
                }
            }
        }
    }
}
