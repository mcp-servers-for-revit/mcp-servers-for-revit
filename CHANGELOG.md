# Changelog

All notable changes to this fork are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.2.2] - 2026-11-03

### Changed

- Re-released for the SMS-Toolkit suite alignment. Plugin/server behavior is
  unchanged from 1.2.1; version metadata and release assets are refreshed so
  the suite can pin a fresh tag.

## [1.2.1] - 2026-11-03

### Fixed

- Installer: the AI-client config JSON sanity guard rejected **every** config
  containing a comma (`Pos(',', S)` instead of `Pos(',,', S)`), so Claude
  Desktop, Cursor, and AnythingLLM config writes all failed with "Unable to
  update the … AI-client config safely" and the file was left untouched. Most
  visible with AnythingLLM, whose checkbox is auto-selected whenever the app is
  installed. The rejection dialog now directs the user to contact the developer
  with the installer log.
- Installer: OpenCode detection emitted all fields on one space-joined line
  (PowerShell parsed the unparenthesized `+` expressions in the `@(...)` array
  as a single string), so the installer saw `installed=1 version=...` and
  treated OpenCode as absent. Detection now writes one field per line; a
  regression assertion covers the line count.
- Installer: custom completion and config-error dialogs now use
  `SuppressibleMsgBox`, so `/VERYSILENT /SUPPRESSMSGBOXES` no longer hangs on
  the final "Installation complete" message box.
- Installer: removed the empty vertical gap left by the hidden OpenCode V1/V2
  sub-checkbox when the OpenCode version is known.
- Plugin: the default `commandRegistry.json` now writes every discovered command
  as disabled, so fresh deployments (standalone, suite, or manual ZIP) require
  an explicit user opt-in instead of enabling everything.
- Installer: client-config uninstall now removes only entries that point at this
  installer's exact shim path; a suite-owned entry is left untouched. OpenCode
  `Remove`/`Verify` accept `-ShimPath` and enforce the same ownership rule.

### Added

- Standalone installer now builds for Revit 2020-2026 (2020-2023 core-only;
  2024-2026 include Dwg/OffAxis). Release assets also include
  `opencode-config.ps1`, `core-manifest.json`, and
  `commandRegistry.seed.json` for suite consumption.
- Suite direct-deploy contract documented: the SMS-Toolkit suite owns direct
  deployment and client auto-config; this standalone installer remains the
  testing/piecemeal-update override.
- OpenCode 2 support: the installer detects the CLI or the desktop app (including
  its bundled `opencode-cli.exe`), writes the native `mcp.servers` shape with
  `codemode: false` (direct tool listing instead of Code Mode), honors
  `OPENCODE_CONFIG_DIR` / `XDG_CONFIG_HOME`, and preserves `.jsonc` comments
  and trailing commas. Known versions auto-select the shape; unknown versions
  prompt; no OpenCode installation skips the config entirely. Existing legacy
  configs are migrated to native when OpenCode 2 is detected, and native configs
  are converted back to legacy when OpenCode 1 is detected.
- `installer/opencode-config.ps1` replaces the Inno string surgery for OpenCode
  with a validated, atomic JSONC-aware editor. Uninstall removes the Revit
  entry from both `opencode.json` and `opencode.jsonc` without disturbing other
  servers.
- Tests: `installer/test-opencode-config.ps1` (CI job) and
  `server/test/smoke.mjs` (`npm run test:smoke`). Optional
  `installer/verify-opencode-v2.ps1` + manual `opencode-v2-smoke` workflow
  validate the real OpenCode 2 CLI.

## [1.1.9] - 2026-09-30

### Added

- Dynamic tool advertisement: the plugin serves its registered-command list
  through a new `get_registered_commands` built-in, and the MCP server folds
  the advertised `tools/list` to match — Revit-backed tools whose command is
  not registered are disabled/hidden and calls fail with a clear "not enabled"
  error. Offline behavior stays stable (while the plugin is unreachable the
  full list is advertised); plugins predating `get_registered_commands` are
  probed once and then left on the full list. Also batch-notifies
  `tools/list_changed` (one notification per availability change, not one per
  tool).
