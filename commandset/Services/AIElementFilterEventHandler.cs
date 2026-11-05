using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using RevitMCPSDK.API.Interfaces;
using RevitMCPCommandSet.Models.Common;
using RevitMCPCommandSet.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Xml;

namespace RevitMCPCommandSet.Services
{
    public class AIElementFilterEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
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
        /// Aangemaakte gegevens (invoergegevens)
        /// </summary>
        public FilterSetting FilterSetting { get; private set; }
        /// <summary>
        /// Uitvoeringsresultaat (uitvoergegevens)
        /// </summary>
        public AIResult<List<object>> Result { get; private set; }

        /// <summary>
        /// Stelt de parameters voor het aanmaken in
        /// </summary>
        public void SetParameters(FilterSetting data)
        {
            FilterSetting = data;
            _resetEvent.Reset();
        }
        public void Execute(UIApplication uiapp)
        {
            uiApp = uiapp;

            try
            {
                var elementInfoList = new List<object>();
                // Controleer of de filterinstellingen geldig zijn
                if (!FilterSetting.Validate(out string errorMessage))
                    throw new Exception(errorMessage);
                // Haal de Id's op van elementen die aan de opgegeven criteria voldoen
                var elementList = GetFilteredElements(doc, FilterSetting);
                if (elementList == null || !elementList.Any())
                    throw new Exception("Geen elementen gevonden die aan de opgegeven criteria voldoen in het project, controleer de filterinstellingen");
                // Limiet voor het maximaal aantal elementen van het filter, alleen als expliciet opgegeven
                string message = "";
                if (FilterSetting.MaxElements.HasValue && FilterSetting.MaxElements.Value > 0)
                {
                    int maxElements = FilterSetting.MaxElements.Value;
                    if (elementList.Count > maxElements)
                    {
                        int totalCount = elementList.Count;
                        elementList = elementList.Take(maxElements).ToList();
                        message = $". Bovendien voldoen er in totaal {totalCount} elementen aan de filtercriteria, alleen de eerste {maxElements} worden weergegeven";
                    }
                }

                // Tel desgewenst per type hoe vaak het in het project is geplaatst
                Dictionary<ElementId, int> instanceCounts = null;
                if (FilterSetting.IncludeInstanceCount && FilterSetting.IncludeTypes)
                    instanceCounts = GetInstanceCountsByType(doc, FilterSetting);

                // Haal de informatie op van de elementen met de opgegeven Id's (standaard alleen basisinformatie)
                elementInfoList = FilterSetting.IncludeDetails
                    ? GetElementFullInfo(doc, elementList, instanceCounts)
                    : GetElementBaseInfo(elementList, instanceCounts);

                Result = new AIResult<List<object>>
                {
                    Success = true,
                    Message = $"Succesvol {elementInfoList.Count} elementgegevens opgehaald, gedetailleerde informatie is opgeslagen in de Response-eigenschap"+ message,
                    Response = elementInfoList,
                };
            }
            catch (Exception ex)
            {
                Result = new AIResult<List<object>>
                {
                    Success = false,
                    Message = $"Fout bij het ophalen van elementinformatie: {ex.Message}",
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
            return "Elementinformatie ophalen";
        }

        /// <summary>
        /// Haalt elementen op uit het Revit-document die voldoen aan de filterinstellingen, ondersteunt combinatie van meerdere filtercriteria
        /// </summary>
        /// <param name="doc">Revit-document</param>
        /// <param name="settings">Filterinstellingen</param>
        /// <returns>Verzameling van elementen die aan alle filtercriteria voldoen</returns>
        public static IList<Element> GetFilteredElements(Document doc, FilterSetting settings)
        {
            if (doc == null)
                throw new ArgumentNullException(nameof(doc));
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));
            // Valideer de filterinstellingen
            if (!settings.Validate(out string errorMessage))
            {
                System.Diagnostics.Trace.WriteLine($"Filterinstellingen ongeldig: {errorMessage}");
                return new List<Element>();
            }
            // Registreer de toegepaste filtercriteria
            List<string> appliedFilters = new List<string>();
            List<Element> result = new List<Element>();
            // Als zowel typen als instanties zijn inbegrepen, moet apart gefilterd worden en de resultaten samengevoegd
            if (settings.IncludeTypes && settings.IncludeInstances)
            {
                // Verzamel typeelementen
                result.AddRange(GetElementsByKind(doc, settings, true, appliedFilters));

                // Verzamel instantie-elementen
                result.AddRange(GetElementsByKind(doc, settings, false, appliedFilters));
            }
            else if (settings.IncludeInstances)
            {
                // Verzamel alleen instantie-elementen
                result = GetElementsByKind(doc, settings, false, appliedFilters);
            }
            else if (settings.IncludeTypes)
            {
                // Verzamel alleen typeelementen
                result = GetElementsByKind(doc, settings, true, appliedFilters);
            }

            // Geef informatie over de toegepaste filters weer
            if (appliedFilters.Count > 0)
            {
                System.Diagnostics.Trace.WriteLine($"Er zijn {appliedFilters.Count} filtercriteria toegepast: {string.Join(", ", appliedFilters)}");
                System.Diagnostics.Trace.WriteLine($"Eindresultaat van de filtering: in totaal {result.Count} elementen gevonden");
            }
            return result;

        }

