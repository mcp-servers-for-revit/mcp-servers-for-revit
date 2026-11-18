using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Services;
using RevitMCPSDK.API.Base;

namespace RevitMCPCommandSet.Commands.Delete
{
    public class DeleteElementCommand : ExternalEventCommandBase
    {
        private static readonly object _executionLock = new object();
        private DeleteElementEventHandler _handler => (DeleteElementEventHandler)Handler;

        public override string CommandName => "delete_element";

        public DeleteElementCommand(UIApplication uiApp)
            : base(new DeleteElementEventHandler(), uiApp)
        {
        }

        public override object Execute(JObject parameters, string requestId)
        {
            lock (_executionLock)
            {
                try
                {
                    // Parse array parameters
                    var elementIds = parameters?["elementIds"]?.ToObject<string[]>();
                    if (elementIds == null || elementIds.Length == 0)
                    {
                        throw new ArgumentException("Element ID list cannot be empty");
                    }

                    // Parse dryRun parameter (default: true for safety)
                    bool dryRun = parameters?["dryRun"]?.Value<bool>() ?? true;

                    // Set element IDs and dryRun flag
                    _handler.ElementIds = elementIds;
                    _handler.DryRun = dryRun;

                    // Raise external event and wait for completion
                    if (RaiseAndWaitForCompletion(15000))
                    {
                        if (_handler.IsSuccess)
                        {
                            return new { deleted = !dryRun, dryRun = dryRun, count = _handler.DeletedCount };
                        }
                        else
                        {
                            throw new Exception("Failed to delete element");
                        }
                    }
                    else
                    {
                        throw new TimeoutException("Delete element operation timed out");
                    }
                }
                catch (Exception ex)
                {
                    throw new Exception($"Failed to delete element: {ex.Message}");
                }
            }
        }
    }
}