- DWG geometry walk safety: shared depth-capped walk (`DwgCurveSource.WalkGeometry`,
  `MaxGeometryWalkDepth = 50`) used by all four recursive geometry walks
  (extract curves, model lines, poche walls, layer collection). Hardens against
  the SMS-SEA-L08 crash (Revit 2024.3.6, `0xc00000fd` native stack overflow in
  Revit's `Utility.dll` after `extract_dwg_curves` on a pathological DWG).
- `extract_dwg_curves` params: `includeTessellation` (default `true`),
  `maxPointsPerCurve` (default 0 = unlimited; excess points omitted with
  `tessellatedTruncated` count), `countAll` (default `true`; `false` stops the
  walk at `maxCurves`, making layer totals partial). Results now carry
  `depthCapped` when the walk hit the depth limit.
- `send_code_to_revit` Roslyn conflict guard: advisory preflight surfaces
  assembly-conflict warnings in the result envelope (`warnings`; also logged to
  `%TEMP%\mcp_compile_conflicts.log`), and bind-type failures (FileLoad/
  MissingMethod/TypeLoad/BadImageFormat) are classified into an explicit
  `"Compile subsystem unavailable — Do NOT retry"` error. The server tool turns
  that into a stop signal for agents.

### Fixed

- Resolver hook (`CommandSetLoader`) no longer returns name-matched,
  version-mismatched assemblies for versioned requests (CLR does not reliably
  discard them — they would poison other add-ins' late resolutions). Name-match
  fallback now applies only to simple-name (version-less) requests.
- First-run prompt logic counted plugin built-ins as loaded commands, so the
  "enable commands in Settings" prompt never showed; built-ins are now excluded
  from the check.
- Removed three empty placeholder tool files (`modify_element.ts`,
  `search_modules.ts`, `use_module.ts`) that produced register-time warnings.

## [1.1.8] - 2026-09-24

### Fixed

- Installer: `ReplaceNpxServerEntry` could corrupt AI-client configs with an
  orphaned key prefix; added a pre-save JSON sanity guard + logging and a
  15-assertion harness (`installer/test-client-config-logic.ps1`) (`6aad2e5`).

## [1.1.7] - 2026-09-24

### Added

- `reload_command_set` built-in plugin command + MCP tool: live-reload of
  command set DLLs after a build, without restarting Revit. Command set
  assemblies are byte-loaded (SHA-256 hash cached) so files on disk are never
  locked by Revit; `RevitMCPCommandSet.dll` stays LoadFrom-resident (Roslyn
  sibling probing) and needs a Revit restart to refresh (`f3d6ea2`, `88da0ab`).
- DWG poche wall extraction quality series (scorecard F1 0.78 → 0.85):
  pocket-door slot/strip culling, `MergeGapFt` sweeps (→ 3.0) with
  join-failure retry (`DisallowWallJoinAtEnd`), post-merge fragment/band/stub
  dedup, end-extension + junction-evidence bridging, even-odd hatch-hole
  containment + face-level run pairing (kills phantom walls across cavities)
  (`fdba0e5`, `13c328f`, `389545c`, `9138de6`, `5c1ea64`, `a0ede80`,
  `5174e67`, `0d1c07a`, `1dc35e0`).
- Pure geometry core (`dwg-commandset/Geometry`) with behavior-locked TUnit
  test suite; wall scorecard scorer (`scripts/score-walls.ps1`) + golden-target
  fixtures (`05b738b`, `0e3bb63`, `347cf96`, `57facee`).
- AssemblyResolve hook: probe command-set directories for byte-loaded
  assemblies, exact-version preference, `legacy\` subfolder for exact-identity
  older deps, resolver debug trace (`db2162a`, `6d3a0da`, `41fb29c`).
- `commandRegistry.json` seed template (38 commands, all enabled) (`d6674f4`).

### Changed

- Per-wall `SubTransaction` in the poche build loop — one un-buildable wall no
  longer rolls back the whole batch (`0203a67`); per-wall transactions with
  forced-modal handling off + app-level `FailuresProcessing` delegate that
  suppresses error modals (`37cc705`, `4c77137`); per-stage stopwatch timings,
  per-pair build-failure capture, and `revitFailureLog` in the response
  (`3365cbf`, `13c328f`).

### Fixed

- Doubled backslashes shown in AI-client captions (Inno Pascal does not treat
  backslash as an escape) (`dab7229`).

## [1.1.6] - 2026-09-23

### Changed

- Scrubbed and translated all Chinese comments and user-facing strings to
  English across commandset, plugin, and server; locale-dependent
  parameter/type-name adaptors left untouched (`5f4a799`, `81218be`, `efb4d5e`).

## [1.1.5] - 2026-09-22

### Added

- `send_code_to_revit` execution-contract tool guidance, snippet-relative
  1-based compile-error line numbers, and an explicit failure envelope
  (`Code failed to execute.`) instead of a success envelope (`bb2bfbb`).

## [1.1.4] - 2026-09-16

### Added

- Installer bundles a portable Node runtime + stdio shim; all AI-client entries
  (including AnythingLLM) run via `cmd /c` shim — no system Node required on
  target machines (`1736244`).
- Registry self-heal at startup, name-drift diagnostics, and lossless Settings
  Save (`9efad63` via `d77f5cf`).
- Live state indicator on the Revit MCP Switch button with modeless toast
  (`1698994` via `bc86728`).

## [1.1.3] - 2026-09-16

### Added

- Release pipeline: standalone command-set zips + bundled server zip asset
  attached to releases; extra command sets (dwg/offaxis) build for Revit
  2024–2026 only (2024+ APIs) (`7c14691`, `d385e6d`).

### Fixed

- Installer `SkipPluginBuild` path no longer skips staging the Server
  directory (`e3ee942` via `172d8f5`).

## [1.1.2] - 2026-09-04

### Added

- AnythingLLM client configuration in the installer (`3689ce3`), alongside
  Claude Desktop, Cursor, and opencode.
- Geometry tuning for `create_walls_from_dwg_poche`: `MergeGapFt` 0.5 → 0.8
  (`dbf18eb`), `minWallLengthFt` default 3.5 → 2.0 (`383fd70`),
  `maxWallThicknessFt` relaxed to 5ft default, JambRatio gate disabled (`d2c0039`).
- Pure 2D geometry core (`dwg-commandset/Geometry/PocheGeometryCore.cs`) extracted
  from Revit API code, with behavior-locked unit tests (`tests/dwg-geometry`,
  `05b738b`) and a `WallComparator` target-layout scorer (`0e3bb63`).

### Fixed

- `build-installer.ps1 -SkipPluginBuild`: staged `Server` directory size cast to
  int failed on large trees; now `TryParse`-gated (`e3ee942`).
- Poche face pairing gated by hatch-fill containment, eliminating ghost walls in
  open areas (`619b2f1`).

## [1.1.1] - 2026-08

### Added

- Inno Setup installer bundles the compiled MCP server and configures AI clients
  to point at it instead of `npx` (`5d59063`).
- CI release build uses Node 22 (`8bff8f9`, `79fd6a1`).

## [1.1.0] - 2026-08

### Added

- **Off-Axis toolkit** (`offaxis-commandset/`): 10 compiled MCP commands for
  detecting and snapping near-axis model geometry
  (`detect_off_axis_hybrid`, `detect_off_axis_lines`, `detect_spacing_elements`,
  `fix_off_axis_walls_and_beams`, `fix_off_axis_grids`,
  `fix_off_axis_reference_planes`, `fix_off_axis_sketches`,
  `fix_off_axis_inplace`, `fix_off_axis_model_lines`, `fix_spacing_elements`).
- **DWG command set** (`dwg-commandset/`): `create_walls_from_dwg_layer`,
  `create_grids_from_dwg_layer`, `create_model_lines_from_dwg_layer`,
  `create_walls_from_dwg_poche` — generate Revit elements from imported/linked
  DWG linework and hatch fills.
- Inno Setup-based Windows installer (`installer/`) that installs the add-in,
  command sets, bundled MCP server, and configures AI clients.

### Fixed

- Revit 2025/2026 compatibility: `ElementId.IntegerValue` (removed in 2025)
  swapped for `GetIntValue` across off-axis handlers (`14286eb`).
- Release workflow builds the installer for Revit 2024-2026 and no longer
  publishes the fork-broken npm package (`2d4effd`).

[Unreleased]: https://github.com/chris4d/mcp-servers-for-revit/compare/v1.2.2...HEAD
[1.2.2]: https://github.com/chris4d/mcp-servers-for-revit/compare/v1.2.1...v1.2.2
[1.2.1]: https://github.com/chris4d/mcp-servers-for-revit/compare/v1.1.9...v1.2.1
[1.1.9]: https://github.com/chris4d/mcp-servers-for-revit/compare/v1.1.8...v1.1.9
[1.1.2]: https://github.com/mcp-servers-for-revit/mcp-servers-for-revit/compare/v1.1.1...v1.1.2
[1.1.1]: https://github.com/mcp-servers-for-revit/mcp-servers-for-revit/compare/v1.1.0...v1.1.1
[1.1.0]: https://github.com/mcp-servers-for-revit/mcp-servers-for-revit/compare/v1.0.0...v1.1.0
