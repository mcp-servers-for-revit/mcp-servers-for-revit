using Newtonsoft.Json;

namespace revit_mcp_plugin.Configuration
{
    /// <summary>
    /// <para>Service settings.</para>
    /// </summary>
    public class ServiceSettings
    {
        /// <summary>
        /// <para>Log level.</para>
        /// </summary>
        [JsonProperty("logLevel")]
        public string LogLevel { get; set; } = "Info";

        /// <summary>
        /// <para>Socket service port.</para>
        /// </summary>
        [JsonProperty("port")]
        public int Port { get; set; } = 8080;

    }
}
