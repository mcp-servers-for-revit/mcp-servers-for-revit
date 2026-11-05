using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RevitMCPCommandSet.Models.Common
{
    /// <summary>
    /// Filterinstellingen - ondersteunt filteren op gecombineerde voorwaarden
    /// </summary>
    public class FilterSetting
    {
        /// <summary>
        /// Haalt de naam van de te filteren ingebouwde Revit-categorie op of stelt deze in (bijv. "OST_Walls").
        /// Als dit null of leeg is, wordt er niet op categorie gefilterd.
        /// </summary>
        [JsonProperty("filterCategory")]
        public string FilterCategory { get; set; } = null;
        /// <summary>
        /// Haalt de naam van het te filteren Revit-elementtype op of stelt deze in (bijv. "Wall" of "Autodesk.Revit.DB.Wall").
        /// Als dit null of leeg is, wordt er niet op type gefilterd.
        /// </summary>
        [JsonProperty("filterElementType")]
        public string FilterElementType { get; set; } = null;
        /// <summary>
        /// Haalt de ElementId-waarde van het te filteren familietype (FamilySymbol) op of stelt deze in.
        /// Als deze 0 of negatief is, wordt er niet op familie gefilterd.
        /// Let op: dit filter is alleen van toepassing op elementinstanties, niet op typeelementen.
        /// </summary>
        [JsonProperty("filterFamilySymbolId")]
        public int FilterFamilySymbolId { get; set; } = -1;
        /// <summary>
        /// Haalt op of stelt in of elementtypes (zoals wandtypes, deurtypes, enz.) moeten worden opgenomen
        /// </summary>
        [JsonProperty("includeTypes")]
        public bool IncludeTypes { get; set; } = false;
        /// <summary>
        /// Haalt op of stelt in of elementinstanties (zoals geplaatste wanden, deuren, enz.) moeten worden opgenomen
        /// </summary>
        [JsonProperty("includeInstances")]
        public bool IncludeInstances { get; set; } = true;
        /// <summary>
        /// Haalt op of stelt in of alleen elementen worden geretourneerd die zichtbaar zijn in de huidige weergave.
        /// Let op: dit filter is alleen van toepassing op elementinstanties, niet op typeelementen.
        /// </summary>
        [JsonProperty("filterVisibleInCurrentView")]
        public bool FilterVisibleInCurrentView { get; set; }
        /// <summary>
        /// Haalt de minimale puntcoördinaat voor het filteren op ruimtelijk bereik op of stelt deze in (eenheid: mm)
        /// Als deze waarde samen met BoundingBoxMax is ingesteld, worden elementen gefilterd die dit begrenzingsvak overlappen
        /// </summary>
        [JsonProperty("boundingBoxMin")]
        public JZPoint BoundingBoxMin { get; set; } = null;
        /// <summary>
        /// Haalt de maximale puntcoördinaat voor het filteren op ruimtelijk bereik op of stelt deze in (eenheid: mm)
        /// Als deze waarde samen met BoundingBoxMin is ingesteld, worden elementen gefilterd die dit begrenzingsvak overlappen
        /// </summary>
        [JsonProperty("boundingBoxMax")]
        public JZPoint BoundingBoxMax { get; set; } = null;
        /// <summary>
        /// Maximaal aantal elementen. Zonder waarde (of &lt;= 0) geldt er geen limiet en worden alle
        /// elementen die aan de filtercriteria voldoen geretourneerd.
        /// </summary>
        [JsonProperty("maxElements")]
        public int? MaxElements { get; set; } = null;
        /// <summary>
        /// Haalt op of stelt in of per element de uitgebreide informatie wordt geretourneerd (peil, bounding box,
        /// parameters, weergave-/ruimte-/annotatiespecifieke gegevens, enz.). Standaard false: dan wordt alleen
        /// de basisinformatie (Id, naam, familienaam, categorie) geretourneerd, om de response licht te houden.
        /// </summary>
        [JsonProperty("includeDetails")]
        public bool IncludeDetails { get; set; } = false;
        /// <summary>
        /// Haalt op of stelt in of bij elk geretourneerd elementtype wordt meegegeven hoe vaak het in het project
        /// is geplaatst (InstanceCount). Alleen van toepassing als IncludeTypes true is. De telling geldt voor het
        /// hele project en houdt geen rekening met het zichtbaarheids- of ruimtelijke bereikfilter.
        /// </summary>
        [JsonProperty("includeInstanceCount")]
        public bool IncludeInstanceCount { get; set; } = false;
        /// <summary>
        /// Valideert de geldigheid van de filterinstellingen en controleert op mogelijke conflicten
        /// </summary>
        /// <returns>Retourneert true als de instellingen geldig zijn, anders false</returns>
        public bool Validate(out string errorMessage)
        {
            errorMessage = null;

            // Controleer of ten minste één elementsoort is geselecteerd
            if (!IncludeTypes && !IncludeInstances)
            {
                errorMessage = "Ongeldige filterinstelling: er moet ten minste elementtypes of elementinstanties zijn opgenomen";
                return false;
            }

            // Controleer of ten minste één filtervoorwaarde is opgegeven
            if (string.IsNullOrWhiteSpace(FilterCategory) &&
                string.IsNullOrWhiteSpace(FilterElementType) &&
                FilterFamilySymbolId <= 0)
            {
                errorMessage = "Ongeldige filterinstelling: er moet ten minste één filtervoorwaarde worden opgegeven (categorie, elementtype of familietype)";
                return false;
            }

            // Controleer op conflicten tussen typeelementen en bepaalde filters
            if (IncludeTypes && !IncludeInstances)
            {
                List<string> invalidFilters = new List<string>();
                if (FilterFamilySymbolId > 0)
                    invalidFilters.Add("filteren op familie-instantie");
                if (FilterVisibleInCurrentView)
                    invalidFilters.Add("filteren op zichtbaarheid in weergave");
                if (invalidFilters.Count > 0)
                {
                    errorMessage = $"Bij het uitsluitend filteren van typeelementen zijn de volgende filters niet van toepassing: {string.Join(", ", invalidFilters)}";
                    return false;
                }
            }
            // Controleer de geldigheid van het ruimtelijke bereikfilter
            if (BoundingBoxMin != null && BoundingBoxMax != null)
            {
                // Zorg ervoor dat het minimumpunt kleiner dan of gelijk is aan het maximumpunt
                if (BoundingBoxMin.X > BoundingBoxMax.X ||
                    BoundingBoxMin.Y > BoundingBoxMax.Y ||
                    BoundingBoxMin.Z > BoundingBoxMax.Z)
                {
                    errorMessage = "Ongeldige instelling voor het ruimtelijke bereikfilter: de coördinaat van het minimumpunt moet kleiner dan of gelijk zijn aan die van het maximumpunt";
                    return false;
                }
            }
            else if (BoundingBoxMin != null || BoundingBoxMax != null)
            {
                errorMessage = "Ongeldige instelling voor het ruimtelijke bereikfilter: het minimum- en maximumpunt moeten samen worden ingesteld";
                return false;
            }
            return true;
        }
    }
}
