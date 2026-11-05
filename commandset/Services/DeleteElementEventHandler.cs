using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCPSDK.API.Interfaces;

namespace RevitMCPCommandSet.Services
{
    public class DeleteElementEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        // Uitvoeringsresultaat
        public bool IsSuccess { get; private set; }

        // Aantal succesvol verwijderde elementen
        public int DeletedCount { get; private set; }
        // Foutmelding bij mislukken (wordt teruggegeven in de response i.p.v. een dialoog te tonen)
        public string ErrorMessage { get; private set; }
        // Waarschuwing voor ongeldige of niet-bestaande ID's
        public string Warning { get; private set; }
        // Object voor statussynchronisatie
        public bool TaskCompleted { get; private set; }
        private readonly ManualResetEvent _resetEvent = new ManualResetEvent(false);
        // Array met de te verwijderen elementen-ID's
        public string[] ElementIds { get; set; }

        /// <summary>
        /// Zet de wachtstatus terug. Moet vóór het raisen van het externe event worden aangeroepen,
        /// zodat een snel afgeronde handler zijn signaal niet kwijtraakt.
        /// </summary>
        public void ResetCompletion()
        {
            TaskCompleted = false;
            ErrorMessage = null;
            Warning = null;
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
                var doc = app.ActiveUIDocument.Document;
                DeletedCount = 0;
                if (ElementIds == null || ElementIds.Length == 0)
                {
                    IsSuccess = false;
                    ErrorMessage = "Lijst met element-ID's is leeg";
                    return;
                }
                // Maak een verzameling van te verwijderen elementen-ID's
                List<ElementId> elementIdsToDelete = new List<ElementId>();
                List<string> invalidIds = new List<string>();
                foreach (var idStr in ElementIds)
                {
                    if (int.TryParse(idStr, out int elementIdValue))
                    {
                        var elementId = new ElementId(elementIdValue);
                        // Controleer of het element bestaat
                        if (doc.GetElement(elementId) != null)
                        {
                            elementIdsToDelete.Add(elementId);
                        }
                        else
                        {
                            invalidIds.Add(idStr);
                        }
                    }
                    else
                    {
                        invalidIds.Add(idStr);
                    }
                }
                if (invalidIds.Count > 0)
                {
                    Warning = $"De volgende ID's zijn ongeldig of het element bestaat niet: {string.Join(", ", invalidIds)}";
                }
                // Voer de verwijdering uit als er te verwijderen elementen zijn
                if (elementIdsToDelete.Count > 0)
                {
                    using (var transaction = new Transaction(doc, "Delete Elements"))
                    {
                        transaction.Start();

                        // Verwijder elementen in bulk
                        ICollection<ElementId> deletedIds = doc.Delete(elementIdsToDelete);
                        DeletedCount = deletedIds.Count;

                        transaction.Commit();
                    }
                    IsSuccess = true;
                }
                else
                {
                    ErrorMessage = "Er zijn geen geldige elementen om te verwijderen"
                        + (Warning != null ? $" ({Warning})" : "");
                    IsSuccess = false;
                }
            }
            catch (Exception ex)
            {
                ErrorMessage = "Verwijderen van elementen mislukt: " + ex.Message;
                IsSuccess = false;
            }
            finally
            {
                TaskCompleted = true;
                _resetEvent.Set();
            }
        }
        public string GetName()
        {
            return "Element verwijderen";
        }
    }
}