        /// <summary>
        /// Haalt elementen op die aan de filtercriteria voldoen, op basis van elementsoort (type of instantie)
        /// </summary>
        private static List<Element> GetElementsByKind(Document doc, FilterSetting settings, bool isElementType, List<string> appliedFilters)
        {
            // Maak de basis FilteredElementCollector aan
            FilteredElementCollector collector;
            // Controleer of alleen elementen zichtbaar in de huidige weergave gefilterd moeten worden (alleen van toepassing op instantie-elementen)
            if (!isElementType && settings.FilterVisibleInCurrentView && doc.ActiveView != null)
            {
                collector = new FilteredElementCollector(doc, doc.ActiveView.Id);
                appliedFilters.Add("Zichtbare elementen in huidige weergave");
            }
            else
            {
                collector = new FilteredElementCollector(doc);
            }
            // Filter op basis van elementsoort
            if (isElementType)
            {
                collector = collector.WhereElementIsElementType();
                appliedFilters.Add("Alleen elementtypen");
            }
            else
            {
                collector = collector.WhereElementIsNotElementType();
                appliedFilters.Add("Alleen elementinstanties");
            }
            // Maak de filterlijst aan
            List<ElementFilter> filters = new List<ElementFilter>();
            // 1. Categoriefilter
            if (!string.IsNullOrWhiteSpace(settings.FilterCategory))
            {
                BuiltInCategory category;
                if (!Enum.TryParse(settings.FilterCategory, true, out category))
                {
                    throw new ArgumentException($"Kan '{settings.FilterCategory}' niet omzetten naar een geldige Revit-categorie.");
                }
                ElementCategoryFilter categoryFilter = new ElementCategoryFilter(category);
                filters.Add(categoryFilter);
                appliedFilters.Add($"Categorie: {settings.FilterCategory}");
            }
            // 2. Elementtypefilter
            if (!string.IsNullOrWhiteSpace(settings.FilterElementType))
            {

                Type elementType = null;
                // Probeer verschillende mogelijke vormen van de typenaam te herkennen
                string[] possibleTypeNames = new string[]
                {
                    settings.FilterElementType,                                    // Oorspronkelijke invoer
                    $"Autodesk.Revit.DB.{settings.FilterElementType}, RevitAPI",  // Revit API-namespace
                    $"{settings.FilterElementType}, RevitAPI"                      // Volledig gekwalificeerd met assembly
                };
                foreach (string typeName in possibleTypeNames)
                {
                    elementType = Type.GetType(typeName);
                    if (elementType != null)
                        break;
                }
                if (elementType != null)
                {
                    ElementClassFilter classFilter = new ElementClassFilter(elementType);
                    filters.Add(classFilter);
                    appliedFilters.Add($"Elementtype: {elementType.Name}");
                }
                else
                {
                    throw new Exception($"Waarschuwing: kan het type '{settings.FilterElementType}' niet vinden");
                }
            }
            // 3. Familiesymboolfilter (alleen van toepassing op elementinstanties)
            if (!isElementType && settings.FilterFamilySymbolId > 0)
            {
                ElementId symbolId = new ElementId(settings.FilterFamilySymbolId);
                // Controleer of het element bestaat en een familietype is
                Element symbolElement = doc.GetElement(symbolId);
                if (symbolElement != null && symbolElement is FamilySymbol)
                {
                    FamilyInstanceFilter familyFilter = new FamilyInstanceFilter(doc, symbolId);
                    filters.Add(familyFilter);
                    // Voeg gedetailleerdere logging van familie-informatie toe
                    FamilySymbol symbol = symbolElement as FamilySymbol;
                    string familyName = symbol.Family?.Name ?? "Onbekende familie";
                    string symbolName = symbol.Name ?? "Onbekend type";
                    appliedFilters.Add($"Familietype: {familyName} - {symbolName} (ID: {settings.FilterFamilySymbolId})");
                }
                else
                {
                    string elementType = symbolElement != null ? symbolElement.GetType().Name : "bestaat niet";
                    System.Diagnostics.Trace.WriteLine($"Waarschuwing: het element met ID {settings.FilterFamilySymbolId} {(symbolElement == null ? "bestaat niet" : "is geen geldig FamilySymbol")} (werkelijk type: {elementType})");
                }
            }
            // 4. Ruimtelijk bereikfilter
            if (settings.BoundingBoxMin != null && settings.BoundingBoxMax != null)
            {
                // Omzetten naar Revit XYZ-coördinaten (millimeters naar interne eenheden)
                XYZ minXYZ = JZPoint.ToXYZ(settings.BoundingBoxMin);
                XYZ maxXYZ = JZPoint.ToXYZ(settings.BoundingBoxMax);
                // Maak het Outline-object voor het ruimtelijk bereik aan
                Outline outline = new Outline(minXYZ, maxXYZ);
                // Maak het intersectiefilter aan
                BoundingBoxIntersectsFilter boundingBoxFilter = new BoundingBoxIntersectsFilter(outline);
                filters.Add(boundingBoxFilter);
                appliedFilters.Add($"Ruimtelijk bereikfilter: Min({settings.BoundingBoxMin.X:F2}, {settings.BoundingBoxMin.Y:F2}, {settings.BoundingBoxMin.Z:F2}), " +
                                  $"Max({settings.BoundingBoxMax.X:F2}, {settings.BoundingBoxMax.Y:F2}, {settings.BoundingBoxMax.Z:F2}) mm");
            }
            // Pas het gecombineerde filter toe
            if (filters.Count > 0)
            {
                ElementFilter combinedFilter = filters.Count == 1
                    ? filters[0]
                    : new LogicalAndFilter(filters);
                collector = collector.WherePasses(combinedFilter);
                if (filters.Count > 1)
                {
                    System.Diagnostics.Trace.WriteLine($"Er is een gecombineerd filter met {filters.Count} filtercriteria toegepast (logische AND-relatie)");
                }
            }
            return collector.ToElements().ToList();
        }

        /// <summary>
        /// Telt per type-Id hoeveel instanties er in het project zijn geplaatst. Is er een categorie opgegeven,
        /// dan worden alleen instanties van die categorie geteld (types en hun instanties delen de categorie).
        /// Zichtbaarheids- en ruimtelijke bereikfilters worden bewust niet toegepast: het gaat om gebruik in het hele project.
        /// </summary>
        public static Dictionary<ElementId, int> GetInstanceCountsByType(Document doc, FilterSetting settings)
        {
            var counts = new Dictionary<ElementId, int>();
            FilteredElementCollector collector = new FilteredElementCollector(doc).WhereElementIsNotElementType();
            if (!string.IsNullOrWhiteSpace(settings.FilterCategory) &&
                Enum.TryParse(settings.FilterCategory, true, out BuiltInCategory category))
            {
                collector = collector.OfCategory(category);
            }

            foreach (Element instance in collector)
            {
                ElementId typeId = instance.GetTypeId();
                if (typeId == null || typeId == ElementId.InvalidElementId)
                    continue;
                counts.TryGetValue(typeId, out int current);
                counts[typeId] = current + 1;
            }
            return counts;
        }

        /// <summary>
        /// Geeft het aantal geplaatste instanties van een type terug, of null als er niet geteld wordt
        /// of het element geen type is
        /// </summary>
        private static int? GetInstanceCount(Element element, IDictionary<ElementId, int> instanceCounts)
        {
            if (instanceCounts == null || !(element is ElementType))
                return null;
            return instanceCounts.TryGetValue(element.Id, out int count) ? count : 0;
        }

        /// <summary>
        /// Haalt per element alleen de basisinformatie op (standaard, lichte response)
        /// </summary>
        public static List<object> GetElementBaseInfo(IList<Element> elementCollector, IDictionary<ElementId, int> instanceCounts = null)
        {
            List<object> infoList = new List<object>();
            foreach (var element in elementCollector)
            {
                var info = CreateBaseInfo(element);
                if (info != null)
                {
                    info.InstanceCount = GetInstanceCount(element, instanceCounts);
                    infoList.Add(info);
                }
            }
            return infoList;
        }

