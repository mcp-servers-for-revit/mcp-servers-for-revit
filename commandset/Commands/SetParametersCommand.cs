using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Models.Common;
using RevitMCPCommandSet.Services;
using RevitMCPSDK.API.Base;

namespace RevitMCPCommandSet.Commands
{
    /// <summary>
    /// Past parameterwaarden aan op één of meerdere elementen tegelijk (elk element met zijn eigen set
    /// parameters).
    /// </summary>
    public class SetParametersCommand : ExternalEventCommandBase
    {
        private SetParametersEventHandler _handler => (SetParametersEventHandler)Handler;

        public override string CommandName => "set_parameters";

        public SetParametersCommand(UIApplication uiApp)
            : base(new SetParametersEventHandler(), uiApp)
        {
        }

        private readonly object _executionLock = new object();

        public override object Execute(JObject parameters, string requestId)
        {
            lock (_executionLock)
            {
                try
                {
                    // Parameters parsen: altijd een lijst van elementen, ook voor een enkel element
                    var elements = parameters?["elements"]?.ToObject<List<ElementParameterInput>>();
                    if (elements == null || elements.Count == 0)
                    {
                        throw new ArgumentException("Lijst met elementen mag niet leeg zijn");
                    }

                    _handler.ResetCompletion();
                    _handler.Elements = elements;

                    // Extern event activeren en wachten op voltooiing
                    if (RaiseAndWaitForCompletion(60000)) // 60 seconden time-out
                    {
                        if (_handler.ErrorMessage != null)
                            throw new Exception(_handler.ErrorMessage);
                        return _handler.Result;
                    }
                    else
                    {
                        throw new TimeoutException("Time-out bij aanpassen van parameters");
                    }
                }
                catch (Exception ex)
                {
                    throw new Exception($"Aanpassen van parameters mislukt: {ex.Message}");
                }
            }
        }
    }
}
