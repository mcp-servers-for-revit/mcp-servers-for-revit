using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Services.ViewManagement;
using RevitMCPSDK.API.Base;
using System;
using System.Collections.Generic;

namespace RevitMCPCommandSet.Commands.ViewManagement
{
    public class CreateColorLegendCommand : ExternalEventCommandBase
    {
        private static readonly object _executionLock = new object();
        private CreateColorLegendEventHandler _handler => (CreateColorLegendEventHandler)Handler;

        public override string CommandName => "create_color_legend";

        public CreateColorLegendCommand(UIApplication uiApp)
            : base(new CreateColorLegendEventHandler(), uiApp) { }

        public override object Execute(JObject parameters, string requestId)
        {
            lock (_executionLock)
            {
                try
                {
                    _handler.ParameterName = parameters?["parameterName"]?.Value<string>() ?? "";
                    _handler.Categories = parameters?["categories"]?.ToObject<List<string>>() ?? new List<string>();
                    _handler.ColorScheme = parameters?["colorScheme"]?.Value<string>() ?? "auto";
                    _handler.CreateLegendView = parameters?["createLegendView"]?.Value<bool>() ?? true;
                    _handler.LegendTitle = parameters?["legendTitle"]?.Value<string>() ?? "Color Legend";
                    _handler.TargetViewId = parameters?["targetViewId"]?.Value<long>() ?? 0;

                    // Parse customColors array: [{value, r, g, b}, ...]
                    var customColorsRaw = parameters?["customColors"] as JArray;
                    var customColors = new List<CreateColorLegendEventHandler.ColorMapping>();
                    if (customColorsRaw != null)
                    {
                        foreach (var item in customColorsRaw)
                        {
                            customColors.Add(new CreateColorLegendEventHandler.ColorMapping
                            {
                                Value = item["value"]?.Value<string>() ?? "",
                                R = item["r"]?.Value<int>() ?? 128,
                                G = item["g"]?.Value<int>() ?? 128,
                                B = item["b"]?.Value<int>() ?? 128
                            });
                        }
                    }
                    _handler.CustomColors = customColors;

                    _handler.SetParameters();

                    if (RaiseAndWaitForCompletion(60000))
                        return _handler.Result;

                    throw new TimeoutException("Create color legend timed out");
                }
                catch (Exception ex)
                {
                    throw new Exception($"Create color legend failed: {ex.Message}");
                }
            }
        }
    }
}
