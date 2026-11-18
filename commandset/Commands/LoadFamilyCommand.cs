using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Services;
using RevitMCPSDK.API.Base;

namespace RevitMCPCommandSet.Commands
{
    public class LoadFamilyCommand : ExternalEventCommandBase
    {
        private static readonly object _executionLock = new object();
        private LoadFamilyEventHandler _handler => (LoadFamilyEventHandler)Handler;

        public override string CommandName => "load_family";

        public LoadFamilyCommand(UIApplication uiApp)
            : base(new LoadFamilyEventHandler(), uiApp)
        {
        }

        public override object Execute(JObject parameters, string requestId)
        {
            lock (_executionLock)
            {
                try
                {
                    _handler.Action = parameters?["action"]?.Value<string>() ?? "list";
                    _handler.FamilyPath = parameters?["familyPath"]?.Value<string>() ?? "";
                    _handler.CategoryFilter = parameters?["categoryFilter"]?.Value<string>() ?? "";
                    _handler.SourceTypeId = parameters?["sourceTypeId"]?.Value<long>() ?? 0;
                    _handler.NewTypeName = parameters?["newTypeName"]?.Value<string>() ?? "";

                    if (RaiseAndWaitForCompletion(30000))
                    {
                        return _handler.Result;
                    }
                    else
                    {
                        throw new TimeoutException("Load family timed out");
                    }
                }
                catch (Exception ex)
                {
                    throw new Exception($"Load family failed: {ex.Message}");
                }
            }
        }
    }
}
