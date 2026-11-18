using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Models.Common;
using RevitMCPCommandSet.Services;
using RevitMCPSDK.API.Base;

namespace RevitMCPCommandSet.Commands
{
    /// <summary>
    /// Haalt de waardes van specifieke parameters op voor één of meerdere elementen. De op te halen
    /// parameternamen kunnen per element opgegeven worden, of eenmalig (parameterNames) voor alle
    /// elementen zonder eigen lijst.
    /// </summary>
    public class GetParametersCommand : ExternalEventCommandBase
    {
        private GetParametersEventHandler _handler => (GetParametersEventHandler)Handler;

        public override string CommandName => "get_parameters";

        public GetParametersCommand(UIApplication uiApp)
            : base(new GetParametersEventHandler(), uiApp)
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
                    var elements = parameters?["elements"]?.ToObject<List<ElementParameterRequestInput>>();
                    if (elements == null || elements.Count == 0)
                    {
                        throw new ArgumentException("Lijst met elementen mag niet leeg zijn");
                    }

                    // Elementen zonder eigen parameternamen krijgen de algemene lijst
                    var commonParameterNames = parameters?["parameterNames"]?.ToObject<List<string>>();
                    foreach (var element in elements)
                    {
                        if (element.ParameterNames == null || element.ParameterNames.Count == 0)
                        {
                            element.ParameterNames = commonParameterNames;
                        }

                        if (element.ParameterNames == null || element.ParameterNames.Count == 0)
                        {
                            throw new ArgumentException(
                                $"Geen parameternamen opgegeven voor element '{element.ElementId}': geef parameterNames per element of algemeen op");
                        }
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
                        throw new TimeoutException("Time-out bij ophalen van parameters");
                    }
                }
                catch (Exception ex)
                {
                    throw new Exception($"Ophalen van parameters mislukt: {ex.Message}");
                }
            }
        }
    }
}
