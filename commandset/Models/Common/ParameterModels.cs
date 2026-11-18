using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;

namespace RevitMCPCommandSet.Models.Common
{
    /// <summary>
    /// De aan te passen parameters voor één element, als input voor set_parameters.
    /// </summary>
    public class ElementParameterInput
    {
        /// <summary>ElementId van het element (instance of type), als string.</summary>
        [JsonProperty("elementId")]
        public string ElementId { get; set; }

        /// <summary>De aan te passen parameters + waardes voor dit element.</summary>
        [JsonProperty("parameters")]
        public List<ParameterValueInput> Parameters { get; set; } = new List<ParameterValueInput>();
    }

    /// <summary>
    /// Eén parameternaam + de nieuwe waarde, als input voor set_parameters.
    /// </summary>
    public class ParameterValueInput
    {
        /// <summary>Naam van de parameter, wordt opgezocht via Element.LookupParameter.</summary>
        [JsonProperty("name")]
        public string Name { get; set; }

        /// <summary>
        /// De nieuwe waarde, zoals aangeleverd in JSON (string/getal/boolean). De handler converteert deze waarde naar het Revit-opslagtype.
        /// </summary>
        [JsonProperty("value")]
        public JToken Value { get; set; }
    }

    /// <summary>
    /// Resultaat van de parameterwijzigingen op één element, als output van set_parameters.
    /// </summary>
    public class ElementParameterOutput
    {
        public long ElementId { get; set; }
        public List<ParameterResultOutput> Parameters { get; set; } = new List<ParameterResultOutput>();
    }

    /// <summary>
    /// Resultaat van één parameterwijziging, als output van set_parameters.
    /// </summary>
    public class ParameterResultOutput
    {
        public string ParameterName { get; set; }
        public bool Success { get; set; }
        public string Message { get; set; }
    }
}
