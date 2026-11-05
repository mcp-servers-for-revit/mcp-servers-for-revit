using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCPSDK.API.Interfaces;
using RevitMCPCommandSet.Models.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RevitMCPCommandSet.Services
{
    public class OperateElementEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        private UIApplication uiApp;
        private UIDocument uiDoc => uiApp.ActiveUIDocument;
        private Document doc => uiDoc.Document;
        private Autodesk.Revit.ApplicationServices.Application app => uiApp.Application;

        /// <summary>
        /// Wachtobject voor het event
        /// </summary>
        private readonly ManualResetEvent _resetEvent = new ManualResetEvent(false);
        /// <summary>
        /// Aanmaakgegevens (invoergegevens)
        /// </summary>
        public OperationSetting OperationData { get; private set; }
        /// <summary>
        /// Uitvoeringsresultaat (uitvoergegevens)
        /// </summary>
        public AIResult<string> Result { get; private set; }

        /// <summary>
        /// Stelt de parameters voor het aanmaken in
        /// </summary>
        public void SetParameters(OperationSetting data)
        {
            OperationData = data;
            _resetEvent.Reset();
        }
        public void Execute(UIApplication uiapp)
        {
            uiApp = uiapp;

            try
            {
                bool result = ExecuteElementOperation(uiDoc, OperationData);

                Result = new AIResult<string>
                {
                    Success = true,
                    Message = $"Bewerking succesvol uitgevoerd",
                };
            }
            catch (Exception ex)
            {
                Result = new AIResult<string>
                {
                    Success = false,
                    Message = $"Fout bij het bewerken van element: {ex.Message}",
                };
            }
            finally
            {
                _resetEvent.Set(); // Informeer de wachtende thread dat de bewerking is voltooid
            }
        }

        /// <summary>
        /// Wacht tot het aanmaken is voltooid
        /// </summary>
        /// <param name="timeoutMilliseconds">Time-out (in milliseconden)</param>
        /// <returns>Of de bewerking is voltooid vóór de time-out</returns>
        public bool WaitForCompletion(int timeoutMilliseconds = 10000)
        {
            return _resetEvent.WaitOne(timeoutMilliseconds);
        }

        /// <summary>
        /// IExternalEventHandler.GetName implementatie
        /// </summary>
        public string GetName()
        {
            return "Element bewerken";
        }

        /// <summary>
        /// Voert de bijbehorende elementbewerking uit op basis van de bewerkingsinstellingen
        /// </summary>
        /// <param name="uidoc">Het huidige UI-document</param>
        /// <param name="setting">Bewerkingsinstellingen</param>
        /// <returns>Of de bewerking is geslaagd</returns>
        public static bool ExecuteElementOperation(UIDocument uidoc, OperationSetting setting)
        {
            // Controleer of de parameters geldig zijn
            if (uidoc == null || uidoc.Document == null || setting == null || setting.ElementIds == null ||
                (setting.ElementIds.Count == 0 && setting.Action.ToLower() != "resetisolate"))
                throw new Exception("Ongeldige parameters: het document is leeg of er zijn geen elementen opgegeven om te bewerken");

            Document doc = uidoc.Document;

            // Zet de elementen-ID's van het type int om naar het type ElementId
            ICollection<ElementId> elementIds = setting.ElementIds.Select(id => new ElementId(id)).ToList();

            // Bepaal het type bewerking
            ElementOperationType action;
            if (!Enum.TryParse(setting.Action, true, out action))
            {
                throw new Exception($"Niet-ondersteund bewerkingstype: {setting.Action}");
            }

            // Voer een andere bewerking uit op basis van het bewerkingstype
            switch (action)
            {
                case ElementOperationType.Select:
                    // Selecteer element
                    uidoc.Selection.SetElementIds(elementIds);
                    return true;

                case ElementOperationType.SelectionBox:
                    // Maak een snijkader (section box) in de 3D-weergave

                    // Controleer of de huidige weergave een 3D-weergave is
                    View3D targetView;

                    if (doc.ActiveView is View3D)
                    {
                        // Als de huidige weergave een 3D-weergave is, maak het snijkader in de huidige weergave
                        targetView = doc.ActiveView as View3D;
                    }
                    else
                    {
                        // Als de huidige weergave geen 3D-weergave is, zoek de standaard 3D-weergave
                        FilteredElementCollector collector = new FilteredElementCollector(doc);
                        collector.OfClass(typeof(View3D));

                        // Probeer de standaard 3D-weergave of een andere beschikbare 3D-weergave te vinden
                        targetView = collector
                            .Cast<View3D>()
                            .FirstOrDefault(v => !v.IsTemplate && !v.IsLocked && (v.Name.Contains("{3D}") || v.Name.Contains("Default 3D")));

                        if (targetView == null)
                        {
                            // Als er geen geschikte 3D-weergave is gevonden, gooi een uitzondering
                            throw new Exception("Kan geen geschikte 3D-weergave vinden om een snijkader te maken");
                        }

                        // Activeer deze 3D-weergave
                        uidoc.ActiveView = targetView;
                    }

                    // Bereken de bounding box van de geselecteerde elementen
                    BoundingBoxXYZ boundingBox = null;

                    foreach (ElementId id in elementIds)
                    {
                        Element elem = doc.GetElement(id);
                        BoundingBoxXYZ elemBox = elem.get_BoundingBox(null);

                        if (elemBox != null)
                        {
                            if (boundingBox == null)
                            {
                                boundingBox = new BoundingBoxXYZ
                                {
                                    Min = new XYZ(elemBox.Min.X, elemBox.Min.Y, elemBox.Min.Z),
                                    Max = new XYZ(elemBox.Max.X, elemBox.Max.Y, elemBox.Max.Z)
                                };
                            }
                            else
                            {
                                // Breid de bounding box uit zodat het huidige element erin past
                                boundingBox.Min = new XYZ(
                                    Math.Min(boundingBox.Min.X, elemBox.Min.X),
                                    Math.Min(boundingBox.Min.Y, elemBox.Min.Y),
                                    Math.Min(boundingBox.Min.Z, elemBox.Min.Z));

                                boundingBox.Max = new XYZ(
                                    Math.Max(boundingBox.Max.X, elemBox.Max.X),
                                    Math.Max(boundingBox.Max.Y, elemBox.Max.Y),
                                    Math.Max(boundingBox.Max.Z, elemBox.Max.Z));
                            }
                        }
                    }

                    if (boundingBox == null)
                    {
                        throw new Exception("Kan geen bounding box maken voor de geselecteerde elementen");
                    }

                    // Vergroot de bounding box zodat deze iets groter is dan de elementen
                    double offset = 1.0; // Offset van 1 voet
                    boundingBox.Min = new XYZ(boundingBox.Min.X - offset, boundingBox.Min.Y - offset, boundingBox.Min.Z - offset);
                    boundingBox.Max = new XYZ(boundingBox.Max.X + offset, boundingBox.Max.Y + offset, boundingBox.Max.Z + offset);

                    // Schakel het snijkader in en stel het in binnen de 3D-weergave
                    using (Transaction trans = new Transaction(doc, "Snijkader maken"))
                    {
                        trans.Start();
                        targetView.IsSectionBoxActive = true;
                        targetView.SetSectionBox(boundingBox);
                        trans.Commit();
                    }

                    // Verplaats naar het midden van de weergave
                    uidoc.ShowElements(elementIds);
                    return true;

                case ElementOperationType.SetColor:
                    // Stel de elementen in op de opgegeven kleur
                    using (Transaction trans = new Transaction(doc, "Elementkleur instellen"))
                    {
                        trans.Start();
                        SetElementsColor(doc, elementIds, setting.ColorValue);
                        trans.Commit();
                    }
                    // Scroll naar deze elementen zodat ze zichtbaar zijn
                    uidoc.ShowElements(elementIds);
                    return true;


                case ElementOperationType.SetTransparency:
                    // Stel de transparantie van het element in de huidige weergave in
                    using (Transaction trans = new Transaction(doc, "Elementtransparantie instellen"))
                    {
                        trans.Start();

                        // Maak een object voor grafische overrideinstellingen
                        OverrideGraphicSettings overrideSettings = new OverrideGraphicSettings();

                        // Stel transparantie in (zorg dat de waarde tussen 0-100 ligt)
                        int transparencyValue = Math.Max(0, Math.Min(100, setting.TransparencyValue));

                        // Stel oppervlaktetransparantie in
                        overrideSettings.SetSurfaceTransparency(transparencyValue);

                        // Pas de transparantie-instelling toe op elk element
                        foreach (ElementId id in elementIds)
                        {
                            doc.ActiveView.SetElementOverrides(id, overrideSettings);
                        }

                        trans.Commit();
                    }
                    return true;

                case ElementOperationType.Delete:
                    // Verwijder elementen (transactie vereist)
                    using (Transaction trans = new Transaction(doc, "Elementen verwijderen"))
                    {
                        trans.Start();
                        doc.Delete(elementIds);
                        trans.Commit();
                    }
                    return true;

                case ElementOperationType.Hide:
                    // Verberg elementen (actieve weergave en transactie vereist)
                    using (Transaction trans = new Transaction(doc, "Elementen verbergen"))
                    {
                        trans.Start();
                        doc.ActiveView.HideElements(elementIds);
                        trans.Commit();
                    }
                    return true;

                case ElementOperationType.TempHide:
                    // Verberg elementen tijdelijk (actieve weergave en transactie vereist)
                    using (Transaction trans = new Transaction(doc, "Elementen tijdelijk verbergen"))
                    {
                        trans.Start();
                        doc.ActiveView.HideElementsTemporary(elementIds);
                        trans.Commit();
                    }
                    return true;

                case ElementOperationType.Isolate:
                    // Isoleer elementen (actieve weergave en transactie vereist)
                    using (Transaction trans = new Transaction(doc, "Elementen isoleren"))
                    {
                        trans.Start();
                        doc.ActiveView.IsolateElementsTemporary(elementIds);
                        trans.Commit();
                    }
                    return true;

                case ElementOperationType.Unhide:
                    // Maak elementen weer zichtbaar (actieve weergave en transactie vereist)
                    using (Transaction trans = new Transaction(doc, "Elementen weer zichtbaar maken"))
                    {
                        trans.Start();
                        doc.ActiveView.UnhideElements(elementIds);
                        trans.Commit();
                    }
                    return true;

                case ElementOperationType.ResetIsolate:
                    // Reset isolatie (actieve weergave en transactie vereist)
                    using (Transaction trans = new Transaction(doc, "Isolatie resetten"))
                    {
                        trans.Start();
                        doc.ActiveView.DisableTemporaryViewMode(TemporaryViewMode.TemporaryHideIsolate);
                        trans.Commit();
                    }
                    return true;

                default:
                    throw new Exception($"Niet-ondersteund bewerkingstype: {setting.Action}");
            }
        }

        /// <summary>
        /// Stelt de opgegeven elementen in de weergave in op de opgegeven kleur
        /// </summary>
        /// <param name="doc">Document</param>
        /// <param name="elementIds">Verzameling elementen-ID's waarvoor de kleur moet worden ingesteld</param>
        /// <param name="elementColor">Kleurwaarde (RGB-formaat)</param>
        private static void SetElementsColor(Document doc, ICollection<ElementId> elementIds, int[] elementColor)
        {
            // Controleer of de kleurenarray geldig is
            if (elementColor == null || elementColor.Length < 3)
            {
                elementColor = new int[] { 255, 0, 0 }; // Standaard rood
            }
            // Zorg dat de RGB-waarden tussen 0-255 liggen
            int r = Math.Max(0, Math.Min(255, elementColor[0]));
            int g = Math.Max(0, Math.Min(255, elementColor[1]));
            int b = Math.Max(0, Math.Min(255, elementColor[2]));
            // Maak een Revit-kleurobject - met conversie naar het type byte
            Color color = new Color((byte)r, (byte)g, (byte)b);
            // Maak grafische overrideinstellingen
            OverrideGraphicSettings overrideSettings = new OverrideGraphicSettings();
            // Stel de opgegeven kleur in
            overrideSettings.SetProjectionLineColor(color);
            overrideSettings.SetCutLineColor(color);
            overrideSettings.SetSurfaceForegroundPatternColor(color);
            overrideSettings.SetSurfaceBackgroundPatternColor(color);

            // Probeer het vulpatroon in te stellen
            try
            {
                // Probeer het standaard vulpatroon op te halen
                FilteredElementCollector patternCollector = new FilteredElementCollector(doc)
                    .OfClass(typeof(FillPatternElement));

                // Probeer eerst een dekkend (solid) vulpatroon te vinden
                FillPatternElement solidPattern = patternCollector
                    .Cast<FillPatternElement>()
                    .FirstOrDefault(p => p.GetFillPattern().IsSolidFill);

                if (solidPattern != null)
                {
                    overrideSettings.SetSurfaceForegroundPatternId(solidPattern.Id);
                    overrideSettings.SetSurfaceForegroundPatternVisible(true);
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Instellen van vulpatroon mislukt: {ex.Message}");
            }

            // Pas de overrideinstellingen toe op elk element
            foreach (ElementId id in elementIds)
            {
                doc.ActiveView.SetElementOverrides(id, overrideSettings);
            }
        }

    }
}
