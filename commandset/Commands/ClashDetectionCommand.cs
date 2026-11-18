using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Services;
using RevitMCPSDK.API.Base;

namespace RevitMCPCommandSet.Commands
{
    public class ClashDetectionCommand : ExternalEventCommandBase
    {
        private static readonly object _executionLock = new object();
        private ClashDetectionEventHandler _handler => (ClashDetectionEventHandler)Handler;

        public override string CommandName => "clash_detection";

        public ClashDetectionCommand(UIApplication uiApp)
            : base(new ClashDetectionEventHandler(), uiApp)
        {
        }

        public override object Execute(JObject parameters, string requestId)
        {
            lock (_executionLock)
            {
                try
                {
                    _handler.CategoryA = parameters?["categoryA"]?.Value<string>() ?? "";
                    _handler.CategoryB = parameters?["categoryB"]?.Value<string>() ?? "";
                    _handler.ElementIdsA = parameters?["elementIdsA"]?.ToObject<List<long>>() ?? new List<long>();
                    _handler.ElementIdsB = parameters?["elementIdsB"]?.ToObject<List<long>>() ?? new List<long>();
                    _handler.Tolerance = parameters?["tolerance"]?.Value<double>() ?? 0;
                    _handler.MaxResults = parameters?["maxResults"]?.Value<int>() ?? 100;

                    if (RaiseAndWaitForCompletion(30000))
                    {
                        return _handler.Result;
                    }
                    else
                    {
                        throw new TimeoutException("Clash detection timed out");
                    }
                }
                catch (Exception ex)
                {
                    throw new Exception($"Clash detection failed: {ex.Message}");
                }
            }
        }
    }
}