        /// <summary>
        /// Maakt de lichte basisinformatie voor één element aan (Id, naam, familienaam, categorie).
        /// Is het element zelf een familie, dan is de naam al de familienaam en wordt FamilyName weggelaten.
        /// </summary>
        public static ElementBaseInfo CreateBaseInfo(Element element)
        {
            try
            {
                if (element == null)
                    return null;

                // Een Family heeft zelf geen Category, alleen een FamilyCategory
                Category category = element.Category ?? (element as Family)?.FamilyCategory;

                string familyName = null;
                if (element is ElementType elementType)
                    familyName = elementType.FamilyName;
                else if (!(element is Family))
                    familyName = element.get_Parameter(BuiltInParameter.ELEM_FAMILY_PARAM)?.AsValueString();

                return new ElementBaseInfo
                {
                    Id = element.Id.GetIntValue(),
                    Name = element.Name,
                    FamilyName = string.IsNullOrEmpty(familyName) ? null : familyName,
                    Category = category?.Name,
                    BuiltInCategory = category != null ?
                        Enum.GetName(typeof(BuiltInCategory), category.Id.GetIntValue()) : null
                };
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"Fout bij het aanmaken van de basisinformatie van het element: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Haalt per element de uitgebreide informatie op, afhankelijk van het soort element
        /// </summary>
        public static List<object> GetElementFullInfo(Document doc, IList<Element> elementCollector, IDictionary<ElementId, int> instanceCounts = null)
        {
            List<object> infoList = new List<object>();

            // Elementen ophalen en verwerken
            foreach (var element in elementCollector)
            {
                // Haal elementtype-informatie op. Moet vóór de controle op fysieke modelelementen staan:
                // ook types (bijv. WallType) hebben een categorie met HasMaterialQuantities
                if (element is ElementType elementType)
                {
                    var info = CreateTypeFullInfo(doc, elementType);
                    if (info != null)
                    {
                        info.InstanceCount = GetInstanceCount(elementType, instanceCounts);
                        infoList.Add(info);
                    }
                }
                // Bepaal of het een fysiek modelelement is
                // Haal elementinstantie-informatie op
                else if (element?.Category?.HasMaterialQuantities ?? false)
                {
                    var info = CreateElementFullInfo(doc, element);
                    if (info != null)
                    {
                        infoList.Add(info);
                    }
                }
                // 3. Ruimtelijke positioneringselementen (hoge frequentie)
                else if (element is Level || element is Grid)
                {
                    var info = CreatePositioningElementInfo(doc, element);
                    if (info != null)
                    {
                        infoList.Add(info);
                    }
                }
                // 4. Ruimte-elementen (middelhoge frequentie)
                else if (element is SpatialElement) // Room, Area, enz.
                {
                    var info = CreateSpatialElementInfo(doc, element);
                    if (info != null)
                    {
                        infoList.Add(info);
                    }
                }
                // 5. Weergave-elementen (hoge frequentie)
                else if (element is View)
                {
                    var info = CreateViewInfo(doc, element);
                    if (info != null)
                    {
                        infoList.Add(info);
                    }
                }
                // 6. Annotatie-elementen (gemiddelde frequentie)
                else if (element is TextNote || element is Dimension ||
                         element is IndependentTag || element is AnnotationSymbol ||
                         element is SpotDimension)
                {
                    var info = CreateAnnotationInfo(doc, element);
                    if (info != null)
                    {
                        infoList.Add(info);
                    }
                }
                // 7. Groepen en links verwerken
                else if (element is Group || element is RevitLinkInstance)
                {
                    var info = CreateGroupOrLinkInfo(doc, element);
                    if (info != null)
                    {
                        infoList.Add(info);
                    }
                }
                // 8. Haal basisinformatie van het element op (fallback)
                else
                {
                    var info = CreateElementBasicInfo(doc, element);
                    if (info != null)
                    {
                        infoList.Add(info);
                    }
                }
            }

            return infoList;
        }

        /// <summary>
        /// Maakt een volledig ElementInfo-object voor één element aan
        /// </summary>
        public static ElementInstanceInfo CreateElementFullInfo(Document doc, Element element)
        {
            try
            {
                if (element?.Category == null)
                    return null;

                ElementInstanceInfo elementInfo = new ElementInstanceInfo();        // Aangepaste klasse voor het opslaan van volledige elementinformatie
                // ID
                elementInfo.Id = element.Id.GetIntValue();
                // UniqueId
                elementInfo.UniqueId = element.UniqueId;
                // Typenaam
                elementInfo.Name = element.Name;
                // Familienaam
                elementInfo.FamilyName = element?.get_Parameter(BuiltInParameter.ELEM_FAMILY_PARAM)?.AsValueString();
                // Categorie
                elementInfo.Category = element.Category.Name;
                // Ingebouwde categorie
                elementInfo.BuiltInCategory = Enum.GetName(typeof(BuiltInCategory), element.Category.Id.GetIntValue());
                // Type-Id
                elementInfo.TypeId = element.GetTypeId().GetIntValue();
                // Bijbehorende ruimte-Id
                if (element is FamilyInstance instance)
                    elementInfo.RoomId = instance.Room?.Id.GetIntValue() ?? -1;
                // Peil
                elementInfo.Level = GetElementLevel(doc, element);
                // Maximale bounding box
                BoundingBoxInfo boundingBoxInfo = new BoundingBoxInfo();
                elementInfo.BoundingBox = GetBoundingBoxInfo(element);
                // Parameters
                //elementInfo.Parameters = GetDimensionParameters(element);
                ParameterInfo thicknessParam = GetThicknessInfo(element);      // Dikteparameter
                if (thicknessParam != null)
                {
                    elementInfo.Parameters.Add(thicknessParam);
                }
                ParameterInfo heightParam = GetBoundingBoxHeight(elementInfo.BoundingBox);      // Hoogteparameter
                if (heightParam != null)
                {
                    elementInfo.Parameters.Add(heightParam);
                }

                return elementInfo;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Maakt een volledig TypeFullInfo-object voor één type aan
        /// </summary>
        /// <param name="doc"></param>
        /// <param name="elementType"></param>
        /// <returns></returns>
        public static ElementTypeInfo CreateTypeFullInfo(Document doc, ElementType elementType)
        {
            ElementTypeInfo typeInfo = new ElementTypeInfo();
            // Id
            typeInfo.Id = elementType.Id.GetIntValue();
            // UniqueId
            typeInfo.UniqueId = elementType.UniqueId;
            // Typenaam
            typeInfo.Name = elementType.Name;
            // Familienaam
            typeInfo.FamilyName = elementType.FamilyName;
            // Categorie
            typeInfo.Category = elementType.Category?.Name;
            // Ingebouwde categorie
            typeInfo.BuiltInCategory = elementType.Category != null ?
                Enum.GetName(typeof(BuiltInCategory), elementType.Category.Id.GetIntValue()) : null;
            // Parameterwoordenboek
            typeInfo.Parameters = GetDimensionParameters(elementType);
            ParameterInfo thicknessParam = GetThicknessInfo(elementType);      // Dikteparameter
            if (thicknessParam != null)
            {
                typeInfo.Parameters.Add(thicknessParam);
            }
            return typeInfo;
        }

        /// <summary>
        /// Maakt informatie voor ruimtelijke positioneringselementen aan
        /// </summary>
        public static PositioningElementInfo CreatePositioningElementInfo(Document doc, Element element)
        {
            try
            {
                if (element == null)
                    return null;
                PositioningElementInfo info = new PositioningElementInfo
                {
                    Id = element.Id.GetIntValue(),
                    UniqueId = element.UniqueId,
                    Name = element.Name,
                    FamilyName = element?.get_Parameter(BuiltInParameter.ELEM_FAMILY_PARAM)?.AsValueString(),
                    Category = element.Category?.Name,
                    BuiltInCategory = element.Category != null ?
                        Enum.GetName(typeof(BuiltInCategory), element.Category.Id.GetIntValue()) : null,
                    ElementClass = element.GetType().Name,
                    BoundingBox = GetBoundingBoxInfo(element)
                };

                // Verwerk peil
                if (element is Level level)
                {
                    // Omzetten naar mm
                    info.Elevation = level.Elevation * 304.8;
                }
                // Verwerk grid (assenstelsel)
                else if (element is Grid grid)
                {
                    Curve curve = grid.Curve;
                    if (curve != null)
                    {
                        XYZ start = curve.GetEndPoint(0);
                        XYZ end = curve.GetEndPoint(1);
                        // Maak JZLine aan (omgezet naar mm)
                        info.GridLine = new JZLine(
                            start.X * 304.8, start.Y * 304.8, start.Z * 304.8,
                            end.X * 304.8, end.Y * 304.8, end.Z * 304.8);
                    }
                }

                // Haal peilinformatie op
                info.Level = GetElementLevel(doc, element);

                return info;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"Fout bij het aanmaken van informatie voor ruimtelijke positioneringselementen: {ex.Message}");
                return null;
            }
        }
        /// <summary>
        /// Maakt informatie voor ruimte-elementen aan
        /// </summary>
        public static SpatialElementInfo CreateSpatialElementInfo(Document doc, Element element)
        {
            try
            {
                if (element == null || !(element is SpatialElement))
                    return null;
                SpatialElement spatialElement = element as SpatialElement;
                SpatialElementInfo info = new SpatialElementInfo
                {
                    Id = element.Id.GetIntValue(),
                    UniqueId = element.UniqueId,
                    Name = element.Name,
                    FamilyName = element?.get_Parameter(BuiltInParameter.ELEM_FAMILY_PARAM)?.AsValueString(),
                    Category = element.Category?.Name,
                    BuiltInCategory = element.Category != null ?
                        Enum.GetName(typeof(BuiltInCategory), element.Category.Id.GetIntValue()) : null,
                    ElementClass = element.GetType().Name,
                    BoundingBox = GetBoundingBoxInfo(element)
                };

                // Haal het nummer van de ruimte of zone op
                if (element is Room room)
                {
                    info.Number = room.Number;
                    // Omzetten naar mm³
                    info.Volume = room.Volume * Math.Pow(304.8, 3);
                }
                else if (element is Area area)
                {
                    info.Number = area.Number;
                }

                // Haal oppervlakte op
                Parameter areaParam = element.get_Parameter(BuiltInParameter.ROOM_AREA);
                if (areaParam != null && areaParam.HasValue)
                {
                    // Omzetten naar mm²
                    info.Area = areaParam.AsDouble() * Math.Pow(304.8, 2);
                }

                // Haal omtrek op
                Parameter perimeterParam = element.get_Parameter(BuiltInParameter.ROOM_PERIMETER);
                if (perimeterParam != null && perimeterParam.HasValue)
                {
                    // Omzetten naar mm
                    info.Perimeter = perimeterParam.AsDouble() * 304.8;
                }

                // Haal peil op
                info.Level = GetElementLevel(doc, element);

                return info;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"Fout bij het aanmaken van ruimte-elementinformatie: {ex.Message}");
                return null;
            }
        }
        /// <summary>
        /// Maakt informatie voor weergave-elementen aan
        /// </summary>
        public static ViewInfo CreateViewInfo(Document doc, Element element)
        {
            try
            {
                if (element == null || !(element is View))
                    return null;
                View view = element as View;

                ViewInfo info = new ViewInfo
                {
                    Id = element.Id.GetIntValue(),
                    UniqueId = element.UniqueId,
                    Name = element.Name,
                    FamilyName = element?.get_Parameter(BuiltInParameter.ELEM_FAMILY_PARAM)?.AsValueString(),
                    Category = element.Category?.Name,
                    BuiltInCategory = element.Category != null ?
                        Enum.GetName(typeof(BuiltInCategory), element.Category.Id.GetIntValue()) : null,
                    ElementClass = element.GetType().Name,
                    ViewType = view.ViewType.ToString(),
                    Scale = view.Scale,
                    IsTemplate = view.IsTemplate,
                    DetailLevel = view.DetailLevel.ToString(),
                    BoundingBox = GetBoundingBoxInfo(element)
                };

                // Haal het peil op dat aan de weergave is gekoppeld
                if (view is ViewPlan viewPlan && viewPlan.GenLevel != null)
                {
                    Level level = viewPlan.GenLevel;
                    info.AssociatedLevel = new LevelInfo
                    {
                        Id = level.Id.GetIntValue(),
                        Name = level.Name,
                        Height = level.Elevation * 304.8 // Omzetten naar mm
                    };
                }

                // Bepaal of de weergave geopend en actief is
                UIDocument uidoc = new UIDocument(doc);

                // Haal alle geopende weergaven op
                IList<UIView> openViews = uidoc.GetOpenUIViews();

                foreach (UIView uiView in openViews)
                {
                    // Controleer of de weergave geopend is
                    if (uiView.ViewId.GetValue() == view.Id.GetValue())
                    {
                        info.IsOpen = true;

                        // Controleer of de weergave de huidige actieve weergave is
                        if (uidoc.ActiveView.Id.GetValue() == view.Id.GetValue())
                        {
                            info.IsActive = true;
                        }
                        break;
                    }
                }

                return info;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"Fout bij het aanmaken van weergave-elementinformatie: {ex.Message}");
                return null;
            }
        }
        /// <summary>
        /// Maakt informatie voor annotatie-elementen aan
        /// </summary>
        public static AnnotationInfo CreateAnnotationInfo(Document doc, Element element)
        {
            try
            {
                if (element == null)
                    return null;
                AnnotationInfo info = new AnnotationInfo
                {
                    Id = element.Id.GetIntValue(),
                    UniqueId = element.UniqueId,
                    Name = element.Name,
                    FamilyName = element?.get_Parameter(BuiltInParameter.ELEM_FAMILY_PARAM)?.AsValueString(),
                    Category = element.Category?.Name,
                    BuiltInCategory = element.Category != null ?
                        Enum.GetName(typeof(BuiltInCategory), element.Category.Id.GetIntValue()) : null,
                    ElementClass = element.GetType().Name,
                    BoundingBox = GetBoundingBoxInfo(element)
                };

                // Haal de weergave op waarin het zich bevindt
                Parameter viewParam = element.get_Parameter(BuiltInParameter.VIEW_NAME);
                if (viewParam != null && viewParam.HasValue)
                {
                    info.OwnerView = viewParam.AsString();
                }
                else if (element.OwnerViewId != ElementId.InvalidElementId)
                {
                    View ownerView = doc.GetElement(element.OwnerViewId) as View;
                    info.OwnerView = ownerView?.Name;
                }

                // Verwerk tekstannotatie
                if (element is TextNote textNote)
                {
                    info.TextContent = textNote.Text;
                    XYZ position = textNote.Coord;
                    // Omzetten naar mm
                    info.Position = new JZPoint(
                        position.X * 304.8,
                        position.Y * 304.8,
                        position.Z * 304.8);
                }
                // Verwerk maatvoering
                else if (element is Dimension dimension && !(element is SpotDimension))
                {
                    info.DimensionValue = GetDimensionValue(dimension);
                    // Bij een maatketting is Origin niet beschikbaar, gebruik dan de oorsprong van het eerste segment
                    XYZ origin = dimension.NumberOfSegments > 0
                        ? dimension.Segments.get_Item(0).Origin
                        : dimension.Origin;
                    // Omzetten naar mm
                    info.Position = new JZPoint(
                        origin.X * 304.8,
                        origin.Y * 304.8,
                        origin.Z * 304.8);
                }
                // Verwerk overige annotatie-elementen
                else if (element is AnnotationSymbol annotationSymbol)
                {
                    if (annotationSymbol.Location is LocationPoint locationPoint)
                    {
                        XYZ position = locationPoint.Point;
                        // Omzetten naar mm
                        info.Position = new JZPoint(
                            position.X * 304.8,
                            position.Y * 304.8,
                            position.Z * 304.8);
                    }
                }
                return info;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"Fout bij het aanmaken van annotatie-elementinformatie: {ex.Message}");
                return null;
            }
        }
        /// <summary>
        /// Haalt de waarde van een maatvoering op, omgezet naar mm (hoekmaten in graden).
        /// Bij een maatketting worden de segmentwaarden gescheiden door "; " geretourneerd.
        /// </summary>
        public static string GetDimensionValue(Dimension dimension)
        {
            bool isAngular = dimension.DimensionShape == DimensionShape.Angular;
            string Format(double? value)
            {
                if (!value.HasValue)
                    return null;
                double converted = isAngular
                    ? value.Value * 180.0 / Math.PI     // Radialen naar graden
                    : value.Value * 304.8;              // Voet naar mm
                return Math.Round(converted, 2).ToString(System.Globalization.CultureInfo.InvariantCulture);
            }

            if (dimension.NumberOfSegments > 0)
            {
                var segmentValues = new List<string>();
                foreach (DimensionSegment segment in dimension.Segments)
                {
                    string value = Format(segment.Value);
                    if (value != null)
                        segmentValues.Add(value);
                }
                return segmentValues.Count > 0 ? string.Join("; ", segmentValues) : null;
            }
            return Format(dimension.Value);
        }

