using Autodesk.Revit.UI;
using RevitMCPCommandSet.Models.Common;
using RevitMCPSDK.API.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RevitMCPCommandSet.Services
{
    public class GetSelectedElementsEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        // Uitvoeringsresultaat
        public List<Models.Common.ElementInfo> ResultElements { get; private set; }

        // Object voor statussynchronisatie
        public bool TaskCompleted { get; private set; }
        private readonly ManualResetEvent _resetEvent = new ManualResetEvent(false);

        // Beperk het aantal geretourneerde elementen
        public int? Limit { get; set; }

        // Foutmelding bij mislukken (wordt teruggegeven in de response i.p.v. een dialoog te tonen)
        public string ErrorMessage { get; private set; }

        /// <summary>
        /// Zet de wachtstatus terug. Moet vóór het raisen van het externe event worden aangeroepen,
        /// zodat een snel afgeronde handler zijn signaal niet kwijtraakt.
        /// </summary>
        public void ResetCompletion()
        {
            TaskCompleted = false;
            ErrorMessage = null;
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
                var uiDoc = app.ActiveUIDocument;
                var doc = uiDoc.Document;

                // Haal de momenteel geselecteerde elementen op
                var selectedIds = uiDoc.Selection.GetElementIds();
                var selectedElements = selectedIds.Select(id => doc.GetElement(id)).ToList();

                // Pas de aantalslimiet toe
                if (Limit.HasValue && Limit.Value > 0)
                {
                    selectedElements = selectedElements.Take(Limit.Value).ToList();
                }

                // Converteer naar een lijst van ElementInfo
                ResultElements = selectedElements.Select(element => new ElementInfo
                {
#if REVIT2024_OR_GREATER
                    Id = element.Id.Value,
#else
                    Id = element.Id.IntegerValue,
#endif
                    UniqueId = element.UniqueId,
                    Name = element.Name,
                    Category = element.Category?.Name
                }).ToList();
            }
            catch (Exception ex)
            {
                ErrorMessage = "Ophalen van geselecteerde elementen mislukt: " + ex.Message;
                ResultElements = new List<Models.Common.ElementInfo>();
            }
            finally
            {
                TaskCompleted = true;
                _resetEvent.Set();
            }
        }

        public string GetName()
        {
            return "Geselecteerde elementen ophalen";
        }
    }
}
