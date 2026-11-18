using Newtonsoft.Json;

namespace RevitMCPCommandSet.Models.Common
{
    public class ModifyElementSetting
    {
        [JsonProperty("elementIds")]
        public List<long> ElementIds { get; set; } = new List<long>();

        [JsonProperty("action")]
        public string Action { get; set; }

        [JsonProperty("translation")]
        public JZPoint Translation { get; set; }

        [JsonProperty("rotationCenter")]
        public JZPoint RotationCenter { get; set; }

        [JsonProperty("rotationAngle")]
        public double RotationAngle { get; set; }

        [JsonProperty("mirrorPlaneOrigin")]
        public JZPoint MirrorPlaneOrigin { get; set; }

        [JsonProperty("mirrorPlaneNormal")]
        public JZPoint MirrorPlaneNormal { get; set; }

        [JsonProperty("copyOffset")]
        public JZPoint CopyOffset { get; set; }
    }
}
