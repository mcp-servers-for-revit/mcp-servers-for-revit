using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Services;
using RevitMCPSDK.API.Base;

namespace RevitMCPCommandSet.Commands
{
    public class CreateFilledRegionCommand : ExternalEventCommandBase
    {
        private static readonly object _executionLock = new object();
        private CreateFilledRegionEventHandler _handler => (CreateFilledRegionEventHandler)Handler;

        public override string CommandName => "create_filled_region";

        public CreateFilledRegionCommand(UIApplication uiApp)
            : base(new CreateFilledRegionEventHandler(), uiApp)
        {
        }

        public override object Execute(JObject parameters, string requestId)
        {
            lock (_executionLock)
            {
                try
                {
                    _handler.BoundaryPoints = parameters?["boundaryPoints"]?.ToObject<List<Dictionary<string, double>>>()
                                              ?? new List<Dictionary<string, double>>();
                    _handler.ViewId = parameters?["viewId"]?.Value<long>() ?? 0;
                    _handler.FilledRegionTypeName = parameters?["filledRegionTypeName"]?.Value<string>() ?? "";

                    if (RaiseAndWaitForCompletion(15000))
                    {
                        return _handler.Result;
                    }
                    else
                    {
                        throw new TimeoutException("Create filled region timed out");
                    }
                }
                catch (Exception ex)
                {
                    throw new Exception($"Create filled region failed: {ex.Message}");
                }
            }
        }
    }
}
