# AGENTS.md

Guidance for agent work in this repository — a fork of `mcp-servers-for-revit` that extends the upstream Revit MCP plugin with an **off-axis line inaccuracy detection and remediation toolkit**.

---

## Repository Overview

This repo is a fork of [mcp-servers-for-revit](https://github.com/mcp-servers-for-revit/mcp-servers-for-revit). It retains the upstream MCP server + Revit plugin + command-set architecture and adds an off-axis remediation toolkit plus installer distribution.

| Directory | Purpose |
|---|---|
| `server/` | TypeScript MCP server (exposes tools to AI clients, bridges to the Revit plugin over WebSocket). |
| `plugin/` | C# Revit add-in (`revit_mcp_plugin`). Listens, dispatches commands, hosts the Settings UI. |
| `commandset/` | C# `RevitMCPCommandSet` — core command set (compiles to `RevitMCPCommandSet.dll`). |
| `offaxis-commandset/` | C# `OffAxisCommandSet` — the off-axis detection/fix command set (compiles to `OffAxisCommandSet.dll`), plus standalone ZIP distribution. |
| `dwg-commandset/` | C# `DwgCommandSet` — DWG import toolset (grid/model lines/walls/poche walls from placed DWG layers; compiles to `DwgCommandSet.dll`). Includes a pure-geometry core (`Geometry/`) used by unit tests. |
| `installer/` | Inno Setup packaging (`build-installer.ps1` generates `.iss`, compiles `mcp-servers-for-revit-setup.exe`). |
| `scripts/` | Release script (`release.ps1`) + DWG wall scorecard scorer (`score-walls.ps1`). |
| `tests/` | `dwg-geometry/` TUnit suite (36 tests) covering `dwg-commandset/Geometry`; run via the test exe directly (`dotnet test` crashes with this TUnit/MTP combo). |

The off-axis detector/fixer logic is **promoted into compiled C# commands** (see §Off-Axis Toolkit). The original Roslyn `.csx` prototype scripts are **frozen** in a separate archive repo (see §Frozen Script Archive) — do not treat them as live source.

---

## Off-Axis Toolkit

The core feature of this fork. Detect and align slightly off-axis lines (near 0°, 45°, 90°, 135°, 180°) across Revit model elements.

### Compiled MCP Tools

The 10 commands live in `offaxis-commandset/` and are exposed as MCP tools:

| MCP Tool Name | Type | Purpose | Key Parameters |
|---|---|---|---|
| `detect_off_axis_hybrid` | Detector | Warning-driven fast detection (uses Revit `Document.GetWarnings()`) | (none) |
| `detect_off_axis_lines` | Detector | Geometric scan for lines with angular deviation | `minAngleDeg`, `maxAngleDeg` |
| `detect_spacing_elements` | Detector | 1/4" lattice spacing regularizer detector | `limit` |
| `fix_off_axis_walls_and_beams` | Fixer | Snaps walls & structural framing | `elementIds`, `minAngleDeg`, `maxAngleDeg` |
| `fix_off_axis_grids` | Fixer | Rotates 3D grids about midpoint | `elementIds`, `minAngleDeg`, `maxAngleDeg` |
| `fix_off_axis_reference_planes` | Fixer | Snaps vertical reference plane normals | `elementIds`, `minAngleDeg`, `maxAngleDeg` |
| `fix_off_axis_sketches` | Fixer | Ray-ray closed sketch profile solver (Floors/Ceilings/Roofs) | `hostIds`, `lineIds`, `minAngleDeg`, `maxAngleDeg` |
| `fix_off_axis_inplace` | Fixer | In-place Component Extrusion sketch fixer | `hostIds`, `lineIds`, `minAngleDeg`, `maxAngleDeg` |
| `fix_off_axis_model_lines` | Fixer | Top-level model line & curve chain solver | `elementIds`, `minAngleDeg`, `maxAngleDeg` |
| `fix_spacing_elements` | Fixer | Iterative 1/4" lattice regularizer | `elementIds`, `maxMoveInches` |

### Architecture

- **`offaxis-commandset/Commands/OffAxis/*.cs`** — command declarations.
- **`offaxis-commandset/Services/OffAxis/*.cs`** — `ExternalEventCommandBase` + `IExternalEventHandler`/`IWaitableExternalEventHandler` handlers implementing the logic.
- **`offaxis-commandset/Utils/OffAxis/`** — `OffAxisGeometryUtils.cs` and `SilentFailuresPreprocessor.cs`.
- **`offaxis-commandset/command.json`** — command set declaration (name `OffAxisCommandSet`, developer Scott Mitchell Studio).
- **MCP tool definitions** in `server/src/tools/*.ts` (auto-registered via glob in `server/src/tools/register.ts`).

Commands only live in **one** assembly. Off-axis commands are **not** duplicated in `RevitMCPCommandSet`; the root `command.json` holds only core commands.

---

## Distribution

Two packages are produced:

1. **Full Plugin Installer** (`installer/output/mcp-servers-for-revit-setup.exe`) — built by `installer/build-installer.ps1`. Installs the add-in, both command sets, and configures AI clients (Claude Desktop, Cursor, opencode 1/2, AnythingLLM). `AppVersion` is **derived from the plugin** (`plugin/Properties/AssemblyInfo.cs` `AssemblyFileVersion`), not hardcoded.
2. **Standalone CommandSet ZIP** (`offaxis-commandset/dist/OffAxisCommandSet.zip`) — built by `offaxis-commandset/build-package.ps1`. Contains `OffAxisCommandSet`, `install.ps1`, `uninstall.ps1`, `README.md`. Merges 10 commands into an existing plugin's `commandRegistry.json`, or can be dropped in via Revit MCP Settings -> "Open CommandSet Folder" -> Refresh -> Save.

The installer exposes a **Command Sets selection step** (pre-selects all) plus an AI-client config step.

### Deployment Gotcha — Installer overwrites AI-client configs (RESOLVED as of v1.1.1)

As of **v1.1.1** this gotcha is fixed in the installer: `installer/build-installer.ps1` bundles the compiled MCP server with the exe (`{app}\Server\build\index.js` + `node_modules`), and `ConfigureClaudeDesktop` / `ConfigureCursor` / `ConfigureOpencode` write client configs that run the bundled server via `node` — never `npx`. Install-time logic also **upgrades pre-existing `npx`-based entries** in those configs to the bundled-server form. The npm package `mcp-server-for-revit@1.0.0` remains broken upstream (unfixed `ajv` dep), and this fork deliberately never depends on it. Historical context for anyone reverting:

```jsonc
// ~/.config/opencode/opencode.json — pre-v1.1.1 symptom was a forced npx entry
// of the broken npm package; if you hit that state, point back at the local build:
"mcp-server-for-revit": {
  "type": "local",
  "command": ["node", "C:\\dev\\mcp-servers-for-revit\\server\\build\\index.js"],
  "enabled": true
}
```

Verify the local server boots with a manual MCP handshake (`node server/build/index.js` from `server/`; respond to `initialize`; 33 tools register). Note: `better-sqlite3` has **no prebuilt binary for Node 26** and this machine lacks node-gyp/Python/VS, so the 3 sqlite-backed tools (`query_stored_data`, `store_project_data`, `store_room_data`) fail to register — the other 33 tools and the connection work fine.

As of **v1.1.4** the installer additionally **bundles a portable Node runtime** (`{app}\node\node.exe`, staged from nodejs.org v22 win-x64 at build time, cached in `installer/.node-cache/`) and a stdio launcher **shim** (`{app}\Server\run-mcp-server.cmd`: prefer `{app}\node\node.exe`, fall back to PATH `node`, exec `{app}\Server\build\index.js`). **All** AI-client entries - including `ConfigureAnythingLLM` (`%APPDATA%\anythingllm-desktop\storage\plugins\anythingllm_mcp_servers.json`) - are written as `cmd /c {app}\Server\run-mcp-server.cmd`, so staff machines need **no system Node install** for the standalone installer either; the entry-refresh logic now upgrades stale `npx`- AND direct-`node`-form entries to the shim form. (Suite-side AnythingLLM wiring in SMS-toolkit already used its own portable node + shim; the two never collide - the suite deploys the plugin/server directly and does not run this installer.) Invoke the installer build with `-Command` (e.g. `powershell -Command "& { .\installer\build-installer.ps1 ... }"`), **not** `-File` - with `-File`, `@(2024,2025,2026)` is split at the commas and `2025`/`2026)` bind positionally to later params (this once set `OutputDir=2025` and built only Revit 2024).

### OpenCode 2 Config (implemented)

- The installer writes OpenCode configs through `installer/opencode-config.ps1` (staged to `{app}\Installer`), not Inno string surgery. The helper edits JSONC as raw text, so comments and trailing commas survive; writes are validated and atomic.
- **Native V2 shape**: `mcp.servers.<name>` with `type: "local"`, `command: ["cmd","/c",shim]`, and `codemode: false` (Code Mode off so Revit tools stay directly callable). No `enabled` key in native entries.
- **Legacy shape** (OpenCode 1): `mcp.<name>` with `enabled: true`. OpenCode 2 still reads this deprecated shape, but it cannot express `codemode`.
- **Detection**: `DetectOpenCode` runs `opencode-config.ps1 -Action Detect` via a `dontcopy` temp extract during `InitializeWizard`. It checks the CLI (`opencode --version`), the desktop install (`%LOCALAPPDATA%\Programs\ai.opencode.desktop\OpenCode.exe` / Program Files), and the desktop bundled CLI (`%APPDATA%\ai.opencode.desktop\cli\<version>-<arch>\opencode-cli.exe`). Registry detection is not used (the desktop installer has no usable uninstall entry).
- **Selection**: known version auto-selects the shape (1.x legacy, 2.x native) and hides the native checkbox. Version unknown but OpenCode detected shows the checkbox as a prompt. No OpenCode detected skips OpenCode config entirely (the checkbox can still be forced).
- **Migration**: installing the native shape removes a legacy Revit entry; installing the legacy shape removes a native Revit entry. Both directions also clean the other config file, preserving unrelated servers/comments.
- Config dir honors `OPENCODE_CONFIG_DIR`, then `XDG_CONFIG_HOME\opencode`, then `%USERPROFILE%\.config\opencode`. OpenCode 2 loads both `opencode.json` and `opencode.jsonc`; `.jsonc` wins for overlapping keys. Native installs target `.jsonc` when it exists (otherwise `.json`); legacy installs target `.json`. Verified against V2 2.2.0: `OPENCODE_CONFIG_DIR` loads `opencode.jsonc` from that directory and `XDG_CONFIG_HOME` sets the config path. `OPENCODE_CONFIG` (a custom config file path) is not edited by this installer.
- Uninstall removes the Revit entry from both `.json` and `.jsonc` (both shapes) while preserving other servers/comments, even if OpenCode is no longer detected. `opencode-config.ps1 -Action Remove -ShimPath <path>` only removes an entry whose command matches that exact shim; a suite-owned entry is left untouched.
- Tests: `installer/test-opencode-config.ps1` (blocking CI job in `.github/workflows/ci.yml`) covers native/legacy add/update/migrate/remove, detection, `.jsonc` precedence, comments/trailing commas, idempotency, invalid-config rollback, and exact-shim ownership (`Verify`/`Remove`). `installer/verify-opencode-v2.ps1` is the optional real-CLI smoke (manual `opencode-v2-smoke` workflow); desktop-only machines skip it with `/mcps` instructions because `opencode-cli mcp list` only sees the desktop app's shared server while the app is running.
- `server/test/smoke.mjs` (`npm run test:smoke`) verifies the legacy stdio MCP handshake and `tools/list`; OpenCode 2's default `legacy` protocol uses this path.

---

### Tool Advertisement (implemented)

The Node server's tool list is no longer blindly static. The plugin serves its registry snapshot through the built-in **`get_registered_commands`** command (registered in `plugin/Core/SocketService.cs` alongside `reload_command_set`; pure registry read, no ExternalEvent), and the server folds the advertised tools to match (`server/src/utils/ToolAvailability.ts`):

- **Mapping**: MCP tool name → command name is same-name by default; overrides live in `TOOL_COMMAND_OVERRIDES` (`color_elements`→`color_splash`, `tag_all_rooms`→`tag_rooms`, `tag_all_walls`→`tag_walls`); sqlite tools (`query_stored_data`, `store_project_data`, `store_room_data`) are local and never filtered. **When adding a tool whose command name differs from the tool name, update `TOOL_COMMAND_OVERRIDES`.**
- **Fold mechanism**: `registerTools` captures each `server.tool()` `RegisteredTool` handle (monkey-patched during registration — tool files unchanged); availability flips `handle.enabled` directly (NOT `enable()`/`disable()` — those each emit `tools/list_changed`, flooding the client) and sends one batched `sendToolListChanged()`.
- **Sync points**: startup fire-and-forget probe in `index.ts`; per-connection TTL refresh (60s) in `ConnectionManager.withRevitConnection`; forced re-fetch after `reload_command_set`. A call against a known-missing command fails with an actionable "not enabled in the current Revit session" error (guard in `SocketClient.sendCommand`), on top of the SDK's own `Tool ... disabled` rejection.
- **Offline/compat stability**: while the plugin is unreachable the full tool list stays advertised (stdio UX must not depend on Revit being online); plugins without `get_registered_commands` are probed once ("Method ... not found"), then left on the full list forever (`pluginTooOld`). Old servers + new plugin also fine (the command is simply never called).
- **First-run prompt fix** (same change set): the "no commands enabled" check in `SocketService.Initialize` now excludes plugin built-ins — it previously counted `reload_command_set` and never fired.

## Suite direct-deploy contract

The SMS-Toolkit suite is the primary owner. It deploys the plugin/server directly from this repo's pinned release; it must **not** invoke this standalone installer.

- Same software, same `mcp-server-for-revit` client-config key. Do not introduce a distinct name.
- Ownership is by exact shim path, never by key alone:
  - suite: `{suite-app}\run-mcp-server.cmd`
  - standalone: `{app}\Server\run-mcp-server.cmd`
- Install may reclaim the key (last installer wins). Uninstall removes only an entry whose command matches that installer's own shim. `RemoveFromClaudeDesktop`, `RemoveFromCursor`, `RemoveFromAnythingLLM`, and `opencode-config.ps1 -Action Remove -ShimPath` enforce this.
- Fresh installs seed `commandRegistry.json` disabled. The plugin's own default registry creation is disabled too; existing registries are preserved on reinstall.
- Supported Revit years: 2020-2026. 2020-2023 are core-only; 2024-2026 include Dwg/OffAxis.
- Release artifacts consumed by the suite: per-year plugin zips, bundled server zip, portable Node runtime, launcher shim, `opencode-config.ps1`, `core-manifest.json`, and `commandRegistry.seed.json`.
- Standalone installer remains the testing/piecemeal-update vehicle. Suite-side client config auto-detection covers Claude Desktop, Cursor, OpenCode 1/2, and AnythingLLM; standalone remains the manual override.

---

## Off-Axis Toolkit Knowledge (durable)

The following learnings were hard-won during development and remain authoritative for the compiled commands.

### Safety Mechanisms & Thresholds

- **`Application.FailuresProcessing` Delegate Pipeline**: All fixers hook `document.Application.FailuresProcessing` using a lambda delegate (`try ... finally { app.FailuresProcessing -= handler; }`). It deletes warnings (`fa.DeleteWarning(msg)`) to suppress GUI popups and triggers silent rollback (`args.SetProcessingResult(ProceedWithRollBack)`) on errors, eliminating modal UI blocks.
- **Large-Fix Flags**: `FLAG_MOVEMENT_IN = 0.5` in; `FLAG_DEVIATION_DEG = 0.1` deg; predicted swing `= 2·L·sin(Δθ/2)`. Any element exceeding either threshold → `LargeFix = true`.
- **Pre-check filters**: pinned elements; hosted inserts; dimension-locked elements; profiles containing non-Line curves; gaps in profiles > 0.01 ft.

### Key Pitfalls & Critical Learnings

- **`OfClass(typeof(ModelCurve))` returns EMPTY** for standalone/top-level model curves. Collect with `OfClass(typeof(CurveElement))` and filter `e is ModelCurve`. Sketch-hosted category names contain `"Sketch"` and must be denied.
- **`OfClass(typeof(Extrusion))` / `OfClass(typeof(GenericForm))` return EMPTY** for in-place form elements. Enumerate warning-driven from `Document.GetWarnings()`: failing set is `[FamilyInstance host, form, ModelLine(<Sketch>)]`.
- **Form type detection** by `e.GetType().Name`: `"Extrusion"` is fixable; `"Blend"/"Sweep"/"Revolve"/"GenericForm"/"SweptBlend"` are advisory-only; categories `Mass`/`Toposolid` excluded.
- **In-place strategies**: (1) length-preserving midpoint rotation vs an **18-direction** world-axis candidate set (3 axes ± and 6 diagonals ±) — all 18 must be present else a true 135° diagonal reports ~45° and never snaps; (2) fallback corner-vertex ray-ray closure.
- **Sketch plane frame** built from member `ModelLine.SketchPlane.GetPlane()`; Plane uses `XVec`/`YVec` (via reflection), not `BasisX`/`BasisY`.
- **Sketch access differences**: Floors/Ceilings → `((Floor)el).SketchId` → `Sketch.Profile` (`CurveArrArray`). FootPrint Roofs → no `.SketchId`; use `roof.GetProfiles()` (`ModelCurveArrArray`).
- **Stale warnings**: `Document.GetWarnings()` can retain stale entries; always verify fixes by measuring geometry, not warning-list presence.
- **`IsJoined`/`IsJoinedWith` do not exist** in this build — use `JoinGeometryUtils.GetJoinedElements(doc, el)`.

### Pass B — 1/4" Lattice Spacing Regularizer

- **Lattice math**: for a segment parallel (±0.1°) to a grid with signed perpendicular offset `p`, phase `φ = p − G·floor(p/G)` (`G = 0.25 in`); nearest lattice line `s_n = φ + G·round((p−φ)/G)`; deviation `= (p − s_n)·12` in. `DeltaIn = 0` ⇔ on-lattice.
- **Detector exclusions**: pinned; dimension-referenced (walk `Dimension.References`); API-joined (`JoinGeometryUtils.GetJoinedElements`); bbox fully embedded in another wall's XY bbox (±0.02 ft); hosted by a `FamilyInstance` whose host is in wall set; length < 0.5 ft.
- **Fixer = live recompute, never CSV deltas**: after batch moves, Revit joint cleanup silently steals part of the move. Recompute current `p → φ → s_n →` shift on the fly, snap-commit, re-read, self-correct up to 3 passes.
- **CRITICAL**: never call `SetProcessingResult(ProceedWithCommit)` when there are no errors — it triggers an infinite failure-processing loop / UI lock / MCP timeout.
- **Batching**: keep batches to **10–12 element IDs per MCP call** to stay under the 120s window in large models.

### DWG Geometry Walk Safety (durable, learned from a hard crash)

- **Incident (v1.1.7.0, Revit 2024.3.6, SMS-SEA-L08)**: `extract_dwg_curves` on a linked DWG killed Revit with a **native stack overflow** — `0xc00000fd` faulting in Revit's own `Utility.dll` (Application Error 1000), right after a ~591 MB single-tick VM jump from full geometry materialization. Journal cut mid-write, no shutdown record.
- **Not a Roslyn crash**: the six `API_ERROR` "Assembly version conflict" journal lines fired earlier, at MCP-toggle time, from Revit's *static* add-in dependency checker (scans DLLs in the addin folder vs. preloaded assemblies). They are coincident noise — bind failures are `FileLoadException`/`MissingMethodException`/`TypeLoadException`, never stack overflows. The missing `%TEMP%\mcp_resolver_debug.log` confirmed the AssemblyResolve hook never fired.
- **Guardrail**: all recursive geometry walks in `dwg-commandset` must go through `DwgCurveSource.WalkGeometry` (depth cap `MaxGeometryWalkDepth = 50`, optional `GeometryWalkStats`, optional `cancelled` early-exit). `StackOverflowException` is **uncatchable** in .NET — managed try/catch (present in every handler) cannot save the process, so recursion is bounded structurally instead.
- **Residual risk**: a managed cap cannot prevent recursion *inside a single native call* (`GetInstanceGeometry()` on a pathological nested-block DWG, or `Tessellate()` on a hostile spline). If a user reports a Revit-native crash during DWG extraction again, suspect the DWG itself, not the plugin binding.
- **Tool params** (v1.1.9+): `extract_dwg_curves` gained `includeTessellation` (default `true`), `maxPointsPerCurve` (default 0 = unlimited, excess points omitted + `tessellatedTruncated` count), `countAll` (default `true`; `false` stops the walk at `maxCurves` so layer totals become partial). Handlers surface `depthCapped` in results when the 50-level cap was hit.

### Roslyn Conflict Hygiene (durable)

- `send_code_to_revit` compiles **in-process** via `Microsoft.CodeAnalysis.CSharp 4.8.0`; its dep chain (`System.Collections.Immutable` 7.0.0.0, `System.Reflection.Metadata` 7.0.0.0, `System.Memory` 4.0.1.2, `System.Threading.Tasks.Extensions` 4.2.0.1, `System.Text.Encoding.CodePages` 7.0.0.0, `Unsafe` 6.0.0.0, + Roslyn DLLs) ships beside `RevitMCPCommandSet.dll`, which is why that DLL is LoadFrom-resident (sibling probing for the JIT-lazy Roslyn load) and can't hot-reload.
- Other add-ins (Ideate BIMLink et al.) may preload **newer** copies of the same chain before the MCP toggle → Revit's static checker logs `API_ERROR` conflicts at toggle. These are noisy but benign; `ExecuteCodeEventHandler` now runs an advisory preflight (`GetConflictWarning`, higher-version preloads only — a *lower* preloaded copy like Revit 2024's own `System.Memory` 4.0.1.1 is the proven-benign case) and classifies bind-type exceptions into an explicit `"Compile subsystem unavailable — ... Do NOT retry"` envelope (also logged to `%TEMP%\mcp_compile_conflicts.log`). The server tool surfaces that as a stop signal.
- **Resolver policy**: `CommandSetLoader`'s AssemblyResolve hook returns a name-matched (version-mismatched) candidate **only when the request carries no version**. Returning wrong-version assemblies for versioned requests is not reliably discarded by the CLR and would poison other add-ins' late resolutions.
- **`legacy\` subfolder**: resolver honors exact-identity dep copies in `dir\legacy\` (e.g. Unsafe 4.0.4.1 alongside 6.0.0.0), but the build/installer deliberately never stages it — hand-place only when a concrete binding needs it.
- **Planned**: out-of-process compile host (Roslyn moves out of Revit's process entirely; addin folder goes clean). Deferred — the crash was native, not binding-related, but the host still removes journal noise, ~591 MB compile-residency, and the whole conflict class.

### Interaction Guidelines

- Report exact counts: **Fixed**, **Skipped (reason)**, and **LargeFix**; flag constraint-locked (dimensioned/curtain-wall-hosted) elements that rolled back safely.
- When dispatching to the compiled tools, pass `elementIds`/`hostIds`/`lineIds` as JSON arrays or CSV strings. For spacing passes use 10–12 IDs per call; all warnings/errors are handled headlessly by the `FailuresProcessing` delegate.
- Always set `transactionMode: "none"` when a script/command manages its own transactions.
- `send_code_to_revit` contract: the snippet runs inside `Execute(Document document, object[] parameters)` — use lowercase `document`, return with `return ...`, length-like values are in internal **feet**. Compile-error line numbers are 1-based snippet-relative (since v1.1.6). Failure results are labeled `"Code failed to execute."` instead of a success envelope; a `"Compile subsystem unavailable"` error means the Roslyn chain hit an assembly conflict — **stop retrying and tell the user to restart Revit**. A companion global opencode skill `revit-code-execution` (in the user's `~/.config/opencode/skills/`) carries the durable API lessons for agents.
- `reload_command_set` (v1.1.7+): plugin built-in byte-loads matching command-set DLLs by hash; `RevitMCPCommandSet.dll` itself is LoadFrom-resident (Roslyn dependency probing) so it can't hot-reload — restart Revit to refresh it. The tool returns the full registered-command list, useful to verify tool advertisement vs registration.
- Release workflow (as of v1.1.7): merge feature branches into `main`, bump version in `plugin/Properties/AssemblyInfo.cs` + `server/package.json` (+ `npm install --package-lock-only` in `server/`), build installer via `powershell -Command "& { .\installer\build-installer.ps1 }"` (R20–R23 dwg/offaxis build failures inside the installer are **expected** — those extras ship only for R24–R26), build `offaxis-commandset/build-package.ps1` if the off-axis set changed, commit the regenerated `.iss`, push, tag, `gh release create -R chris4d/mcp-servers-for-revit` (plain `gh` targets the upstream repo and 403s).`

