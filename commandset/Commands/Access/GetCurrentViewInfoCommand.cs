using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Services;
using RevitMCPSDK.API.Base;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace RevitMCPCommandSet.Commands.Access
{
    public class GetCurrentViewInfoCommand : ExternalEventCommandBase
    {
        private GetCurrentViewInfoEventHandler _handler => (GetCurrentViewInfoEventHandler)Handler;

        public override string CommandName => "get_current_view_info";

        public GetCurrentViewInfoCommand(UIApplication uiApp)
            : base(new GetCurrentViewInfoEventHandler(), uiApp)
        {
        }

        public override object Execute(JObject parameters, string requestId)
        {
            // Raise external event and wait for completion
            if (RaiseAndWaitForCompletion(10000)) // 10 second timeout
            {
                if (_handler.ErrorMessage != null)
                    throw new Exception(_handler.ErrorMessage);
                return _handler.ResultInfo;
            }
            else
            {
                throw new TimeoutException("Get info timed out");
            }
        }
    }
}
