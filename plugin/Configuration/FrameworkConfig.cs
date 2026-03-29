using Newtonsoft.Json;
using System.Collections.Generic;

namespace revit_mcp_plugin.Configuration
{
    /// <summary>
    /// <para>Framework configuration class.</para>
    /// </summary>
    public class FrameworkConfig
    {
        /// <summary>
        /// <para>Command configuration list.</para>
        /// </summary>
        [JsonProperty("commands")]
        public List<CommandConfig> Commands { get; set; } = new List<CommandConfig>();

        /// <summary>
        /// <para>Global settings.</para>
        /// </summary>
        [JsonProperty("settings")]
        public ServiceSettings Settings { get; set; } = new ServiceSettings();
    }
}
