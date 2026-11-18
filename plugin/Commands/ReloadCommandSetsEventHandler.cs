using System;
using System.Collections.Generic;
using System.Threading;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCPSDK.API.Interfaces;
using revit_mcp_plugin.Core;

namespace revit_mcp_plugin.Commands
{
    public class ReloadCommandSetsEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        private readonly ManualResetEvent _resetEvent = new ManualResetEvent(false);
        public string AssemblyNameFilter { get; set; }
        public object Result { get; private set; }

        public void Execute(UIApplication app)
        {
            try
            {
                var socket = SocketService.Instance;
                var commandManager = socket.PluginCommandManager;
                var configManager = socket.PluginConfigManager;
                if (commandManager == null)
                {
                    Result = new Dictionary<string, object> { ["error"] = "Plugin command manager not initialized" };
                    return;
                }

                int configs = commandManager.ReloadCommands(AssemblyNameFilter);

                var registered = new List<string>();
                var registryConcrete = socket.PluginCommandRegistry as RevitCommandRegistry;
                if (registryConcrete != null)
                    foreach (var name in registryConcrete.GetRegisteredCommands())
                        registered.Add(name);

                Result = new Dictionary<string, object>
                {
                    ["success"] = true,
                    ["configsReloaded"] = configs,
                    ["registeredCommands"] = registered
                };
            }
            catch (Exception ex)
            {
                Result = new Dictionary<string, object> { ["error"] = ex.Message };
            }
            finally
            {
                _resetEvent.Set();
            }
        }

        public bool WaitForCompletion(int timeoutMilliseconds = 60000)
        {
            _resetEvent.Reset();
            return _resetEvent.WaitOne(timeoutMilliseconds);
        }

        public string GetName()
        {
            return "Reload Command Sets";
        }
    }
}
