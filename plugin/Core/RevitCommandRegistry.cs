using RevitMCPSDK.API.Interfaces;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace revit_mcp_plugin.Core
{
    public class RevitCommandRegistry : ICommandRegistry
    {
        private readonly ConcurrentDictionary<string, IRevitCommand> _commands = new ConcurrentDictionary<string, IRevitCommand>();

        public void RegisterCommand(IRevitCommand command)
        {
            _commands[command.CommandName] = command;
        }

        public bool TryGetCommand(string commandName, out IRevitCommand command)
        {
            return _commands.TryGetValue(commandName, out command);
        }

        public void ClearCommands()
        {
            _commands.Clear();
        }

        public IEnumerable<string> GetRegisteredCommands()
        {
            return _commands.Keys;
        }
    }
}
