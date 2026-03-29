using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPSDK.API.Base;

namespace RevitMCPCommandSet.Commands.ExecuteDynamicCode
{
    /// <summary>
    /// Command class for handling code execution
    /// </summary>
    public class ExecuteCodeCommand : ExternalEventCommandBase
    {
        private ExecuteCodeEventHandler _handler => (ExecuteCodeEventHandler)Handler;

        public override string CommandName => "send_code_to_revit";

        public ExecuteCodeCommand(UIApplication uiApp)
            : base(new ExecuteCodeEventHandler(), uiApp)
        {
        }

        public override object Execute(JObject parameters, string requestId)
        {
            try
            {
                // Parameter validation
                if (!parameters.ContainsKey("code"))
                {
                    throw new ArgumentException("Missing required parameter: 'code'");
                }

                // Parse code and parameters
                string code = parameters["code"].Value<string>();
                JArray parametersArray = parameters["parameters"] as JArray;
                object[] executionParameters = parametersArray?.ToObject<object[]>() ?? Array.Empty<object>();

                // Set execution parameters
                _handler.SetExecutionParameters(code, executionParameters);

                // Raise external event and wait for completion
                if (RaiseAndWaitForCompletion(60000)) // 1 minute timeout
                {
                    return _handler.ResultInfo;
                }
                else
                {
                    throw new TimeoutException("Code execution timed out");
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to execute code: {ex.Message}", ex);
            }
        }
    }
}