---

## Frozen Script Archive

The original Roslyn `.csx` prototype scripts (detectors, fixers, spacing regularizer) and the full spec are **frozen and preserved** in the separate archive repo `revit-fix-line-innacuracy` (last state: branch `feature/failures-processing-pipeline`). They are **superseded** by the compiled `OffAxisCommandSet` commands in this repo and should be treated as historical reference only — the compiled commands are the live source of truth.

---

## Build & Conventions

- **Revit installed locally is 2024**; command sets build with `-c "Release R24"`.
- Repo targets Revit 2020–2026 (R20–R26) for the **core** set/plugin; the `dwg-commandset` / `offaxis-commandset` extras use Revit 2024+ APIs (`Floor.SketchId`, long ElementId values) and compile **only for R24/R25/R26** — release zips and the installer ship them for 2024–2026, core-only for 2020–2023.
- Plugin side-loads command sets from `%APPDATA%\Autodesk\Revit\Addins\{year}\revit_mcp_plugin\Commands\{SetName}\{year}\*.dll`, discovered via `commandRegistry.json` + `Assembly.LoadFrom`.
- **Commit only intentionally.** Don't push to upstream unless explicitly requested.

---

## Roadmap (next up)

Ordered backlog (v1.2.2 released: DWG walk caps, Roslyn conflict guards, dynamic tool advertisement, first-run prompt fix, OpenCode 2 installer/config support, suite alignment):

