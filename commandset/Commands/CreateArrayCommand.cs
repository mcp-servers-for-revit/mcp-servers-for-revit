using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Services;
using RevitMCPSDK.API.Base;

namespace RevitMCPCommandSet.Commands
{
    public class CreateArrayCommand : ExternalEventCommandBase
    {
        private static readonly object _executionLock = new object();
        private CreateArrayEventHandler _handler => (CreateArrayEventHandler)Handler;

        public override string CommandName => "create_array";

        public CreateArrayCommand(UIApplication uiApp)
            : base(new CreateArrayEventHandler(), uiApp)
        {
        }

        public override object Execute(JObject parameters, string requestId)
        {
            lock (_executionLock)
            {
                try
                {
                    _handler.ElementIds = parameters?["elementIds"]?.ToObject<List<long>>() ?? new List<long>();
                    _handler.ArrayType = parameters?["arrayType"]?.Value<string>() ?? "linear";
                    _handler.Count = parameters?["count"]?.Value<int>() ?? 1;
                    _handler.SpacingX = parameters?["spacingX"]?.Value<double>() ?? 0;
                    _handler.SpacingY = parameters?["spacingY"]?.Value<double>() ?? 0;
                    _handler.SpacingZ = parameters?["spacingZ"]?.Value<double>() ?? 0;
                    _handler.CenterX = parameters?["centerX"]?.Value<double>() ?? 0;
                    _handler.CenterY = parameters?["centerY"]?.Value<double>() ?? 0;
                    _handler.TotalAngle = parameters?["totalAngle"]?.Value<double>() ?? 360;

                    if (RaiseAndWaitForCompletion(15000))
                    {
                        return _handler.Result;
                    }
                    else
                    {
                        throw new TimeoutException("Create array timed out");
                    }
                }
                catch (Exception ex)
                {
                    throw new Exception($"Create array failed: {ex.Message}");
                }
            }
        }
    }
}
