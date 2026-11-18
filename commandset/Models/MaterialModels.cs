using Newtonsoft.Json;

namespace RevitMCPCommandSet.Models
{
    // ---------------------------------------------------------------------------------------------
    // Invoer van manage_materials
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// De volledige aanvraag voor manage_materials. Per actie worden alleen de bijbehorende velden gebruikt.
    /// </summary>
    public class ManageMaterialsRequest
    {
        /// <summary>options | list | create | update</summary>
        [JsonProperty("action")]
        public string Action { get; set; }

        // options
        [JsonProperty("mainGroup")]
        public string MainGroup { get; set; }

        [JsonProperty("includeFillPatterns")]
        public bool IncludeFillPatterns { get; set; }

        // list
        /// <summary>all (standaard) | valid | invalid | non-naakt</summary>
        [JsonProperty("filter")]
        public string Filter { get; set; }

        [JsonProperty("nameContains")]
        public string NameContains { get; set; }

        [JsonProperty("offset")]
        public int? Offset { get; set; }

        [JsonProperty("limit")]
        public int? Limit { get; set; }

        // create / update
        /// <summary>Alleen laten zien wat er zou gebeuren, zonder iets aan te passen.</summary>
        [JsonProperty("dryRun")]
        public bool DryRun { get; set; }

        [JsonProperty("materials")]
        public List<MaterialInput> Materials { get; set; }

        [JsonProperty("updates")]
        public List<MaterialInput> Updates { get; set; }
    }

    /// <summary>
    /// Eén materiaal om aan te maken (create) of aan te passen (update). De naam wordt nooit vrij
    /// opgegeven maar altijd opgebouwd uit de delen.
    /// </summary>
    public class MaterialInput
    {
        /// <summary>ElementId van het bestaande materiaal (alleen update).</summary>
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("mainGroup")]
        public string MainGroup { get; set; }

        [JsonProperty("characteristic")]
        public string Characteristic { get; set; }

        [JsonProperty("application")]
        public string Application { get; set; }

        /// <summary>Vrije staart van de naam. Bij update: een lege tekst haalt de staart weg.</summary>
        [JsonProperty("custom")]
        public string Custom { get; set; }

        /// <summary>Materiaalkleur als "R,G,B" (0-255).</summary>
        [JsonProperty("color")]
        public string Color { get; set; }

        [JsonProperty("surfaceForeground")]
        public MaterialPatternInput SurfaceForeground { get; set; }

        [JsonProperty("surfaceBackground")]
        public MaterialPatternInput SurfaceBackground { get; set; }

        [JsonProperty("cutForeground")]
        public MaterialPatternInput CutForeground { get; set; }

        [JsonProperty("cutBackground")]
        public MaterialPatternInput CutBackground { get; set; }

        /// <summary>
        /// Naam of ElementId van een bestaand materiaal als sjabloon. Create dupliceert het sjabloon in zijn
        /// geheel; update neemt klasse, kleur en arcering over. De assets worden in beide gevallen gedeeld met het sjabloon, nooit gekopieerd.
        /// </summary>
        [JsonProperty("template")]
        public string Template { get; set; }
    }

    /// <summary>Een vulpatroon (op naam) en/of de kleur daarvan.</summary>
    public class MaterialPatternInput
    {
        [JsonProperty("pattern")]
        public string Pattern { get; set; }

        [JsonProperty("color")]
        public string Color { get; set; }
    }

    // ---------------------------------------------------------------------------------------------
    // Uitvoer van manage_materials. Wordt met camelCase en zonder null-waardes geserialiseerd (zie
    // ManageMaterialsEventHandler), dus lege velden komen niet in het antwoord.
    // ---------------------------------------------------------------------------------------------

    public class ManageMaterialsResult
    {
        public string Action { get; set; }

        public bool? DryRun { get; set; }

        public string Message { get; set; }

        // options
        public List<string> MainGroups { get; set; }
        public string MainGroup { get; set; }
        public List<string> Characteristics { get; set; }
        public List<string> Applications { get; set; }
        public List<string> FillPatterns { get; set; }

        // list
        /// <summary>Aantal materialen dat aan het filter voldoet (vóór offset/limit).</summary>
        public int? Total { get; set; }
        public int? Count { get; set; }
        public List<MaterialInfo> Materials { get; set; }

        // create / update
        public int? Succeeded { get; set; }
        public int? Failed { get; set; }
        public List<MaterialItemResult> Results { get; set; }
    }

    public class NaaktOptionsResult
    {
        public List<string> MainGroups { get; set; }
        public string MainGroup { get; set; }
        public List<string> Characteristics { get; set; }
        public List<string> Applications { get; set; }
    }

    /// <summary>De uitkomst van de NAAKT-controle op een materiaalnaam.</summary>
    public class NaaktCheckResult
    {
        /// <summary>De gecontroleerde naam (niet in de uitvoer: staat al bij het materiaal).</summary>
        [JsonIgnore]
        public string Name { get; set; }

        public bool Valid { get; set; }
        public string MainGroup { get; set; }
        public string Characteristic { get; set; }
        public string Application { get; set; }
        public string Custom { get; set; }

        public List<string> Issues { get; set; } = new List<string>();
        public string Suggestion { get; set; }
    }

    /// <summary>Samenvatting van één materiaal in het project (list).</summary>
    public class MaterialInfo
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public NaaktCheckResult Naakt { get; set; }
        public string MaterialClass { get; set; }

        /// <summary>Materiaalkleur als "R,G,B".</summary>
        public string Color { get; set; }

        public MaterialPatternInfo SurfaceForeground { get; set; }
        public MaterialPatternInfo SurfaceBackground { get; set; }
        public MaterialPatternInfo CutForeground { get; set; }
        public MaterialPatternInfo CutBackground { get; set; }

        public string AppearanceAsset { get; set; }
        public string StructuralAsset { get; set; }
        public string ThermalAsset { get; set; }
    }

    public class MaterialPatternInfo
    {
        public string Pattern { get; set; }
        public string Color { get; set; }
    }

    /// <summary>Het resultaat voor één item van een create- of update-aanroep.</summary>
    public class MaterialItemResult
    {
        /// <summary>Positie in de aangeleverde lijst (vanaf 0).</summary>
        public int Index { get; set; }

        public string Id { get; set; }
        public string OldName { get; set; }
        public string NewName { get; set; }

        /// <summary>created | updated | unchanged | failed</summary>
        public string Status { get; set; }

        public bool Success { get; set; }

        /// <summary>Wat er wijzigt (of wijzigde): name, color, template, surfaceForeground, enzovoort.</summary>
        public List<string> Changes { get; set; }

        public string Message { get; set; }
        public List<string> Issues { get; set; }
        public string Suggestion { get; set; }
    }
}
