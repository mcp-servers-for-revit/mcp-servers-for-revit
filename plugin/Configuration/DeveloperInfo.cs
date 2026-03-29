using Newtonsoft.Json;

namespace revit_mcp_plugin.Configuration
{
    /// <summary>
    /// <para>Developer information.</para>
    /// </summary>
    public class DeveloperInfo
    {
        /// <summary>
        /// <para>Developer name.</para>
        /// </summary>
        [JsonProperty("name")]
        public string Name { get; set; } = "";

        /// <summary>
        /// <para>Developer e-mail address.</para>
        /// </summary>
        [JsonProperty("email")]
        public string Email { get; set; } = "";

        /// <summary>
        /// <para>Developer website.</para>
        /// </summary>
        [JsonProperty("website")]
        public string Website { get; set; } = "";

        /// <summary>
        /// <para>Developer Organization.</para>
        /// </summary>
        [JsonProperty("organization")]
        public string Organization { get; set; } = "";
    }
}
