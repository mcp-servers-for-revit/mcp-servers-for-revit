using Newtonsoft.Json;
using RevitMCPSDK.API.Interfaces;

namespace revit_mcp_plugin.Configuration
{
    /// <summary>
    /// <para>Command configuration class.</para>
    /// </summary>
    public class CommandConfig
    {
        /// <summary>
        /// <para>Name of the command. Corresponds to <see cref="IRevitCommand.CommandName"/></para>
        /// </summary>
        [JsonProperty("commandName")]
        public string CommandName { get; set; }

        /// <summary>
        /// <para>Assembly path - DLL containing this command.</para>
        /// </summary>
        [JsonProperty("assemblyPath")]
        public string AssemblyPath { get; set; }

        /// <summary>
        /// <para>Enable this command.</para>
        /// </summary>
        [JsonProperty("enabled")]
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// <para>Supported Revit versions.</para>
        /// </summary>
        [JsonProperty("supportedRevitVersions")]
        public string[] SupportedRevitVersions { get; set; } = new string[0];

        /// <summary>
        /// <para>Developer information.</para>
        /// </summary>
        [JsonProperty("developer")]
        public DeveloperInfo Developer { get; set; } = new DeveloperInfo();

        /// <summary>
        /// <para>Command description.</para>
        /// </summary>
        [JsonProperty("description")]
        public string Description { get; set; } = "";

        /// <summary>
        /// <para>UI-only grouping category (not serialized).</para>
        /// </summary>
        [JsonIgnore]
        public string GroupCategory { get; set; } = "Other";
    }
}
