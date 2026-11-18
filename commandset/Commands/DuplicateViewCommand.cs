using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Services;
using RevitMCPSDK.API.Base;

namespace RevitMCPCommandSet.Commands
{
    public class DuplicateViewCommand : ExternalEventCommandBase
    {
        private static readonly object _executionLock = new object();
        private DuplicateViewEventHandler _handler => (DuplicateViewEventHandler)Handler;

        public override string CommandName => "duplicate_view";

        public DuplicateViewCommand(UIApplication uiApp)
            : base(new DuplicateViewEventHandler(), uiApp)
        {
        }

        public override object Execute(JObject parameters, string requestId)
        {
            lock (_executionLock)
            {
                try
                {
                    _handler.ViewIds = parameters?["viewIds"]?.ToObject<List<long>>() ?? new List<long>();
                    _handler.DuplicateOption = parameters?["duplicateOption"]?.Value<string>() ?? "duplicate";
                    _handler.NewNameSuffix = parameters?["newNameSuffix"]?.Value<string>() ?? "";
                    _handler.NewNamePrefix = parameters?["newNamePrefix"]?.Value<string>() ?? "";

                    if (RaiseAndWaitForCompletion(15000))
                    {
                        return _handler.Result;
                    }
                    else
                    {
                        throw new TimeoutException("Duplicate view timed out");
                    }
                }
                catch (Exception ex)
                {
                    throw new Exception($"Duplicate view failed: {ex.Message}");
                }
            }
        }
    }
}
