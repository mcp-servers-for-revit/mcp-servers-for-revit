using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using RevitMCPSDK.API.Interfaces;
using revit_mcp_plugin.Core;

namespace revit_mcp_plugin.Commands
{
    /// <summary>
    /// Built-in introspection command: returns the names of all commands
    /// currently registered in the plugin (command sets + plugin built-ins).
    /// The MCP server polls this to keep its advertised tool list honest
    /// about what Revit can actually dispatch (dynamic tool advertisement).
    /// Pure registry read - no ExternalEvent / no Revit API access needed.
    /// </summary>
    public class GetRegisteredCommandsCommand : IRevitCommand
    {
        private readonly RevitCommandRegistry _commandRegistry;

        public GetRegisteredCommandsCommand(RevitCommandRegistry commandRegistry)
        {
            _commandRegistry = commandRegistry;
        }

        public string CommandName => "get_registered_commands";

        public object Execute(JObject parameters, string requestId)
        {
            var names = new List<string>();
            foreach (var name in _commandRegistry.GetRegisteredCommands())
            {
                names.Add(name);
            }
            names.Sort();
            return new { commands = names };
        }
    }
}