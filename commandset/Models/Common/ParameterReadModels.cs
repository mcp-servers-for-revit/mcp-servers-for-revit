using Newtonsoft.Json;
using System.Collections.Generic;

namespace RevitMCPCommandSet.Models.Common
{
    /// <summary>
    /// Beschrijving van één parameter (zonder waarde), als output van get_available_parameters.
    /// </summary>
    public class ParameterDefinitionOutput
    {
        /// <summary>Naam van de parameter zoals getoond in Revit's Properties-palet.</summary>
        public string Name { get; set; }

        /// <summary>Gegevenstype van de parameter (bijv. "Length", "Yes/No", "Text"), indien bekend.</summary>
        public string DataType { get; set; }

        /// <summary>Revit StorageType: Double, Integer, String of ElementId.</summary>
        public string StorageType { get; set; }

        /// <summary>Parametergroep waaronder de parameter in het Properties-palet staat.</summary>
        public string Group { get; set; }

        public bool IsReadOnly { get; set; }
    }

    /// <summary>
    /// De beschikbare instance-parameters van één element, als output van get_available_parameters.
    /// De type-parameters staan niet per element maar eenmalig per type in
    /// <see cref="AvailableParametersResult.Types"/>, zodat elementen van hetzelfde familietype die
    /// lijst niet herhalen.
    /// </summary>
    public class ElementAvailableParametersOutput
    {
        public long ElementId { get; set; }
        public bool Found { get; set; }
        public string Message { get; set; }
        public string Name { get; set; }
        public string Category { get; set; }

        /// <summary>ElementId van het familietype, verwijst naar een entry in Types. Null als het element geen type heeft.</summary>
        public long? TypeId { get; set; }

        public List<ParameterDefinitionOutput> InstanceParameters { get; set; } = new List<ParameterDefinitionOutput>();
    }

    /// <summary>
    /// De beschikbare type-parameters van één familietype, als output van get_available_parameters.
    /// </summary>
    public class TypeAvailableParametersOutput
    {
        public long TypeId { get; set; }
        public string FamilyName { get; set; }
        public string TypeName { get; set; }
        public string Category { get; set; }
        public List<ParameterDefinitionOutput> TypeParameters { get; set; } = new List<ParameterDefinitionOutput>();
    }

    /// <summary>
    /// Totaalresultaat van get_available_parameters.
    /// </summary>
    public class AvailableParametersResult
    {
        public List<ElementAvailableParametersOutput> Elements { get; set; } = new List<ElementAvailableParametersOutput>();
        public List<TypeAvailableParametersOutput> Types { get; set; } = new List<TypeAvailableParametersOutput>();
    }

    /// <summary>
    /// Eén element + optioneel de op te halen parameternamen, als input voor get_parameters. Zonder
    /// eigen parameternamen gelden de algemene parameterNames van de aanroep.
    /// </summary>
    public class ElementParameterRequestInput
    {
        /// <summary>ElementId van het element (instance of type), als string.</summary>
        [JsonProperty("elementId")]
        public string ElementId { get; set; }

        /// <summary>Op te halen parameternamen voor dit element; gaat voor op de algemene lijst.</summary>
        [JsonProperty("parameterNames")]
        public List<string> ParameterNames { get; set; }
    }

    /// <summary>
    /// De opgehaalde parameterwaarden van één element, als output van get_parameters.
    /// </summary>
    public class ElementParameterValuesOutput
    {
        public long ElementId { get; set; }
        public bool Found { get; set; }
        public string Message { get; set; }

        /// <summary>
        /// ElementId van het familietype. Parameters met Source "type" moeten via dit ID aangepast
        /// worden (set_parameters zoekt alleen op het opgegeven element zelf). Null als het element geen
        /// type heeft of zelf een type is.
        /// </summary>
        public long? TypeId { get; set; }

        public List<ParameterValueOutput> Parameters { get; set; } = new List<ParameterValueOutput>();
    }

    /// <summary>
    /// De waarde van één parameter, als output van get_parameters.
    /// </summary>
    public class ParameterValueOutput
    {
        public string Name { get; set; }
        public bool Found { get; set; }
        public string Message { get; set; }

        /// <summary>"instance" of "type": waar de parameter gevonden is.</summary>
        public string Source { get; set; }

        public string StorageType { get; set; }

        /// <summary>
        /// De waarde: getal in weergave-eenheden van het model (zie Unit) voor Double, boolean voor
        /// Ja/Nee-parameters, integer, tekst, of het ElementId als getal voor ElementId-parameters.
        /// </summary>
        public object Value { get; set; }

        /// <summary>De waarde zoals Revit hem in het Properties-palet toont (AsValueString).</summary>
        public string DisplayValue { get; set; }

        /// <summary>Weergave-eenheid van Value (bijv. "Millimeters"), alleen bij meetbare Double-parameters.</summary>
        public string Unit { get; set; }

        public bool IsReadOnly { get; set; }
    }
}
