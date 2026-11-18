using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Services.DataExtraction;
using RevitMCPSDK.API.Base;
using System;
using System.Collections.Generic;

namespace RevitMCPCommandSet.Commands.DataExtraction
{
    public class SetMaterialPropertiesCommand : ExternalEventCommandBase
    {
        private static readonly object _executionLock = new object();
        private SetMaterialPropertiesEventHandler _handler => (SetMaterialPropertiesEventHandler)Handler;

        public override string CommandName => "set_material_properties";

        public SetMaterialPropertiesCommand(UIApplication uiApp)
            : base(new SetMaterialPropertiesEventHandler(), uiApp) { }

        public override object Execute(JObject parameters, string requestId)
        {
            lock (_executionLock)
            {
                try
                {
                    // Parse requests array
                    var requestsJson = parameters?["requests"]?.ToObject<List<JObject>>() ?? new List<JObject>();
                    var requests = new List<SetMaterialRequest>();

                    foreach (var req in requestsJson)
                    {
                        requests.Add(new SetMaterialRequest
                        {
                            MaterialId = req["materialId"]?.Value<long>() ?? 0,
                            Comments = req["comments"]?.Value<string>(),
                            Description = req["description"]?.Value<string>(),
                            Manufacturer = req["manufacturer"]?.Value<string>(),
                            Model = req["model"]?.Value<string>(),
                            Url = req["url"]?.Value<string>(),
                            Cost = req["cost"]?.Value<double?>(),
                            Mark = req["mark"]?.Value<string>(),
                            Keynote = req["keynote"]?.Value<string>(),
                            Name = req["name"]?.Value<string>()
                        });
                    }

                    _handler.Requests = requests;
                    _handler.DryRun = parameters?["dryRun"]?.Value<bool>() ?? true;

                    _handler.SetParameters();

                    if (RaiseAndWaitForCompletion(120000)) // 2-minute timeout
                        return _handler.Result;

                    throw new TimeoutException("Set material properties timed out — try reducing the number of materials");
                }
                catch (Exception ex)
                {
                    throw new Exception($"Set material properties failed: {ex.Message}");
                }
            }
        }
    }
}
