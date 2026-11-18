using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Services;
using RevitMCPSDK.API.Base;

namespace RevitMCPCommandSet.Commands
{
    /// <summary>
    /// Haalt voor één of meerdere elementen op welke parameters ze hebben (zonder waardes), opgesplitst
    /// in instance- en type-parameters. Bedoeld om vooraf te bepalen welke parameters met get_parameters
    /// opgehaald of met set_parameters aangepast kunnen worden.
    /// </summary>
    public class GetAvailableParametersCommand : ExternalEventCommandBase
    {
        private GetAvailableParametersEventHandler _handler => (GetAvailableParametersEventHandler)Handler;

        public override string CommandName => "get_available_parameters";

        public GetAvailableParametersCommand(UIApplication uiApp)
            : base(new GetAvailableParametersEventHandler(), uiApp)
        {
        }

        private readonly object _executionLock = new object();

        public override object Execute(JObject parameters, string requestId)
        {
            lock (_executionLock)
            {
                try
                {
                    // Parameters parsen: altijd een lijst van element-ID's, ook voor een enkel element
                    var elementIds = parameters?["elementIds"]?.ToObject<List<string>>();
                    if (elementIds == null || elementIds.Count == 0)
                    {
                        throw new ArgumentException("Lijst met element-ID's mag niet leeg zijn");
                    }

                    _handler.ResetCompletion();
                    _handler.ElementIds = elementIds;

                    // Extern event activeren en wachten op voltooiing
                    if (RaiseAndWaitForCompletion(60000)) // 60 seconden time-out
                    {
                        if (_handler.ErrorMessage != null)
                            throw new Exception(_handler.ErrorMessage);
                        return _handler.Result;
                    }
                    else
                    {
                        throw new TimeoutException("Time-out bij ophalen van beschikbare parameters");
                    }
                }
                catch (Exception ex)
                {
                    throw new Exception($"Ophalen van beschikbare parameters mislukt: {ex.Message}");
                }
            }
        }
    }
}
