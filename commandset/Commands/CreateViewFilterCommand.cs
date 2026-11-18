using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Services;
using RevitMCPSDK.API.Base;

namespace RevitMCPCommandSet.Commands
{
    public class CreateViewFilterCommand : ExternalEventCommandBase
    {
        private static readonly object _executionLock = new object();
        private CreateViewFilterEventHandler _handler => (CreateViewFilterEventHandler)Handler;

        public override string CommandName => "create_view_filter";

        public CreateViewFilterCommand(UIApplication uiApp)
            : base(new CreateViewFilterEventHandler(), uiApp)
        {
        }

        public override object Execute(JObject parameters, string requestId)
        {
            lock (_executionLock)
            {
                try
                {
                    _handler.Action = parameters?["action"]?.Value<string>() ?? "list";
                    _handler.FilterName = parameters?["filterName"]?.Value<string>() ?? "";
                    _handler.CategoryNames = parameters?["categoryNames"]?.ToObject<List<string>>() ?? new List<string>();
                    _handler.ParameterName = parameters?["parameterName"]?.Value<string>() ?? "";
                    _handler.FilterRule = parameters?["filterRule"]?.Value<string>() ?? "";
                    _handler.FilterValue = parameters?["filterValue"]?.Value<string>() ?? "";
                    _handler.ViewId = parameters?["viewId"]?.Value<long>() ?? 0;
                    _handler.ColorR = parameters?["colorR"]?.Value<int>() ?? -1;
                    _handler.ColorG = parameters?["colorG"]?.Value<int>() ?? -1;
                    _handler.ColorB = parameters?["colorB"]?.Value<int>() ?? -1;
                    _handler.IsVisible = parameters?["isVisible"]?.Value<bool>() ?? true;

                    if (RaiseAndWaitForCompletion(15000))
                    {
                        return _handler.Result;
                    }
                    else
                    {
                        throw new TimeoutException("Create view filter timed out");
                    }
                }
                catch (Exception ex)
                {
                    throw new Exception($"Create view filter failed: {ex.Message}");
                }
            }
        }
    }
}
