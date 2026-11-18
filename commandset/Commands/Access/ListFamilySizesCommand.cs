using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Services;
using RevitMCPSDK.API.Base;

namespace RevitMCPCommandSet.Commands.Access
{
    public class ListFamilySizesCommand : ExternalEventCommandBase
    {
        private static readonly object _executionLock = new object();
        private ListFamilySizesEventHandler _handler => (ListFamilySizesEventHandler)Handler;

        public override string CommandName => "list_family_sizes";

        public ListFamilySizesCommand(UIApplication uiApp)
            : base(new ListFamilySizesEventHandler(), uiApp)
        {
        }

        public override object Execute(JObject parameters, string requestId)
        {
            lock (_executionLock)
            {
                try
                {
                int limit = parameters?["limit"]?.Value<int>() ?? 50;
                string sortBy = parameters?["sortBy"]?.Value<string>() ?? "instanceCount";
                List<string> categories = parameters?["categories"]?.ToObject<List<string>>() ?? new List<string>();

                _handler.Limit = limit;
                _handler.SortBy = sortBy;
                _handler.Categories = categories;

                if (RaiseAndWaitForCompletion(60000))
                {
                    if (_handler.ErrorMessage != null)
                        throw new Exception(_handler.ErrorMessage);
                    return _handler.Result;
                }
                else
                {
                    throw new TimeoutException("List family sizes timed out");
                }
            }
                catch (Exception ex)
                {
                    throw new Exception($"Failed to list family sizes: {ex.Message}");
                }
            }
        }
    }
}