1. **v1.2.2 - RELEASED.** Re-release of v1.2.1 for the SMS-Toolkit suite alignment (same plugin/server behavior; refreshed version metadata and release assets).

2. **Out-of-process compile host — DEFERRED (recorded for future development; decided not to pursue in this cycle)**. Design captured from the v1.2 scoping discussion so it can be picked up cold:
   - **Motivation**: removes the ~591 MB Roslyn compile residency from Revit's process; kills the toggle-time `API_ERROR` journal noise (the Roslyn chain leaves the addin folder → nothing for Revit's static checker to flag) and the whole binding-conflict class; compile-path crashes/hangs (Roslyn OOM, parser blowups) take down the host instead of Revit; compile can move off Revit's UI thread (run on the WebSocket dispatch thread *before* raising the ExternalEvent — the 60s wait then wraps only execution).
   - **Shape**: new `compile-host/` project, `RevitMCPCompileHost.exe`, **net48** framework-dependent (proven Roslyn-on-net48 combo, no runtime bloat, works for Revit 2020–2026 since it's outside Revit). JSON-stdio protocol: `{protocolVersion, source, referencePaths[]}` → `{ok, assemblyB64, diagnostics[{line,severity,message}]}`. **Host owns the wrapper template + the 13-line diagnostic remap** (the 1-based snippet-line contract survives verbatim). Plugin sends `AppDomain.CurrentDomain.GetAssemblies()` file paths as the reference set (this preserves the hidden capability: snippets compile against *everything loaded in Revit*, other add-ins included — current code is file-based metadata anyway, so fidelity is identical).
   - **Plugin side**: `CompileHostClient` (connect-or-spawn, timeout + kill-tree, stderr → `%TEMP%` log); `ExecuteCodeCommand` compiles before `RaiseAndWaitForCompletion`; handler keeps `Assembly.Load(bytes)` + invoke + transaction + `ExecutionResultInfo` shape. Remove the Roslyn PackageReference + conflict preflight from the commandset; repurpose `ClassifyCompileFailure` for snippet-*runtime* bind failures (today those are misattributed to the compile chain). **No in-process fallback** (it would resurrect the noise/conflict class); missing-host error = "re-run installer", not "restart Revit".
   - **Packaging**: installer stages one `{app}\CompileHost\` folder for all Revit years; commandset folder goes clean. Watch for manual-DLL-drop skew (plugin updated via "Open CommandSet Folder", host stale) → `protocolVersion` in the handshake.
   - **Open decisions when picked up**: daemon-with-idle-exit vs spawn-per-request (daemon = no latency regression vs warm in-process; spawn = simplest); fold `RevitMCPCommandSet.dll` byte-load unlock (hot-reload via `reload_command_set`) into scope or not; whether host emits PDBs (today `Emit` has none, so snippet runtime stacks lack source lines — free upgrade opportunity).
   - **Steelman for keeping the status quo** (why this was deferred): the investigated crash was native geometry (already fixed by the walk caps), not compile; the v1.1.9 guards make the residual binding race loud and safe; the "compile against everything loaded" feature needs IPC marshaling; and a small-fork maintenance surface (protocol, lifecycle, orphans, AV heuristics on a self-spawning compiler exe, two logs to correlate) is permanent. `send_code_to_revit` is a convenience tool behind ~33 purpose-built ones.
