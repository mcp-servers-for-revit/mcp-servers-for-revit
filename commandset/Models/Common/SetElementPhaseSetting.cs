using Newtonsoft.Json;

namespace RevitMCPCommandSet.Models.Common
{
    public class SetElementPhaseRequest
    {
        [JsonProperty("elementId")]
        public long ElementId { get; set; }

        [JsonProperty("createdPhaseId")]
        public long? CreatedPhaseId { get; set; }

        [JsonProperty("demolishedPhaseId")]
        public long? DemolishedPhaseId { get; set; }
    }

    public class SetElementPhaseResult
    {
        [JsonProperty("elementId")]
        public long ElementId { get; set; }

        [JsonProperty("success")]
        public bool Success { get; set; }

        [JsonProperty("message")]
        public string Message { get; set; }
    }
}
