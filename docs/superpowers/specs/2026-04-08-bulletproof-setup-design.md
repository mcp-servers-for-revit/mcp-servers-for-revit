# Bulletproof Setup & Reliability — Design Spec

**Date**: 2026-04-08
**Goal**: Make the Revit MCP plugin "install and it works" for non-technical architects/engineers, eliminating setup and connection problems.
**Target users**: Colleagues with only Revit installed, no technical skills (no PowerShell, no JSON editing).

---

## 1. Auto-Configuration of Claude Desktop (from C# Plugin)

### Trigger
Every time the plugin loads in Revit (Application.OnStartup), run the config check.

### Behavior
1. Locate `claude_desktop_config.json`:
   - Standard path: `%APPDATA%\Claude\claude_desktop_config.json`
   - MSIX/Store path: `%LOCALAPPDATA%\Packages\Claude_*\LocalCache\Roaming\Claude\claude_desktop_config.json`
2. If Claude Desktop is not installed (neither path exists):
   - Show dialog: "To use AI with Revit, install Claude Desktop from claude.ai/download" with an "Open Browser" button
   - Do NOT create the folder — Claude Desktop creates it on first run
   - Exit config flow gracefully
3. If config file exists:
   - Read and parse JSON
   - If JSON is malformed: save as `claude_desktop_config.corrupted.YYYYMMDD-HHmmss.bak`, create fresh config
   - If valid JSON: check for `mcpServers.revit-mcp` entry
4. If `revit-mcp` entry is missing or points to wrong paths:
   - Create backup: `claude_desktop_config.YYYYMMDD-HHmmss.bak`
   - Merge new `revit-mcp` entry — preserve ALL other keys in the JSON
   - Write back with proper UTF-8 encoding (no BOM)
5. If `revit-mcp` entry is already correct:
   - Do nothing, show nothing. Silent success.

### Security Constraints
- **Localhost only**: TCP server binds to `127.0.0.1`, never `0.0.0.0`
- **User-space only**: writes to `%APPDATA%` only, never `Program Files`, never registry
- **No credentials**: writes only local file paths (node.exe, index.js), never API keys or tokens
- **Backup before modify**: always create timestamped backup before any write
- **Validate after write**: re-read and parse the file after writing to confirm valid JSON

### Files Modified
- `plugin/Utils/ClaudeDesktopConfigurator.cs` — rewrite config logic with safety guarantees
- `plugin/Core/Application.cs` — call config check on startup

---

## 2. Health-Check & Diagnostics

### 2.1 Automatic Health-Check (on MCP Switch activation)

When user clicks "Revit MCP Switch", before starting the TCP server, run these checks in order:

| # | Check | Auto-fix | If unfixable |
|---|-------|----------|-------------|
| 1 | DLL Zone.Identifier (blocked files) | Unblock all DLLs in plugin folder | Log warning, continue |
| 2 | Port available (8080-8089) | Try next port | Dialog: "No ports available (8080-8089). Close programs using these ports" |
| 3 | node.exe exists in `server/runtime/` | — | Dialog: "Server files missing — reinstall the plugin" |
| 4 | index.js exists in `server/build/` | — | Dialog: "Server files missing — reinstall the plugin" |
| 5 | Claude Desktop config correct | Auto-fix (Section 1) | Log warning, continue (user may not use Claude Desktop) |

If all checks pass: green indicator, no messages.
If auto-fixed something: discrete note in MCP panel: "Configuration updated automatically".
If unfixable: dialog with specific message and suggested action.

### 2.2 "Verify Connection" Button

New button in the MCP dockable panel. On click, runs full diagnostic:

1. Plugin loaded → check command registry count
2. MCP Server running → check TCP bind
3. Claude Desktop config → validate JSON entry
4. Round-trip test → send `say_hello`, measure response time

