using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Services;
using RevitMCPSDK.API.Base;

namespace RevitMCPCommandSet.Commands
{
    public class RenumberElementsCommand : ExternalEventCommandBase
    {
        private static readonly object _executionLock = new object();
        private RenumberElementsEventHandler _handler => (RenumberElementsEventHandler)Handler;

        public override string CommandName => "renumber_elements";

        public RenumberElementsCommand(UIApplication uiApp)
            : base(new RenumberElementsEventHandler(), uiApp)
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
                    _handler.ParameterName = parameters?["parameterName"]?.Value<string>() ?? "";
                    _handler.StartNumber = parameters?["startNumber"]?.Value<int>() ?? 1;
                    _handler.Prefix = parameters?["prefix"]?.Value<string>() ?? "";
                    _handler.Suffix = parameters?["suffix"]?.Value<string>() ?? "";
                    _handler.Increment = parameters?["increment"]?.Value<int>() ?? 1;
                    _handler.SortBy = parameters?["sortBy"]?.Value<string>() ?? "location";
                    _handler.DryRun = parameters?["dryRun"]?.Value<bool>() ?? true;

                    if (RaiseAndWaitForCompletion(15000))
                    {
                        return _handler.Result;
                    }
                    else
                    {
                        throw new TimeoutException("Renumber elements timed out");
                    }
                }
                catch (Exception ex)
                {
                    throw new Exception($"Renumber elements failed: {ex.Message}");
                }
            }
        }
    }
}
