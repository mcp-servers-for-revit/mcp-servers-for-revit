using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Services;
using RevitMCPSDK.API.Base;

namespace RevitMCPCommandSet.Commands
{
    public class CreateFloorCommand : ExternalEventCommandBase
    {
        private static readonly object _executionLock = new object();
        private CreateFloorEventHandler _handler => (CreateFloorEventHandler)Handler;

        public override string CommandName => "create_floor";

        public CreateFloorCommand(UIApplication uiApp)
            : base(new CreateFloorEventHandler(), uiApp)
        {
        }

        public override object Execute(JObject parameters, string requestId)
        {
            lock (_executionLock)
            {
                try
                {
                    _handler.BoundaryPoints = parameters?["boundaryPoints"]?.ToObject<List<Dictionary<string, double>>>() ?? new List<Dictionary<string, double>>();
                    _handler.RoomId = parameters?["roomId"]?.Value<long>() ?? 0;
                    _handler.FloorTypeName = parameters?["floorTypeName"]?.Value<string>() ?? "";
                    _handler.LevelElevation = parameters?["levelElevation"]?.Value<double>() ?? 0;
                    _handler.IsStructural = parameters?["isStructural"]?.Value<bool>() ?? false;

                    if (RaiseAndWaitForCompletion(15000))
                    {
                        return _handler.Result;
                    }
                    else
                    {
                        throw new TimeoutException("Create floor timed out");
                    }
                }
                catch (Exception ex)
                {
                    throw new Exception($"Create floor failed: {ex.Message}");
                }
            }
        }
    }
}
