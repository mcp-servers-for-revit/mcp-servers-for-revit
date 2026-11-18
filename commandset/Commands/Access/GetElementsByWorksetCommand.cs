using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Services;
using RevitMCPSDK.API.Base;

namespace RevitMCPCommandSet.Commands.Access
{
    public class GetElementsByWorksetCommand : ExternalEventCommandBase
    {
        private static readonly object _executionLock = new object();
        private GetElementsByWorksetEventHandler _handler => (GetElementsByWorksetEventHandler)Handler;

        public override string CommandName => "get_elements_by_workset";

        public GetElementsByWorksetCommand(UIApplication uiApp)
            : base(new GetElementsByWorksetEventHandler(), uiApp)
        {
        }

        public override object Execute(JObject parameters, string requestId)
        {
            lock (_executionLock)
            {
                try
                {
                    var worksetName = parameters?["worksetName"]?.Value<string>();
                    if (string.IsNullOrWhiteSpace(worksetName))
                        throw new ArgumentException("worksetName is required");

                    _handler.WorksetName = worksetName;
                    _handler.CategoryFilter = parameters?["categoryFilter"]?.ToObject<List<string>>();
                    _handler.MaxElements = parameters?["maxElements"]?.Value<int>() ?? 500;

                    if (RaiseAndWaitForCompletion(120000))
                    {
                        return _handler.Result;
                    }
                    else
                    {
                        throw new TimeoutException("Get elements by workset timed out");
                    }
                }
                catch (Exception ex)
                {
                    throw new Exception($"Get elements by workset failed: {ex.Message}");
                }
            }
        }
    }
}
