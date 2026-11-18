using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Services;
using RevitMCPSDK.API.Base;

namespace RevitMCPCommandSet.Commands
{
    public class CreateRevisionCommand : ExternalEventCommandBase
    {
        private static readonly object _executionLock = new object();
        private CreateRevisionEventHandler _handler => (CreateRevisionEventHandler)Handler;

        public override string CommandName => "create_revision";

        public CreateRevisionCommand(UIApplication uiApp)
            : base(new CreateRevisionEventHandler(), uiApp)
        {
        }

        public override object Execute(JObject parameters, string requestId)
        {
            lock (_executionLock)
            {
                try
                {
                    _handler.Action = parameters?["action"]?.Value<string>() ?? "list";
                    _handler.RevisionDate = parameters?["date"]?.Value<string>() ?? "";
                    _handler.RevisionDescription = parameters?["description"]?.Value<string>() ?? "";
                    _handler.IssuedBy = parameters?["issuedBy"]?.Value<string>() ?? "";
                    _handler.IssuedTo = parameters?["issuedTo"]?.Value<string>() ?? "";
                    _handler.SheetIds = parameters?["sheetIds"]?.ToObject<List<long>>() ?? new List<long>();

                    if (RaiseAndWaitForCompletion(15000))
                    {
                        return _handler.Result;
                    }
                    else
                    {
                        throw new TimeoutException("Create revision timed out");
                    }
                }
                catch (Exception ex)
                {
                    throw new Exception($"Create revision failed: {ex.Message}");
                }
            }
        }
    }
}
