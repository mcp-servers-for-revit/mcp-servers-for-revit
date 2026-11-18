using Newtonsoft.Json;

namespace RevitMCPCommandSet.Models.DataExtraction
{
    /// <summary>
    /// Model for material quantity data
    /// </summary>
    public class MaterialQuantityModel
    {
        [JsonProperty("materialId")]
        public long MaterialId { get; set; }

        [JsonProperty("materialName")]
        public string MaterialName { get; set; }

        [JsonProperty("materialClass")]
        public string MaterialClass { get; set; }

        [JsonProperty("area")]
        public double Area { get; set; } // Square feet

        [JsonProperty("volume")]
        public double Volume { get; set; } // Cubic feet

        [JsonProperty("elementCount")]
        public int ElementCount { get; set; }

        [JsonProperty("elementIds")]
        public List<long> ElementIds { get; set; } = new List<long>();
    }

    /// <summary>
    /// Result container for material quantities
    /// </summary>
    public class GetMaterialQuantitiesResult
    {
        [JsonProperty("totalMaterials")]
        public int TotalMaterials { get; set; }

        [JsonProperty("totalCount")]
        public int TotalCount { get; set; }

        [JsonProperty("truncated")]
        public bool Truncated { get; set; }

        [JsonProperty("totalArea")]
        public double TotalArea { get; set; }

        [JsonProperty("totalVolume")]
        public double TotalVolume { get; set; }

        [JsonProperty("materials")]
        public List<MaterialQuantityModel> Materials { get; set; } = new List<MaterialQuantityModel>();

        [JsonProperty("success")]
        public bool Success { get; set; }

        [JsonProperty("message")]
        public string Message { get; set; }
    }
}
