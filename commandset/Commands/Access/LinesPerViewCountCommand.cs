using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Services;
using RevitMCPSDK.API.Base;

namespace RevitMCPCommandSet.Commands.Access
{
    public class LinesPerViewCountCommand : ExternalEventCommandBase
    {
        private static readonly object _executionLock = new object();
        private LinesPerViewCountEventHandler _handler => (LinesPerViewCountEventHandler)Handler;

        public override string CommandName => "lines_per_view_count";

        public LinesPerViewCountCommand(UIApplication uiApp)
            : base(new LinesPerViewCountEventHandler(), uiApp)
        {
        }

        public override object Execute(JObject parameters, string requestId)
        {
            lock (_executionLock)
            {
                try
                {
                    int threshold = parameters?["threshold"]?.Value<int>() ?? 0;
                    bool includeDetailLines = parameters?["includeDetailLines"]?.Value<bool>() ?? true;
                    bool includeModelLines = parameters?["includeModelLines"]?.Value<bool>() ?? true;
                    int limit = parameters?["limit"]?.Value<int>() ?? 200;

                    _handler.Threshold = threshold;
                    _handler.IncludeDetailLines = includeDetailLines;
                    _handler.IncludeModelLines = includeModelLines;
                    _handler.Limit = limit;

                    if (RaiseAndWaitForCompletion(120000)) // 2-minute timeout for scanning all views
                    {
                        if (_handler.ErrorMessage != null)
                            throw new Exception(_handler.ErrorMessage);
                        return _handler.Result;
                    }
                    else
                    {
                        throw new TimeoutException("Lines per view count timed out — try reducing limit or increasing threshold");
                    }
                }
                catch (Exception ex)
                {
                    throw new Exception($"Failed to count lines per view: {ex.Message}");
                }
            }
        }
    }
}
