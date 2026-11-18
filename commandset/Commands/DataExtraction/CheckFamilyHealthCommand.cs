using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Services.DataExtraction;
using RevitMCPSDK.API.Base;
using System;
using System.Linq;

namespace RevitMCPCommandSet.Commands.DataExtraction
{
    public class CheckFamilyHealthCommand : ExternalEventCommandBase
    {
        private static readonly object _executionLock = new object();
        private CheckFamilyHealthEventHandler _handler => (CheckFamilyHealthEventHandler)Handler;

        public override string CommandName => "check_family_health";

        public CheckFamilyHealthCommand(UIApplication uiApp)
            : base(new CheckFamilyHealthEventHandler(), uiApp) { }

        public override object Execute(JObject parameters, string requestId)
        {
            lock (_executionLock)
            {
                try
                {
                    _handler.Categories = parameters?["categories"]?
                        .ToObject<string[]>() ?? new string[0];
                    _handler.IncludeSystemFamilies = parameters?["includeSystemFamilies"]?.Value<bool>() ?? false;
                    _handler.IncludeFileSize = parameters?["includeFileSize"]?.Value<bool>() ?? false;
                    _handler.SortBy = parameters?["sortBy"]?.Value<string>() ?? "size";

                    _handler.SetParameters();

                    if (RaiseAndWaitForCompletion(60000))
                        return _handler.Result;

                    throw new TimeoutException("Check family health timed out");
                }
                catch (Exception ex)
                {
                    throw new Exception($"Check family health failed: {ex.Message}");
                }
            }
        }
    }
}
