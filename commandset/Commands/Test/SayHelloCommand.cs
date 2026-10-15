using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPSDK.API.Base;
using RevitMCPCommandSet.Services;

namespace RevitMCPCommandSet.Commands.Test
{
    public class SayHelloCommand : RevitCommandBase
    {
        private static readonly object _executionLock = new object();
        private SayHelloEventHandler _handler => (SayHelloEventHandler)Handler;

        public override string CommandName => "say_hello";

        public SayHelloCommand(UIApplication uiApp)
            : base(new SayHelloEventHandler(), uiApp)
        {
        }

        public override object Execute(JObject parameters, string requestId)
        {
            lock (_executionLock)
            {
                try
                {
                    // Parse optional message parameter
                    string message = "Hello MCP!";
                    if (parameters?["message"] != null)
                    {
                        message = parameters["message"].ToString();
                    }

                    _handler.Message = message;

                    if (RaiseAndWaitForCompletion(15000))
                    {
                        return new { success = true, message = message };
                    }
                    else
                    {
                        throw new TimeoutException("Say hello operation timed out");
                    }
                }
                catch (Exception ex)
                {
                    throw new Exception($"Say hello failed: {ex.Message}");
                }
            }
        }
    }
}
