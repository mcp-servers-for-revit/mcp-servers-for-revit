using Newtonsoft.Json;

namespace RevitMCPCommandSet.Models.DataExtraction
{
    /// <summary>
    /// Statistics for a category
    /// </summary>
    public class CategoryStatistics
    {
        [JsonProperty("categoryName")]
        public string CategoryName { get; set; }

        [JsonProperty("elementCount")]
        public int ElementCount { get; set; }

        [JsonProperty("typeCount")]
        public int TypeCount { get; set; }

        [JsonProperty("familyCount")]
        public int FamilyCount { get; set; }

        [JsonProperty("types")]
        public List<TypeStatistics> Types { get; set; } = new List<TypeStatistics>();
    }

    /// <summary>
    /// Statistics for a type
    /// </summary>
    public class TypeStatistics
    {
        [JsonProperty("typeName")]
        public string TypeName { get; set; }

        [JsonProperty("familyName")]
        public string FamilyName { get; set; }

        [JsonProperty("instanceCount")]
        public int InstanceCount { get; set; }
    }

    /// <summary>
    /// Statistics by level
    /// </summary>
    public class LevelStatistics
    {
        [JsonProperty("levelName")]
        public string LevelName { get; set; }

        [JsonProperty("elevation")]
        public double Elevation { get; set; } // mm

        [JsonProperty("elementCount")]
        public int ElementCount { get; set; }
    }

    /// <summary>
    /// Result container for model statistics
    /// </summary>
    public class AnalyzeModelStatisticsResult
    {
        [JsonProperty("projectName")]
        public string ProjectName { get; set; }

        [JsonProperty("totalElements")]
        public int TotalElements { get; set; }

        [JsonProperty("totalTypes")]
        public int TotalTypes { get; set; }

        [JsonProperty("totalFamilies")]
        public int TotalFamilies { get; set; }

        [JsonProperty("totalViews")]
        public int TotalViews { get; set; }

        [JsonProperty("totalSheets")]
        public int TotalSheets { get; set; }

        [JsonProperty("categories")]
        public List<CategoryStatistics> Categories { get; set; } = new List<CategoryStatistics>();

        [JsonProperty("levels")]
        public List<LevelStatistics> Levels { get; set; } = new List<LevelStatistics>();

        [JsonProperty("success")]
        public bool Success { get; set; }

        [JsonProperty("message")]
        public string Message { get; set; }
    }
}
