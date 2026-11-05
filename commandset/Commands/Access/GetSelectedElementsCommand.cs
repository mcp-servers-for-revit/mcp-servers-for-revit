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
    public class GetSelectedElementsCommand : ExternalEventCommandBase
    {
        private static readonly object _executionLock = new object();
        private GetSelectedElementsEventHandler _handler => (GetSelectedElementsEventHandler)Handler;

        public override string CommandName => "get_selected_elements";

        public GetSelectedElementsCommand(UIApplication uiApp)
            : base(new GetSelectedElementsEventHandler(), uiApp)
        {
        }

        public override object Execute(JObject parameters, string requestId)
        {
            lock (_executionLock)
            {
                try
                {
                    // Parameters parsen
                    int? limit = parameters?["limit"]?.Value<int>();

                    // Stel het maximumaantal in
                    _handler.ResetCompletion();
                    _handler.Limit = limit;

                    // Extern event activeren en wachten op voltooiing
                    if (RaiseAndWaitForCompletion(15000))
                    {
                        if (_handler.ErrorMessage != null)
                            throw new Exception(_handler.ErrorMessage);
                        return _handler.ResultElements;
                    }
                    else
                    {
                        throw new TimeoutException("Time-out bij ophalen van geselecteerde elementen");
                    }
                }
                catch (Exception ex)
                {
                    throw new Exception($"Ophalen van geselecteerde elementen mislukt: {ex.Message}");
                }
            }
        }
    }
}
