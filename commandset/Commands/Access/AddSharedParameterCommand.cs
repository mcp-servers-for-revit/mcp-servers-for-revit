using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Services;
using RevitMCPSDK.API.Base;

namespace RevitMCPCommandSet.Commands.Access
{
    public class AddSharedParameterCommand : ExternalEventCommandBase
    {
        private static readonly object _executionLock = new object();
        private AddSharedParameterEventHandler _handler => (AddSharedParameterEventHandler)Handler;

        public override string CommandName => "add_shared_parameter";

        public AddSharedParameterCommand(UIApplication uiApp)
            : base(new AddSharedParameterEventHandler(), uiApp)
        {
        }

        public override object Execute(JObject parameters, string requestId)
        {
            lock (_executionLock)
            {
                try
                {
                    var parameterName = parameters?["parameterName"]?.Value<string>();
                    if (string.IsNullOrEmpty(parameterName))
                        throw new ArgumentException("parameterName is required and cannot be empty");

                    var groupName = parameters?["groupName"]?.Value<string>();
                    if (string.IsNullOrEmpty(groupName))
                        throw new ArgumentException("groupName is required and cannot be empty");

                    var categories = parameters?["categories"]?.ToObject<List<string>>();
                    if (categories == null || categories.Count == 0)
                        throw new ArgumentException("categories is required and cannot be empty");

                    _handler.ParameterName = parameterName;
                    _handler.GroupName = groupName;
                    _handler.Categories = categories;
                    _handler.IsInstance = parameters?["isInstance"]?.Value<bool>() ?? true;
                    _handler.ParameterGroup = parameters?["parameterGroup"]?.Value<string>();

                    if (RaiseAndWaitForCompletion(15000))
                    {
                        return _handler.Result;
                    }
                    else
                    {
                        throw new TimeoutException("Add shared parameter timed out");
                    }
                }
                catch (Exception ex)
                {
                    throw new Exception($"Add shared parameter failed: {ex.Message}");
                }
            }
        }
    }
}
