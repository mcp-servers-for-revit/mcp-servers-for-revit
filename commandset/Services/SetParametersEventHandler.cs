using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using MF_Utilities;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Models.Common;
using RevitMCPCommandSet.Utils;
using RevitMCPSDK.API.Interfaces;
using System.Globalization;

namespace RevitMCPCommandSet.Services
{
    public class SetParametersEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        // Aan te passen elementen + parameters
        public List<ElementParameterInput> Elements { get; set; }

        // Uitvoeringsresultaat, per element
        public List<ElementParameterOutput> Result { get; private set; }

        // Object voor statussynchronisatie
        public bool TaskCompleted { get; private set; }
        private readonly ManualResetEvent _resetEvent = new ManualResetEvent(false);

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
            Result = new List<ElementParameterOutput>();
            try
            {
                var doc = app.ActiveUIDocument.Document;
                if (Elements == null || Elements.Count == 0)
                {
                    return;
                }

                // Elementen opzoeken in het huidige document; niet-gevonden ID's slaan we apart op zodat
                // ze ook in het resultaat terugkomen (ParameterHelper krijgt alleen bestaande elementen
                // te zien).
                var elementChanges = new List<ElementParameterChange>();
                var missingElementIds = new List<string>();

                foreach (var input in Elements)
                {
                    Element element = ResolveElement(doc, input.ElementId);
                    if (element == null)
                    {
                        missingElementIds.Add(input.ElementId);
                        continue;
                    }

                    List<ParameterChange> changes = input.Parameters
                        .Select(p => new ParameterChange(p.Name, ConvertJTokenToValue(p.Value)))
                        .ToList();
                    elementChanges.Add(new ElementParameterChange(element, changes));
                }

                if (elementChanges.Count > 0)
                {
                    List<ElementParameterChangeResult> changeResults = ParameterHelper.SetParameters(elementChanges);
                    foreach (var changeResult in changeResults)
                    {
                        Result.Add(new ElementParameterOutput
                        {
                            ElementId = changeResult.Element.Id.GetValue(),
                            Parameters = changeResult.ParameterResults.Select(pr => new ParameterResultOutput
                            {
                                ParameterName = pr.ParameterName,
                                Success = pr.Success,
                                Message = pr.Message
                            }).ToList()
                        });
                    }
                }

                foreach (var missingId in missingElementIds)
                {
                    Result.Add(new ElementParameterOutput
                    {
                        ElementId = 0,
                        Parameters = new List<ParameterResultOutput>
                        {
                            new ParameterResultOutput
                            {
                                ParameterName = null,
                                Success = false,
                                Message = $"Element met ID '{missingId}' niet gevonden"
                            }
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                ErrorMessage = "Aanpassen van parameters mislukt: " + ex.Message;
            }
            finally
            {
                TaskCompleted = true;
                _resetEvent.Set();
            }
        }

        private static Element ResolveElement(Document doc, string elementIdString)
        {
            if (string.IsNullOrEmpty(elementIdString) || !long.TryParse(elementIdString, out long idValue))
            {
                return null;
            }

            return doc.GetElement(new ElementId(idValue));
        }

        /// <summary>
        /// Zet een JSON-waarde om naar het object-type dat ParameterHelper.SetParameter(s) verwacht.
        /// Booleans kent ParameterHelper niet (Ja/Nee-parameters verwachten een integer 0/1). Getallen
        /// geven we als echte int/double door en niet als tekst: ParameterHelper parst strings met
        /// double.TryParse in de huidige cultuur, waardoor "2.5" op een Nederlandse Windows als 25 zou
        /// worden gelezen. ParameterHelper zet int/double zelf om naar het StorageType van de parameter
        /// (zie ParameterHelper.TrySetValue).
        /// </summary>
        private static object ConvertJTokenToValue(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null)
            {
                return null;
            }

            switch (token.Type)
            {
                case JTokenType.Boolean:
                    // Ja/Nee-parameters verwachten een integer 0/1
                    return token.Value<bool>() ? 1 : 0;

                case JTokenType.Integer:
                    // Newtonsoft levert hele getallen als long; ParameterHelper kent alleen int (en
                    // string). Past het niet in een int (groot ElementId), dan als tekst: voor hele
                    // getallen is het parsen cultuuronafhankelijk.
                    long longValue = token.Value<long>();
                    return longValue >= int.MinValue && longValue <= int.MaxValue
                        ? (object)(int)longValue
                        : longValue.ToString(CultureInfo.InvariantCulture);

                case JTokenType.Float:
                    return token.Value<double>();

                default:
                    return token.Value<string>();
            }
        }

        public string GetName()
        {
            return "Parameters aanpassen";
        }
    }
}
