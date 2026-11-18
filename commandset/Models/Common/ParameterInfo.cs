using Newtonsoft.Json;

namespace RevitMCPCommandSet.Models.Common
{
    public class ParamData
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("value")]
        public object Value { get; set; }

        [JsonProperty("storageType")]
        public string StorageType { get; set; }

        [JsonProperty("isReadOnly")]
        public bool IsReadOnly { get; set; }

        [JsonProperty("isShared")]
        public bool IsShared { get; set; }

        [JsonProperty("groupName")]
        public string GroupName { get; set; }

        [JsonProperty("hasValue")]
        public bool HasValue { get; set; }
    }

    public class ElementParametersResult
    {
        [JsonProperty("elementId")]
        public long ElementId { get; set; }

        [JsonProperty("elementName")]
        public string ElementName { get; set; }

        [JsonProperty("category")]
        public string Category { get; set; }

        [JsonProperty("parameters")]
        public List<ParamData> Parameters { get; set; } = new List<ParamData>();
    }

    public class SetParameterRequest
    {
        [JsonProperty("elementId")]
        public long ElementId { get; set; }

        [JsonProperty("parameterName")]
        public string ParameterName { get; set; }

        [JsonProperty("value")]
        public object Value { get; set; }
    }

    public class SetParameterResult
    {
        [JsonProperty("elementId")]
        public long ElementId { get; set; }

        [JsonProperty("parameterName")]
        public string ParameterName { get; set; }

        [JsonProperty("success")]
        public bool Success { get; set; }

        [JsonProperty("message")]
        public string Message { get; set; }
    }

    public class SetWorksetRequest
    {
        [JsonProperty("elementId")]
        public long ElementId { get; set; }

        [JsonProperty("worksetName")]
        public string WorksetName { get; set; }
    }

    public class SetWorksetResult
    {
        [JsonProperty("elementId")]
        public long ElementId { get; set; }

        [JsonProperty("worksetName")]
        public string WorksetName { get; set; }

        [JsonProperty("success")]
        public bool Success { get; set; }

        [JsonProperty("message")]
        public string Message { get; set; }
    }

    public class CompoundLayerInput
    {
        [JsonProperty("function")]
        public string Function { get; set; }

        [JsonProperty("widthMm")]
        public double WidthMm { get; set; }

        [JsonProperty("materialName")]
        public string MaterialName { get; set; }

        [JsonProperty("materialId")]
        public long? MaterialId { get; set; }

        [JsonProperty("wraps")]
        public bool? Wraps { get; set; }
    }
}
