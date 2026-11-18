using Autodesk.Revit.UI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using RevitMCPCommandSet.Models;
using RevitMCPCommandSet.Utils;
using RevitMCPSDK.API.Interfaces;

namespace RevitMCPCommandSet.Services
{
    /// <summary>
    /// Voert manage_materials uit op Revit's API-thread. Het eigenlijke werk zit in
    /// MaterialOperationsUtils; deze handler haalt alleen het actieve document op en bewaart het
    /// resultaat (camelCase, zonder lege velden) of de foutmelding.
    /// </summary>
    public class ManageMaterialsEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        private static readonly JsonSerializer ResultSerializer = JsonSerializer.Create(new JsonSerializerSettings
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver(),
            NullValueHandling = NullValueHandling.Ignore
        });

        // Aanvraag, door ManageMaterialsCommand gezet vóór het event wordt geraised
        public ManageMaterialsRequest Request { get; set; }

        // Uitvoeringsresultaat of foutmelding (precies één van de twee is gevuld na afloop)
        public JObject Result { get; set; }
        public string Error { get; set; }

        // Object voor statussynchronisatie
        public bool TaskCompleted { get; private set; }
        private readonly ManualResetEvent _resetEvent = new ManualResetEvent(false);

        /// <summary>
        /// Zet de wachtstatus en het vorige resultaat terug. Moet vóór het raisen van het externe event
        /// worden aangeroepen, zodat een snel afgeronde handler zijn signaal niet kwijtraakt.
        /// </summary>
        public void ResetCompletion()
        {
            TaskCompleted = false;
            Result = null;
            Error = null;
            _resetEvent.Reset();
        }

        // Implementatie van de IWaitableExternalEventHandler-interface
        public bool WaitForCompletion(int timeoutMilliseconds = 10000)
        {
            return _resetEvent.WaitOne(timeoutMilliseconds);
        }

        public void Execute(UIApplication app)
        {
            try
            {
                var doc = app.ActiveUIDocument?.Document;
                if (doc == null)
                {
                    Error = "Geen actief document beschikbaar";
                    return;
                }

                ManageMaterialsResult result = MaterialOperationsUtils.Execute(doc, Request);
                Result = JObject.FromObject(result, ResultSerializer);
            }
            catch (Exception ex)
            {
                Error = ex.Message;
            }
            finally
            {
                TaskCompleted = true;
                _resetEvent.Set();
            }
        }

        public string GetName()
        {
            return "Materialen beheren";
        }
    }
}
