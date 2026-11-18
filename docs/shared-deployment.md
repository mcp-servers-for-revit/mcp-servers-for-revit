# Shared deployment

Build the MCP server and the Release solution for each Revit year you support. From the repository root, publish each year to the same **new release directory** before pointing clients at it. For example, to publish Revit 2026 to a share, run the following PowerShell command.

```powershell
.\scripts\publish-network-build.ps1 -Year 2026 -NetworkRootPath '\\fileserver\RevitMCP\release-1'
```

The script publishes the available command sets, their dependencies and the bundled server. It seeds the shared command registry on first publish. Open Revit MCP Settings to enable the commands you want to expose, including the new parameter and material commands. The registry applies to everyone using this release directory. Give clients read access to the binaries and allow writes to the shared Logs directory. The administrator who manages command selection also needs write access to the registry.

Install the matching add-in and MCP server locally using the normal installation procedure. Add the following entries to `appSettings` in `RevitMCPPlugin.dll.config` beside `RevitMCPPlugin.dll`. Set the local path to the build directory your MCP client runs. Restart Revit to read the settings and restart the client to load a newly synced bundle.

```xml
<configuration>
  <appSettings>
    <add key="NetworkRootPath" value="\\fileserver\RevitMCP\release-1" />
    <add key="NetworkMcpServerBuildPath" value="\\fileserver\RevitMCP\release-1\Server\build" />
    <add key="LocalMcpServerBuildPath" value="%LOCALAPPDATA%\RevitMCP\Server\build" />
  </appSettings>
</configuration>
```

All three settings are optional. `NetworkRootPath` selects the shared Commands and Logs directories. Setting both build paths enables synchronization of `index.js` when the Revit service initializes. Leave these settings empty for a local installation. To use an existing corporate ribbon tab, set `RibbonTabName` to its name.

To hide individual tools from a particular MCP client, set `REVIT_MCP_DISABLED_TOOLS` in its server environment to a comma-separated list of tool names and restart the client. Command permissions are managed in Revit MCP Settings.