Output in the panel:
```
[OK] Plugin loaded (115 commands)
[OK] MCP Server running on port 8080
[OK] Claude Desktop configured
[OK] Round-trip test: 45ms
```

Or:
```
[OK] Plugin loaded (115 commands)
[FAIL] MCP Server not running — click "Revit MCP Switch" to start
[--] Claude Desktop: skipped (server not running)
```

Include a "Open log folder" link for support escalation.

### 2.3 Contextual Error Messages

Replace all generic errors with actionable messages:

| Current message | New message |
|-----------------|-------------|
| "Connection failed" | "Revit MCP is not active. Go to Add-Ins → click Revit MCP Switch" |
| "Timeout" | "Operation took too long (model has 50K elements). Try filtering by category" |
| "Tool not found" | "Command X is not enabled. Go to Settings → enable the command" |
| (silence when port busy) | "Port 8080 is busy. Using port 8081 instead" |
| "Revit connection failed" | "Cannot connect to Revit. Make sure Revit is open with a project loaded" |

### Files
- `plugin/Core/SocketService.cs` — health-check before TCP bind, contextual error messages
- `plugin/UI/MCPDockablePanel.xaml` — add "Verify Connection" button
- `plugin/UI/MCPDockablePanel.xaml.cs` — diagnostic logic and panel output
- New: `plugin/Helpers/HealthChecker.cs` — centralized health-check logic

---

## 3. Improved Installer

### 3.1 INSTALLA.bat (new file, included in release ZIP)

A batch file that the user double-clicks. Contents:

```batch
@echo off
echo Installing Revit MCP Plugin...
powershell -ExecutionPolicy Bypass -File "%~dp0scripts\install.ps1" -LocalZip "%~dp0"
pause
```

The `-LocalZip` parameter tells install.ps1 to use the already-extracted files from the ZIP instead of downloading from GitHub.

### 3.2 DISINSTALLA.bat (new file, included in release ZIP)

```batch
@echo off
echo Uninstalling Revit MCP Plugin...
powershell -ExecutionPolicy Bypass -File "%~dp0scripts\install.ps1" -Uninstall
pause
```

### 3.3 install.ps1 improvements

| Feature | Change |
|---------|--------|
| `-LocalZip` parameter | Install from local extracted folder instead of downloading |
| Bilingual messages | Detect Windows language; show Italian + English |
| Visual summary | Show green checkmarks for each completed step |
| DLL unblock | Already present, verify it runs on all files recursively |
| Claude Desktop auto-config | Call the same logic as the plugin (shared common.ps1 functions) |

### 3.4 release.yml changes

Add `INSTALLA.bat` and `DISINSTALLA.bat` to each release ZIP. These files are static and go in the root of the ZIP alongside `mcp-servers-for-revit.addin`.

### Release ZIP structure
```
mcp-servers-for-revit-v2.1.0-Revit2025.zip
├── INSTALLA.bat
├── DISINSTALLA.bat
├── scripts/
│   ├── install.ps1
│   ├── common.ps1
│   └── diagnose.ps1
├── mcp-servers-for-revit.addin
└── revit_mcp_plugin/
    └── (plugin files, server, runtime/node.exe)
```

### Files
- New: `INSTALLA.bat`
- New: `DISINSTALLA.bat`
- Modified: `scripts/install.ps1` — add `-LocalZip` parameter, bilingual messages
- Modified: `.github/workflows/release.yml` — include .bat files in ZIP

---

## 4. Technical Fixes

### 4.1 Port Auto-Discovery

**Plugin side** (`SocketService.cs`):
- Try binding to ports 8080, 8081, ..., 8089 in sequence
- Write chosen port to `mcp-port.txt` in the plugin folder
- Log: "Port 8080 busy, using port 8081"

**Server side** (`SocketClient.ts`):
- On connection, read `mcp-port.txt` from the plugin folder path
- Fallback: try 8080 if file doesn't exist (backward compatibility)

