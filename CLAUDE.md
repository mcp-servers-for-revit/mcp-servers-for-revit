# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project overview

This repo (a fork of `mcp-servers-for-revit/mcp-servers-for-revit`) has three independently-built components that together let an MCP client (Claude, etc.) drive Autodesk Revit:

```
MCP Client <--stdio--> server/ (TypeScript)  <--TCP/JSON-RPC--> plugin/ (C#, runs inside Revit) --loads--> commandset/ (C#, actual Revit API calls)
```

- `server/` — MCP server (Node/TypeScript). Exposes one MCP tool per Revit operation.
- `plugin/` — the Revit add-in (`revit_mcp_plugin`). Runs inside Revit, hosts a raw TCP socket server, dispatches incoming JSON-RPC calls to registered commands, and shares the existing **"ModuleFabrique"** ribbon tab (installed alongside this org's other in-house add-ins) rather than creating its own tab.
- `commandset/` — the actual `IRevitCommand` implementations (`RevitMCPCommandSet`) that call the Revit API. Built and loaded as a separate assembly, dynamically discovered by the plugin at runtime (not statically referenced). Also owns `command.json` (manifest) and `commandRegistry.json` (enabled-state) as the repo-managed source of truth for both.
- `tests/commandset/` — integration tests (TUnit) that run against a live Revit process.

`origin` points at the user's own fork (`Modulefabrique/mcp-servers-for-revit`), not the upstream repo — a normal `git push` only reaches the fork. This fork only builds for **Revit 2024 and 2026** (configurations trimmed from the upstream's 2020-2026 range to match what's actually used here).

## Team-wide network deployment

This fork centralizes install/config state on a network share (`O:\02 - Software\021 - Revit\0221 - API\MCP`, referred to below as `NetworkRootPath`) so a team can manage the plugin, commandset and MCP-server tools from one place instead of per-machine. This is layered on top of the upstream project entirely via config + MSBuild/npm targets — no change to the core dispatch logic.

- **`plugin/RevitMCPPlugin.dll.config`** (appSettings, read once via `PathManager.GetConfigSetting(key)`): `NetworkRootPath` overrides where `PathManager.GetCommandsDirectoryPath()`/`GetLogsDirectoryPath()` look — set, they resolve to `<NetworkRootPath>\Commands` / `\Logs` instead of the folder next to the plugin DLL. `NetworkMcpServerBuildPath` / `LocalMcpServerBuildPath` configure the MCP-server sync described below. Any setting left empty disables that piece of centralization and falls back to local-only behavior.
- **`plugin/Core/McpServerSyncService.cs`**: on every plugin startup (`SocketService.Initialize()`), recursively mirrors the *entire* MCP-server build (`index.js`, `register.js`, `utils/`, `tools/` — everything) from `NetworkMcpServerBuildPath` down to `LocalMcpServerBuildPath`: copies new/changed files, deletes local files no longer present on the network side. No exceptions (not even `register.js`) — the network side is expected to always be a complete, self-consistent build.
- **`plugin/Utils/Logger.cs`**: log filenames include the Windows username (`mcp_{yyyyMMdd}_{username}.log`) so team members sharing a network `Logs` folder don't write into the same file.
- **`plugin/Core/CommandManager.cs`**: loads command assemblies via `File.ReadAllBytes` + `Assembly.Load(byte[])` rather than `Assembly.LoadFrom`, specifically to dodge .NET Framework's `loadFromRemoteSources` block when `Commands` resolves to a network path (relevant for the net48 2024 target; net8 2026 has no such restriction). Because `Assembly.Load(byte[])` has no associated file path, the CLR can no longer auto-resolve that assembly's own co-located dependencies the way `LoadFrom` did — `CommandManager` registers an `AppDomain.AssemblyResolve` handler that searches every folder a command assembly was loaded from (tracked in a static `_knownAssemblyDirectories` set) and loads matches the same way. `ReflectionTypeLoadException.LoaderExceptions` is logged explicitly on failure (the bare exception message alone doesn't say which dependency is missing).
- **`server/package.json`**: `npm run build:debug` compiles and copies `build/` to a fixed local path (`C:\Users\t.vwolferen\revit-mcp\build`, matching the path configured in `claude_desktop_config.json` for immediate manual testing); `npm run build:release` compiles and copies to the network share (`NetworkRootPath\Server\build`) for the whole team to pick up via `McpServerSyncService`. Both are thin wrappers: `build` (`tsc`) plus a `postbuild:debug`/`postbuild:release` npm-lifecycle hook doing the `xcopy`.
- **`plugin`/`commandset` `.csproj` Debug/Release targets**: both projects share the same `bin\<Debug|Release>\<RevitVersion>\` output layout. Debug needs no extra step (plain build output is enough to test locally). Release additionally copies:
  - `plugin`: `CopyToNetworkAddinFolder` target copies `*.dll`/`*.pdb`/`*.config` (deliberately **not** the `.addin` manifest, which is managed separately) to `O:\...\AddinFiles\<RevitVersion>\ModuleFabriqueAddin\` — the same network folder this org's other add-ins are published from.
  - `commandset`: `DeployCommandSet` target copies `*.dll`/`*.pdb` to `<CommandsRootDir>\Commandset\<RevitVersion>\`, plus `command.json` (to `Commands\Commandset\`) and `commandRegistry.json` (to `Commands\`) sourced straight from the repo (`commandset/command.json`, `commandset/commandRegistry.json`) so those two files stay centrally managed in git. `CommandsRootDir` is the local test folder in Debug, `NetworkRootPath\Commands` in Release.

## Commands

### MCP server (`server/`)
```bash
cd server
npm install
npm run build          # tsc -> server/build/, no copy
npm run build:debug    # tsc + copy to the local test folder (see above)
npm run build:release  # tsc + copy to the network share, for the whole team
npx tsx server/src/index.ts   # run directly during development, no build needed
```

### Plugin + command set (C#)
Open `mcp-servers-for-revit.sln` (contains plugin, commandset, and tests projects). Configurations are `Debug R24`/`Debug R26`/`Release R24`/`Release R26` — the Revit year is part of the configuration name and selects both the target framework and `RevitVersion` MSBuild property (see "Multi-version targeting" below). See "Team-wide network deployment" above for what Debug vs. Release actually copies where.

### Tests (`tests/commandset/`)
Requires a running, fully-loaded Revit 2026 instance (tests inject into it — no separate addin install needed) and the .NET 10 SDK.
```bash
dotnet test -c "Debug R26" -r win-x64 tests/commandset
```
`-r win-x64` is required on ARM64 machines (Revit API assemblies are x64-only). Test classes extend `RevitApiTest`; setup/teardown use `[Before(HookType.Class)]` / `[After(HookType.Class)]` with `[HookExecutor<RevitThreadExecutor>]` to run on Revit's API thread.

### Releasing
A single `v*` tag drives everything via `.github/workflows/release.yml` (this workflow still targets the upstream's full 2020-2026 matrix and npm publish — it hasn't been adapted to this fork's R24/R26-only, network-deployed setup, so treat it as inherited/likely stale rather than the actual release path used day-to-day here).

## Architecture

### Transport is raw TCP, not WebSocket
Despite the (now-removed) README diagram saying "WebSocket", the actual transport is a plain TCP socket: `plugin/Core/SocketService.cs` runs a `TcpListener` on port 8080, and `server/src/utils/SocketClient.ts` (`RevitClientConnection`) connects with Node's `net.Socket`. Messages are JSON-RPC 2.0 objects serialized with no delimiter; both sides attempt `JSON.parse`/`JsonConvert.DeserializeObject` on the accumulated buffer and treat a parse failure as "wait for more data" (see `processBuffer` in `SocketClient.ts`).

### MCP server tool auto-discovery
`server/src/tools/register.ts` scans its own directory at startup and dynamically imports every `.ts`/`.js` file except `index`/`register` themselves, calling whichever exported function name starts with `register` (e.g. `registerGetCurrentViewInfoTool`). **Adding a new tool means only dropping a new file in `server/src/tools/` that exports a `registerXTool(server)` function** — no manual wiring in `register.ts` or `index.ts`. Each tool handler goes through `withRevitConnection` (`server/src/utils/ConnectionManager.ts`), which serializes all Revit connections behind a single mutex (`connectionMutex`) so concurrent tool calls from the MCP client don't race on the one TCP connection, then calls `revitClient.sendCommand(method, params)`. In this fork, whatever the team publishes to the network build (see above) becomes every team member's local `tools/` folder on next Revit start — adding a team-wide tool means dropping the compiled `.js` in the network build's `tools/` folder, not editing each machine.

### Plugin-side command dispatch
`SocketService.ProcessJsonRPCRequest` looks the JSON-RPC method name up directly in an `ICommandRegistry` and calls `command.Execute(params, requestId)` itself (the separate `CommandExecutor` class exists but is not actually wired into this path — don't assume it runs). The registry is populated at plugin startup by `CommandManager.LoadCommands()`, which:
1. Reads enabled commands + their assembly path from a config produced by `ConfigurationManager` (backed by the WPF Settings window in `plugin/UI/`, which reads `command.json` — the manifest of *available* commands shipped with each `commandset` build — and writes a separate registry file recording which ones the user *enabled*). In this fork both files normally live under the network `Commands` folder (see above), so enabling/disabling a command from any one machine's Settings window is already team-wide.
2. Loads each configured assembly via `Assembly.Load(File.ReadAllBytes(path))` (see "Team-wide network deployment" for why, and for the `AssemblyResolve` handler this requires) and reflects over its types for anything implementing `RevitMCPSDK.API.Interfaces.IRevitCommand`, instantiating it (optionally via `IRevitCommandInitializable.Initialize(uiApplication)` or a `UIApplication`-accepting constructor).

This is why `commandset` is never project-referenced by `plugin` — it's a plugin-in, loaded purely by convention (command name match) at runtime. **Pitfall**: a command assembly built against a different version of the `RevitMCPSDK` package than the currently-running plugin will silently fail to register (no matching type, no log line) even if a same-named command class exists in it — .NET type identity includes the defining assembly, so an old `IRevitCommand` and the plugin's current one are different types. Symptom is `Method '<name>' not found` from `SocketService` with nothing informative in the log; check the command assembly's actual build date/origin before assuming a config problem.

### ExternalEvent pattern (Revit API thread-affinity)
Revit API calls must happen on Revit's UI thread, but the socket listener runs on its own thread. Every command in `commandset/Commands/` extends `RevitMCPSDK`'s `ExternalEventCommandBase`, pairing itself with a handler in `commandset/Services/` that implements `IExternalEventHandler` + `IWaitableExternalEventHandler`. The flow: `Command.Execute()` calls `RaiseAndWaitForCompletion(timeoutMs)`, which raises the paired `ExternalEvent` (created/cached per-key by the singleton `plugin/Core/ExternalEventManager.cs`) and blocks on a `ManualResetEvent`; Revit invokes the handler's `Execute(UIApplication)` on its own thread once free, which does the actual Revit API work, stores the result on the handler instance, and signals the event. The command then reads the result back off the handler. Two rules every handler must follow: (1) `_resetEvent.Reset()` happens *before* the raise — in the handler's `SetParameters(...)` or a `ResetCompletion()` method the command calls first — never inside `WaitForCompletion()` (resetting there, after the raise, races with a fast handler and loses its signal → spurious timeout); (2) no `TaskDialog.Show` in handlers or the utils they call — a modal dialog blocks the MCP request until someone clicks OK; put the error in a result/`ErrorMessage` property the command turns into an exception instead. When adding a new Revit operation, this Command+EventHandler pair (in `Commands/` and `Services/` respectively) is the pattern to follow — see `GetCurrentViewInfoCommand.cs` / `GetCurrentViewInfoEventHandler.cs` for the minimal example.

### Multi-version Revit targeting
Both `plugin` and `commandset` `.csproj`s define per-Revit-year configurations, now only `Debug R24`/`Debug R26`/`Release R24`/`Release R26`, each setting `RevitVersion` and `TargetFramework`: **net48** for Revit 2024, **net8.0-windows10.0.19041.0** for 2026. Conditional compilation symbols `REVIT2022_OR_GREATER`, `REVIT2023_OR_GREATER`, `REVIT2024_OR_GREATER` gate API differences (e.g. `ElementId.Value` vs `.IntegerValue`) and are all defined for both remaining targets. `RevitMCPSDK` (the base classes referenced everywhere — `IRevitCommand`, `ExternalEventCommandBase`, `ICommandRegistry`, `IWaitableExternalEventHandler`, `RevitVersionAdapter`, JSON-RPC models) is an external NuGet package version-pinned to `$(RevitVersion).*` — its source is not part of this repo. `commandset` additionally references `Microsoft.CodeAnalysis.CSharp` (Roslyn, for the `send_code_to_revit` dynamic-code command) — on the net48 (2024) target this transitively pulls in several `System.*` BCL-compat shims (`System.Memory`, `System.Buffers`, `System.Collections.Immutable`, etc.) that must ship alongside `RevitMCPCommandSet.dll`; net8 (2026) doesn't need them since the runtime already provides that surface.

### Ribbon UI
`plugin/Core/Application.cs` adds a single toggle button to the existing **"ModuleFabrique"** ribbon tab (not its own tab — `CreateRibbonPanel` targets that tab by name and throws a descriptive exception if the ModuleFabrique add-in hasn't loaded first). `MCPServiceConnection` (the button's click handler) starts/stops `SocketService` and calls `Application.UpdateToggleButtonIcon(isRunning)`, which swaps the button's icon between `Core/Ressources/mcp-server-black-*.png` (stopped) and `mcp-server-orange-*.png` (running) so the server state is visible at a glance.

## Conventions

- This fork's code comments, XML doc comments, and user-facing error/log strings are maintained in **Dutch** (the upstream project's leftover Chinese comments were translated to Dutch, not English) — match this when writing new comments, and prefer existing Dutch technical terms already used in the codebase (e.g. `element`, `aanzicht`/`weergave`, `wand`, `peil`, `familietype`, `parameter`) over inventing new ones.
- `LICENSE`, `README.md`, and `assets/` have been removed from this fork — don't recreate them without checking with the user first.
