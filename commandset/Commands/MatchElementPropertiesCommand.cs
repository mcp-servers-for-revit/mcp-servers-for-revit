using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Services;
using RevitMCPSDK.API.Base;

namespace RevitMCPCommandSet.Commands
{
    public class MatchElementPropertiesCommand : ExternalEventCommandBase
    {
        private static readonly object _executionLock = new object();
        private MatchElementPropertiesEventHandler _handler => (MatchElementPropertiesEventHandler)Handler;

        public override string CommandName => "match_element_properties";

        public MatchElementPropertiesCommand(UIApplication uiApp)
            : base(new MatchElementPropertiesEventHandler(), uiApp)
        {
        }

        public override object Execute(JObject parameters, string requestId)
        {
            lock (_executionLock)
            {
                try
                {
                    _handler.SourceElementId = parameters?["sourceElementId"]?.Value<long>() ?? 0;
                    _handler.TargetElementIds = parameters?["targetElementIds"]?.ToObject<List<long>>() ?? new List<long>();
                    _handler.ParameterNames = parameters?["parameterNames"]?.ToObject<List<string>>() ?? new List<string>();
                    _handler.IncludeTypeParameters = parameters?["includeTypeParameters"]?.Value<bool>() ?? false;

                    if (RaiseAndWaitForCompletion(15000))
                    {
                        return _handler.Result;
                    }
                    else
                    {
                        throw new TimeoutException("Match element properties timed out");
                    }
                }
                catch (Exception ex)
                {
                    throw new Exception($"Match element properties failed: {ex.Message}");
                }
            }
        }
    }
}