**Config side** (`ClaudeDesktopConfigurator.cs`):
- When writing Claude Desktop config, use the actual bound port (not hard-coded 8080)

Port file location: `%APPDATA%\Autodesk\Revit\Addins\<version>\revit_mcp_plugin\mcp-port.txt`
Content: just the port number as plain text, e.g. `8081`

### 4.2 Structured Logging

New class: `plugin/Helpers/McpLogger.cs`

```
[2026-04-08 14:23:01] [INFO] [SocketService] Server started on port 8080
[2026-04-08 14:23:02] [INFO] [HealthChecker] All checks passed
[2026-04-08 14:25:15] [WARN] [SocketService] Port 8080 busy, trying 8081
[2026-04-08 14:30:00] [ERROR] [CommandExecutor] CreateRoom failed: Level not found
```

- Log file: `%APPDATA%\Autodesk\Revit\Addins\<version>\revit_mcp_plugin\logs\mcp-YYYY-MM-DD.log`
- Rotation: keep last 7 days, delete older files on startup
- Replace ALL `Trace.WriteLine` calls with `McpLogger.Info/Warn/Error`
- Replace ALL empty catch blocks with `McpLogger.Error(ex)`

### 4.3 Adaptive Timeouts

In the TypeScript server, each tool declares its timeout category at registration time.

Three tiers:

| Tier | Timeout | Tools |
|------|---------|-------|
| `fast` | 30s | get_project_info, get_current_view_info, get_selected_elements, say_hello, get_phases, get_worksets, get_warnings, get_materials, get_shared_parameters, get_schedule_data, load_selection, save_selection, delete_selection, get_current_view_elements |
| `standard` | 120s | create_room, create_level, create_view, create_sheet, create_schedule, set_element_parameters, delete_element, modify_element, create_grid, create_floor, create_filled_region, create_text_note, create_dimensions, tag_all_rooms, tag_all_walls, and most creation/modification tools |
| `heavy` | 300s | batch_export, analyze_model_statistics, purge_unused, check_model_health, clash_detection, export_to_excel, export_elements_data, batch_rename, bulk_modify_parameter_values, workflow_model_audit, send_code_to_revit, import_from_excel |

Implementation: add an optional `timeout` field to the `withRevitConnection()` call. Default: `standard` (120s).

### 4.4 Connection Retry with Backoff

In `ConnectionManager.ts` / `SocketClient.ts`:

- On connection failure: retry after 1s, then 2s, then 4s (max 3 attempts)
- On timeout: do NOT retry (the command already ran for 30-300s)
- On Revit closed and reopened: next tool call reconnects automatically (no manual restart)
- Error after 3 retries: "Cannot connect to Revit. Make sure Revit is open and MCP Switch is active (green indicator)"

### Files
- `plugin/Core/SocketService.cs` — port auto-discovery, structured logging
- `server/src/utils/SocketClient.ts` — read mcp-port.txt, adaptive timeout, retry logic
- `server/src/utils/ConnectionManager.ts` — retry with backoff
- All tool `.ts` files — add timeout tier to `withRevitConnection()` call
- New: `plugin/Helpers/McpLogger.cs` — structured logger
- All C# files with empty catch blocks — add McpLogger.Error(ex)

---

## Out of Scope

- Database migration (sql.js → better-sqlite3) — no real-world issues reported
- Global mutex removal — Revit ExternalEvent is inherently single-threaded
- TypeScript test suite — important but doesn't fix colleague problems today
- New Revit features (geometry export, multi-doc, analysis)
- Native .exe installer (MSI/NSIS) — .bat approach is simpler and doesn't trigger antivirus

---

## Success Criteria

1. A colleague with only Revit installed can go from ZIP download to working AI chat in under 3 minutes
2. No manual JSON editing required, ever
3. If something breaks, the user sees a message that tells them exactly what to do
4. Port conflicts are resolved automatically without user intervention
5. All errors are logged to a file that can be shared for support