        /// <summary>
        /// Maakt informatie voor groep of link aan
        /// </summary>
        public static GroupOrLinkInfo CreateGroupOrLinkInfo(Document doc, Element element)
        {
            try
            {
                if (element == null)
                    return null;
                GroupOrLinkInfo info = new GroupOrLinkInfo
                {
                    Id = element.Id.GetIntValue(),
                    UniqueId = element.UniqueId,
                    Name = element.Name,
                    FamilyName = element?.get_Parameter(BuiltInParameter.ELEM_FAMILY_PARAM)?.AsValueString(),
                    Category = element.Category?.Name,
                    BuiltInCategory = element.Category != null ?
                        Enum.GetName(typeof(BuiltInCategory), element.Category.Id.GetIntValue()) : null,
                    ElementClass = element.GetType().Name,
                    BoundingBox = GetBoundingBoxInfo(element)
                };

                // Verwerk groep
                if (element is Group group)
                {
                    ICollection<ElementId> memberIds = group.GetMemberIds();
                    info.MemberCount = memberIds?.Count;
                    info.GroupType = group.GroupType?.Name;
                }
                // Verwerk link
                else if (element is RevitLinkInstance linkInstance)
                {
                    RevitLinkType linkType = doc.GetElement(linkInstance.GetTypeId()) as RevitLinkType;
                    if (linkType != null)
                    {
                        ExternalFileReference extFileRef = linkType.GetExternalFileReference();
                        // Haal absoluut pad op
                        string absPath = ModelPathUtils.ConvertModelPathToUserVisiblePath(extFileRef.GetAbsolutePath());
                        info.LinkPath = absPath;

                        // Gebruik GetLinkedFileStatus om de linkstatus op te halen
                        LinkedFileStatus linkStatus = linkType.GetLinkedFileStatus();
                        info.LinkStatus = linkStatus.ToString();
                    }
                    else
                    {
                        info.LinkStatus = LinkedFileStatus.Invalid.ToString();
                    }

                    // Haal positie op
                    LocationPoint location = linkInstance.Location as LocationPoint;
                    if (location != null)
                    {
                        XYZ point = location.Point;
                        // Omzetten naar mm
                        info.Position = new JZPoint(
                            point.X * 304.8,
                            point.Y * 304.8,
                            point.Z * 304.8);
                    }
                }

                return info;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"Fout bij het aanmaken van groeps- en linkinformatie: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Maakt uitgebreide basisinformatie voor het element aan
        /// </summary>
        public static ElementBasicInfo CreateElementBasicInfo(Document doc, Element element)
        {
            try
            {
                if (element == null)
                    return null;
                ElementBasicInfo basicInfo = new ElementBasicInfo
                {
                    Id = element.Id.GetIntValue(),
                    UniqueId = element.UniqueId,
                    Name = element.Name,
                    FamilyName = element?.get_Parameter(BuiltInParameter.ELEM_FAMILY_PARAM)?.AsValueString(),
                    Category = element.Category?.Name,
                    BuiltInCategory = element.Category != null ?
                        Enum.GetName(typeof(BuiltInCategory), element.Category.Id.GetIntValue()) : null,
                    BoundingBox = GetBoundingBoxInfo(element)
                };
                return basicInfo;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"Fout bij het aanmaken van basiselementinformatie: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Haalt de dikteparameterinformatie op van een systeemfamilie-component
        /// </summary>
        /// <param name="element">Systeemfamilie-component (wand, vloer, deur, enz.)</param>
        /// <returns>Parameterinformatie-object, retourneert null indien ongeldig</returns>
        public static ParameterInfo GetThicknessInfo(Element element)
        {
            if (element == null)
            {
                return null;
            }

            // Haal het componenttype op
            ElementType elementType = element.Document.GetElement(element.GetTypeId()) as ElementType;
            if (elementType == null)
            {
                return null;
            }

            // Haal het bijbehorende ingebouwde dikteparameter op basis van het componenttype op
            Parameter thicknessParam = null;

            if (elementType is WallType)
            {
                thicknessParam = elementType.get_Parameter(BuiltInParameter.WALL_ATTR_WIDTH_PARAM);
            }
            else if (elementType is FloorType)
            {
                thicknessParam = elementType.get_Parameter(BuiltInParameter.FLOOR_ATTR_THICKNESS_PARAM);
            }
            else if (elementType is FamilySymbol familySymbol)
            {
                switch (familySymbol.Category?.Id.GetIntValue())
                {
                    case (int)BuiltInCategory.OST_Doors:
                    case (int)BuiltInCategory.OST_Windows:
                        thicknessParam = elementType.get_Parameter(BuiltInParameter.FAMILY_THICKNESS_PARAM);
                        break;
                }
            }
            else if (elementType is CeilingType)
            {
                thicknessParam = elementType.get_Parameter(BuiltInParameter.CEILING_THICKNESS);
            }

            if (thicknessParam != null && thicknessParam.HasValue)
            {
                return new ParameterInfo
                {
                    Name = "Dikte",
                    Value = $"{thicknessParam.AsDouble() * 304.8}"
                };
            }
            return null;
        }

        /// <summary>
        /// Haalt de peilinformatie op waartoe het element behoort
        /// </summary>
        public static LevelInfo GetElementLevel(Document doc, Element element)
        {
            try
            {
                Level level = null;

                // Verwerk het ophalen van het peil voor verschillende elementtypen
                if (element is Wall wall) // Wand
                {
                    level = doc.GetElement(wall.LevelId) as Level;
                }
                else if (element is Floor floor) // Vloer
                {
                    Parameter levelParam = floor.get_Parameter(BuiltInParameter.LEVEL_PARAM);
                    if (levelParam != null && levelParam.HasValue)
                    {
                        level = doc.GetElement(levelParam.AsElementId()) as Level;
                    }
                }
                else if (element is FamilyInstance familyInstance) // Familie-instantie (inclusief algemeen model, enz.)
                {
                    // Probeer de peilparameter van de familie-instantie op te halen
                    Parameter levelParam = familyInstance.get_Parameter(BuiltInParameter.FAMILY_LEVEL_PARAM);
                    if (levelParam != null && levelParam.HasValue)
                    {
                        level = doc.GetElement(levelParam.AsElementId()) as Level;
                    }
                    // Als de bovenstaande methode niets oplevert, probeer SCHEDULE_LEVEL_PARAM te gebruiken
                    if (level == null)
                    {
                        levelParam = familyInstance.get_Parameter(BuiltInParameter.SCHEDULE_LEVEL_PARAM);
                        if (levelParam != null && levelParam.HasValue)
                        {
                            level = doc.GetElement(levelParam.AsElementId()) as Level;
                        }
                    }
                }
                else // Overige elementen
                {
                    // Probeer de algemene peilparameter op te halen
                    Parameter levelParam = element.get_Parameter(BuiltInParameter.INSTANCE_REFERENCE_LEVEL_PARAM);
                    if (levelParam != null && levelParam.HasValue)
                    {
                        level = doc.GetElement(levelParam.AsElementId()) as Level;
                    }
                }

                if (level != null)
                {
                    LevelInfo levelInfo = new LevelInfo
                    {
                        Id = level.Id.GetIntValue(),
                        Name = level.Name,
                        Height = level.Elevation * 304.8
                    };
                    return levelInfo;
                }
                else
                {
                    return null;
                }
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Haalt de bounding-boxinformatie van het element op
        /// </summary>
        public static BoundingBoxInfo GetBoundingBoxInfo(Element element)
        {
            try
            {
                BoundingBoxXYZ bbox = element.get_BoundingBox(null);
                if (bbox == null)
                    return null;
                return new BoundingBoxInfo
                {
                    Min = new JZPoint(
                        bbox.Min.X * 304.8,
                        bbox.Min.Y * 304.8,
                        bbox.Min.Z * 304.8),
                    Max = new JZPoint(
                        bbox.Max.X * 304.8,
                        bbox.Max.Y * 304.8,
                        bbox.Max.Z * 304.8)
                };
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Haalt de hoogteparameterinformatie van de bounding box op
        /// </summary>
        /// <param name="boundingBoxInfo">Bounding-boxinformatie</param>
        /// <returns>Parameterinformatie-object, retourneert null indien ongeldig</returns>
        public static ParameterInfo GetBoundingBoxHeight(BoundingBoxInfo boundingBoxInfo)
        {
            try
            {
                // Parametercontrole
                if (boundingBoxInfo?.Min == null || boundingBoxInfo?.Max == null)
                {
                    return null;
                }

                // Het verschil in de Z-richting is de hoogte
                double height = Math.Abs(boundingBoxInfo.Max.Z - boundingBoxInfo.Min.Z);

                return new ParameterInfo
                {
                    Name = "Hoogte",
                    Value = $"{height}"
                };
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Haalt de namen en waarden op van alle niet-lege parameters van het element
        /// </summary>
        /// <param name="element">Revit-element</param>
        /// <returns>Lijst met parameterinformatie</returns>
        public static List<ParameterInfo> GetDimensionParameters(Element element)
        {
            // Controleer of het element leeg is
            if (element == null)
            {
                return new List<ParameterInfo>();
            }

            var parameters = new List<ParameterInfo>();

            // Haal alle parameters van het element op
            foreach (Parameter param in element.Parameters)
            {
                try
                {
                    // Sla ongeldige parameters over
                    if (!param.HasValue || param.IsReadOnly)
                    {
                        continue;
                    }

                    // Als de huidige parameter een dimensiegerelateerde parameter is
                    if (IsDimensionParameter(param))
                    {
                        // Haal de string-representatie van de parameterwaarde op
                        string value = param.AsValueString();

                        // Voeg toe aan de lijst als de waarde niet leeg is
                        if (!string.IsNullOrWhiteSpace(value))
                        {
                            parameters.Add(new ParameterInfo
                            {
                                Name = param.Definition.Name,
                                Value = value
                            });
                        }
                    }
                }
                catch
                {
                    // Als het ophalen van een parameterwaarde mislukt, ga verder met de volgende
                    continue;
                }
            }

            // Retourneer gesorteerd op parameternaam
            return parameters.OrderBy(p => p.Name).ToList();
        }

        /// <summary>
        /// Bepaalt of de parameter een schrijfbare dimensieparameter is
        /// </summary>
        public static bool IsDimensionParameter(Parameter param)
        {

#if REVIT2023_OR_GREATER
            // Gebruik in Revit 2023 de GetDataType()-methode van Definition om het parametertype op te halen
            ForgeTypeId paramTypeId = param.Definition.GetDataType();

            // Bepaal of de parameter van een dimensiegerelateerd type is
            bool isDimensionType = paramTypeId.Equals(SpecTypeId.Length) ||
                                   paramTypeId.Equals(SpecTypeId.Angle) ||
                                   paramTypeId.Equals(SpecTypeId.Area) ||
                                   paramTypeId.Equals(SpecTypeId.Volume);
            // Sla alleen dimensietype-parameters op
            return isDimensionType;
#else
            // Bepaal of de parameter van een dimensiegerelateerd type is
            bool isDimensionType = param.Definition.ParameterType == ParameterType.Length ||
                                   param.Definition.ParameterType == ParameterType.Angle ||
                                   param.Definition.ParameterType == ParameterType.Area ||
                                   param.Definition.ParameterType == ParameterType.Volume;

            // Sla alleen dimensietype-parameters op
            return isDimensionType;
#endif
        }

    }

    /// <summary>
    /// Lichte basisinformatie van een element, standaard geretourneerd door ai_element_filter
    /// </summary>
    public class ElementBaseInfo
    {
        public int Id { get; set; }
        public string Name { get; set; }
        /// <summary>
        /// Wordt weggelaten als er geen aparte familienaam is (bijv. als het element zelf een familie is)
        /// </summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string FamilyName { get; set; }
        public string Category { get; set; }
        public string BuiltInCategory { get; set; }
        /// <summary>
        /// Aantal geplaatste instanties van dit type in het project; alleen gevuld bij types met includeInstanceCount
        /// </summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public int? InstanceCount { get; set; }
    }

    /// <summary>
    /// Aangepaste klasse voor het opslaan van volledige elementinformatie
    /// </summary>
    public class ElementInstanceInfo
    {
        /// <summary>
        /// Id
        /// </summary>
        public int Id { get; set; }
        /// <summary>
        /// Id
        /// </summary>
        public string UniqueId { get; set; }
        /// <summary>
        /// Type-Id
        /// </summary>
        public int TypeId { get; set; }
        /// <summary>
        /// Naam
        /// </summary>
        public string Name { get; set; }
        /// <summary>
        /// Familienaam
        /// </summary>
        public string FamilyName { get; set; }
        /// <summary>
        /// Categorie
        /// </summary>
        public string Category { get; set; }
        /// <summary>
        /// Ingebouwde categorie
        /// </summary>
        public string BuiltInCategory { get; set; }
        /// <summary>
        /// Bijbehorende ruimte-Id
        /// </summary>
        public int RoomId { get; set; }
        /// <summary>
        /// Naam van bijbehorend peil
        /// </summary>
        public LevelInfo Level { get; set; }
        /// <summary>
        /// Positie-informatie
        /// </summary>
        public BoundingBoxInfo BoundingBox { get; set; }
        /// <summary>
        /// Instantieparameters
        /// </summary>
        public List<ParameterInfo> Parameters { get; set; } = new List<ParameterInfo>();

    }

    /// <summary>
    /// Aangepaste klasse voor het opslaan van volledige elementtype-informatie
    /// </summary>
    public class ElementTypeInfo
    {
        /// <summary>
        /// ID
        /// </summary>
        public int Id { get; set; }
        /// <summary>
        /// Id
        /// </summary>
        public string UniqueId { get; set; }
        /// <summary>
        /// Naam
        /// </summary>
        public string Name { get; set; }
        /// <summary>
        /// Familienaam
        /// </summary>
        public string FamilyName { get; set; }
        /// <summary>
        /// Categorienaam
        /// </summary>
        public string Category { get; set; }
        /// <summary>
        /// Ingebouwde categorie-ID
        /// </summary>
        public string BuiltInCategory { get; set; }
        /// <summary>
        /// Aantal geplaatste instanties van dit type in het project; alleen gevuld met includeInstanceCount
        /// </summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public int? InstanceCount { get; set; }
        /// <summary>
        /// Typeparameters
        /// </summary>
        public List<ParameterInfo> Parameters { get; set; } = new List<ParameterInfo>();

    }

    /// <summary>
    /// Klasse voor basisinformatie van ruimtelijke positioneringselementen (peil, grid, enz.)
    /// </summary>
    public class PositioningElementInfo
    {
        /// <summary>
        /// Element-ID
        /// </summary>
        public int Id { get; set; }
        /// <summary>
        /// Uniek element-ID
        /// </summary>
        public string UniqueId { get; set; }
        /// <summary>
        /// Naam
        /// </summary>
        public string Name { get; set; }
        /// <summary>
        /// Familienaam
        /// </summary>
        public string FamilyName { get; set; }
        /// <summary>
        /// Categorienaam
        /// </summary>
        public string Category { get; set; }
        /// <summary>
        /// Ingebouwde categorie (optioneel)
        /// </summary>
        public string BuiltInCategory { get; set; }
        /// <summary>
        /// De .NET-klassenaam van het element
        /// </summary>
        public string ElementClass { get; set; }
        /// <summary>
        /// Hoogteligging (van toepassing op peilen, eenheid mm)
        /// </summary>
        public double? Elevation { get; set; }
        /// <summary>
        /// Bijbehorend peil
        /// </summary>
        public LevelInfo Level { get; set; }
        /// <summary>
        /// Positie-informatie
        /// </summary>
        public BoundingBoxInfo BoundingBox { get; set; }
        /// <summary>
        /// Gridlijn (van toepassing op assenstelsels/grids)
        /// </summary>
        public JZLine GridLine { get; set; }
    }
    /// <summary>
    /// Klasse voor het opslaan van basisinformatie van ruimte-elementen (ruimte, zone, enz.)
    /// </summary>
    public class SpatialElementInfo
    {
        /// <summary>
        /// Element-ID
        /// </summary>
        public int Id { get; set; }
        /// <summary>
        /// Uniek element-ID
        /// </summary>
        public string UniqueId { get; set; }
        /// <summary>
        /// Naam
        /// </summary>
        public string Name { get; set; }
        /// <summary>
        /// Familienaam
        /// </summary>
        public string FamilyName { get; set; }
        /// <summary>
        /// Nummer
        /// </summary>
        public string Number { get; set; }
        /// <summary>
        /// Categorienaam
        /// </summary>
        public string Category { get; set; }
        /// <summary>
        /// Ingebouwde categorie (optioneel)
        /// </summary>
        public string BuiltInCategory { get; set; }
        /// <summary>
        /// De .NET-klassenaam van het element
        /// </summary>
        public string ElementClass { get; set; }
        /// <summary>
        /// Oppervlakte (eenheid mm²)
        /// </summary>
        public double? Area { get; set; }
        /// <summary>
        /// Volume (eenheid mm³)
        /// </summary>
        public double? Volume { get; set; }
        /// <summary>
        /// Omtrek (eenheid mm)
        /// </summary>
        public double? Perimeter { get; set; }
        /// <summary>
        /// Bijbehorend peil
        /// </summary>
        public LevelInfo Level { get; set; }

        /// <summary>
        /// Positie-informatie
        /// </summary>
        public BoundingBoxInfo BoundingBox { get; set; }
    }
    /// <summary>
    /// Klasse voor het opslaan van basisinformatie van weergave-elementen
    /// </summary>
    public class ViewInfo
    {
        /// <summary>
        /// Element-ID
        /// </summary>
        public int Id { get; set; }
        /// <summary>
        /// Uniek element-ID
        /// </summary>
        public string UniqueId { get; set; }
        /// <summary>
        /// Naam
        /// </summary>
        public string Name { get; set; }
        /// <summary>
        /// Familienaam
        /// </summary>
        public string FamilyName { get; set; }
        /// <summary>
        /// Categorienaam
        /// </summary>
        public string Category { get; set; }
        /// <summary>
        /// Ingebouwde categorie (optioneel)
        /// </summary>
        public string BuiltInCategory { get; set; }
        /// <summary>
        /// De .NET-klassenaam van het element
        /// </summary>
        public string ElementClass { get; set; }

        /// <summary>
        /// Weergavetype
        /// </summary>
        public string ViewType { get; set; }

        /// <summary>
        /// Weergaveschaal
        /// </summary>
        public int? Scale { get; set; }

        /// <summary>
        /// Of het een sjabloonweergave is
        /// </summary>
        public bool IsTemplate { get; set; }

        /// <summary>
        /// Detailniveau
        /// </summary>
        public string DetailLevel { get; set; }

        /// <summary>
        /// Gekoppeld peil
        /// </summary>
        public LevelInfo AssociatedLevel { get; set; }

        /// <summary>
        /// Positie-informatie
        /// </summary>
        public BoundingBoxInfo BoundingBox { get; set; }

        /// <summary>
        /// Of de weergave geopend is
        /// </summary>
        public bool IsOpen { get; set; }

        /// <summary>
        /// Of het de huidige actieve weergave is
        /// </summary>
        public bool IsActive { get; set; }
    }
    /// <summary>
    /// Klasse voor het opslaan van basisinformatie van annotatie-elementen
    /// </summary>
    public class AnnotationInfo
    {
        /// <summary>
        /// Element-ID
        /// </summary>
        public int Id { get; set; }
        /// <summary>
        /// Uniek element-ID
        /// </summary>
        public string UniqueId { get; set; }
        /// <summary>
        /// Naam
        /// </summary>
        public string Name { get; set; }
        /// <summary>
        /// Familienaam
        /// </summary>
        public string FamilyName { get; set; }
        /// <summary>
        /// Categorienaam
        /// </summary>
        public string Category { get; set; }
        /// <summary>
        /// Ingebouwde categorie (optioneel)
        /// </summary>
        public string BuiltInCategory { get; set; }
        /// <summary>
        /// De .NET-klassenaam van het element
        /// </summary>
        public string ElementClass { get; set; }
        /// <summary>
        /// Bijbehorende weergave
        /// </summary>
        public string OwnerView { get; set; }
        /// <summary>
        /// Tekstinhoud (van toepassing op tekstannotaties)
        /// </summary>
        public string TextContent { get; set; }
        /// <summary>
        /// Positie-informatie (eenheid mm)
        /// </summary>
        public JZPoint Position { get; set; }

        /// <summary>
        /// Positie-informatie
        /// </summary>
        public BoundingBoxInfo BoundingBox { get; set; }
        /// <summary>
        /// Maatwaarde (van toepassing op maatvoeringen)
        /// </summary>
        public string DimensionValue { get; set; }
    }
    /// <summary>
    /// Klasse voor het opslaan van basisinformatie van groepen en links
    /// </summary>
    public class GroupOrLinkInfo
    {
        /// <summary>
        /// Element-ID
        /// </summary>
        public int Id { get; set; }
        /// <summary>
        /// Uniek element-ID
        /// </summary>
        public string UniqueId { get; set; }
        /// <summary>
        /// Naam
        /// </summary>
        public string Name { get; set; }
        /// <summary>
        /// Familienaam
        /// </summary>
        public string FamilyName { get; set; }
        /// <summary>
        /// Categorienaam
        /// </summary>
        public string Category { get; set; }
        /// <summary>
        /// Ingebouwde categorie (optioneel)
        /// </summary>
        public string BuiltInCategory { get; set; }
        /// <summary>
        /// De .NET-klassenaam van het element
        /// </summary>
        public string ElementClass { get; set; }
        /// <summary>
        /// Aantal groepsleden
        /// </summary>
        public int? MemberCount { get; set; }
        /// <summary>
        /// Groepstype
        /// </summary>
        public string GroupType { get; set; }
        /// <summary>
        /// Linkstatus
        /// </summary>
        public string LinkStatus { get; set; }
        /// <summary>
        /// Linkpad
        /// </summary>
        public string LinkPath { get; set; }
        /// <summary>
        /// Positie-informatie (eenheid mm)
        /// </summary>
        public JZPoint Position { get; set; }

        /// <summary>
        /// Positie-informatie
        /// </summary>
        public BoundingBoxInfo BoundingBox { get; set; }
    }
    /// <summary>
    /// Uitgebreide klasse voor het opslaan van basiselementinformatie
    /// </summary>
    public class ElementBasicInfo
    {
        /// <summary>
        /// Element-ID
        /// </summary>
        public int Id { get; set; }
        /// <summary>
        /// Uniek element-ID
        /// </summary>
        public string UniqueId { get; set; }
        /// <summary>
        /// Naam
        /// </summary>
        public string Name { get; set; }
        /// <summary>
        /// Familienaam
        /// </summary>
        public string FamilyName { get; set; }
        /// <summary>
        /// Categorienaam
        /// </summary>
        public string Category { get; set; }
        /// <summary>
        /// Ingebouwde categorie (optioneel)
        /// </summary>
        public string BuiltInCategory { get; set; }

        /// <summary>
        /// Positie-informatie
        /// </summary>
        public BoundingBoxInfo BoundingBox { get; set; }
    }



    /// <summary>
    /// Aangepaste klasse voor het opslaan van volledige parameterinformatie
    /// </summary>
    public class ParameterInfo
    {
        public string Name { get; set; }
        public string Value { get; set; }
    }

    /// <summary>
    /// Aangepaste klasse voor het opslaan van bounding-boxinformatie
    /// </summary>
    public class BoundingBoxInfo
    {
        public JZPoint Min { get; set; }
        public JZPoint Max { get; set; }
    }

    /// <summary>
    /// Aangepaste klasse voor het opslaan van peilinformatie
    /// </summary>
    public class LevelInfo
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public double Height { get; set; }
    }



}
