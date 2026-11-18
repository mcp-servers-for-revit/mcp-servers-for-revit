# Bulletproof Setup & Reliability — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the Revit MCP plugin "install and it works" for non-technical architects/engineers — zero manual JSON editing, clear error messages, automatic port handling.

**Architecture:** Four layers of changes: (1) structured logging foundation, (2) port auto-discovery in C# plugin + TypeScript server, (3) safe Claude Desktop auto-config + health-check system, (4) installer improvements with INSTALLA.bat. Each task builds on the previous.

**Tech Stack:** C# (.NET 4.8/8.0), TypeScript/Node.js, PowerShell, WPF XAML

**Spec:** `docs/superpowers/specs/2026-04-08-bulletproof-setup-design.md`

---

## File Map

### New Files
| File | Responsibility |
|------|---------------|
| `plugin/Helpers/McpLogger.cs` | Structured file logger with rotation |
| `plugin/Helpers/HealthChecker.cs` | Centralized health-check logic |
| `INSTALLA.bat` | One-click installer for end users |
| `DISINSTALLA.bat` | One-click uninstaller |

### Modified Files
| File | Changes |
|------|---------|
| `plugin/Core/SocketService.cs` | Port auto-discovery (8080-8089), logging, health-check on start |
| `plugin/Utils/ClaudeDesktopConfigurator.cs` | Safe config with backup, port-aware, logging |
| `plugin/Core/Application.cs` | Call health-check on startup |
| `plugin/UI/MCPDockablePanel.xaml` | Add "Verify Connection" button |
| `plugin/UI/MCPDockablePanel.xaml.cs` | Diagnostic output, LogCommand implementation |
| `server/src/utils/ConnectionManager.ts` | Read mcp-port.txt, retry with backoff, timeout parameter |
| `server/src/utils/SocketClient.ts` | Accept timeout parameter in sendCommand |
| `server/src/tools/*.ts` | Add timeout tier to withRevitConnection calls (heavy tools only) |
| `scripts/install.ps1` | Add -LocalZip parameter |
| `.github/workflows/release.yml` | Include .bat files in ZIP |

---

## Task 1: Structured Logger (McpLogger.cs)

**Files:**
- Create: `plugin/Helpers/McpLogger.cs`

This is the foundation — all subsequent tasks use McpLogger instead of Trace.WriteLine.

- [ ] **Step 1: Create McpLogger.cs**

```csharp
// plugin/Helpers/McpLogger.cs
using System;
using System.IO;
using System.Globalization;

namespace revit_mcp_plugin.Helpers
{
    public static class McpLogger
    {
        private static readonly object _lock = new object();
        private static string _logDir;
        private static string _currentLogFile;

        public static void Initialize(string pluginDir)
        {
            _logDir = Path.Combine(pluginDir, "logs");
            if (!Directory.Exists(_logDir))
                Directory.CreateDirectory(_logDir);

            _currentLogFile = Path.Combine(_logDir,
                $"mcp-{DateTime.Now:yyyy-MM-dd}.log");

            CleanOldLogs(7);
        }

        public static string LogDirectory => _logDir;

        public static void Info(string component, string message)
            => Write("INFO", component, message);

        public static void Warn(string component, string message)
            => Write("WARN", component, message);

        public static void Error(string component, string message)
            => Write("ERROR", component, message);

        public static void Error(string component, string message, Exception ex)
            => Write("ERROR", component, $"{message}: {ex.GetType().Name}: {ex.Message}");

        private static void Write(string level, string component, string message)
        {
            if (_currentLogFile == null) return;
            var line = string.Format(CultureInfo.InvariantCulture,
                "[{0:yyyy-MM-dd HH:mm:ss}] [{1}] [{2}] {3}",
                DateTime.Now, level, component, message);
            lock (_lock)
            {
                try { File.AppendAllText(_currentLogFile, line + Environment.NewLine); }
                catch { /* logging must never crash the host */ }
            }
        }

        private static void CleanOldLogs(int keepDays)
        {
            try
            {
                var cutoff = DateTime.Now.AddDays(-keepDays);
                foreach (var file in Directory.GetFiles(_logDir, "mcp-*.log"))
                {
                    if (File.GetLastWriteTime(file) < cutoff)
                        File.Delete(file);
                }
            }
            catch { /* best-effort cleanup */ }
        }
    }
}
```

- [ ] **Step 2: Verify it compiles**

Open the solution in VS or run:
```bash
dotnet build mcp-servers-for-revit.sln -c "Debug R25"
```
Expected: Build succeeds with 0 errors.

- [ ] **Step 3: Initialize logger in Application.OnStartup**

In `plugin/Core/Application.cs`, add at the top of `OnStartup` (before line 17):

```csharp
// At top of file, add:
using revit_mcp_plugin.Helpers;

// Inside OnStartup, as first line:
var pluginDir = Path.GetDirectoryName(typeof(Application).Assembly.Location);
McpLogger.Initialize(pluginDir);
McpLogger.Info("Application", "Plugin starting");
```

- [ ] **Step 4: Build and verify**

```bash
dotnet build mcp-servers-for-revit.sln -c "Debug R25"
```
Expected: Build succeeds. When Revit starts, a `logs/mcp-YYYY-MM-DD.log` file appears in the plugin folder.

- [ ] **Step 5: Commit**

```bash
git add plugin/Helpers/McpLogger.cs plugin/Core/Application.cs
git commit -m "feat: add structured file logger (McpLogger) with 7-day rotation"
```

---

## Task 2: Port Auto-Discovery (C# Plugin)

**Files:**
- Modify: `plugin/Core/SocketService.cs` (lines 24, 86, 96-119)

- [ ] **Step 1: Add port discovery method to SocketService.cs**

Add this method after the `Initialize` method (after line 94):

```csharp
private int FindAvailablePort(int startPort, int endPort)
{
    for (int port = startPort; port <= endPort; port++)
    {
        try
        {
            var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, port);
            listener.Start();
            listener.Stop();
            return port;
        }
        catch (System.Net.Sockets.SocketException)
        {
            McpLogger.Warn("SocketService", $"Port {port} is busy, trying next");
        }
    }
    return -1;
}

private void WritePortFile(int port)
{
    try
    {
        var pluginDir = Path.GetDirectoryName(typeof(SocketService).Assembly.Location);
        var portFile = Path.Combine(pluginDir, "mcp-port.txt");
        File.WriteAllText(portFile, port.ToString());
        McpLogger.Info("SocketService", $"Port file written: {portFile}");
    }
    catch (Exception ex)
    {
        McpLogger.Error("SocketService", "Failed to write port file", ex);
    }
}
```

- [ ] **Step 2: Replace hard-coded port in Start() method**

Replace the `Start()` method body (lines 96-119) with:

```csharp
public void Start()
{
    if (_isRunning) return;

    int port = FindAvailablePort(8080, 8089);
    if (port == -1)
    {
        McpLogger.Error("SocketService", "No available port in range 8080-8089");
        System.Windows.MessageBox.Show(
            "No available port (8080-8089). Close programs using these ports and try again.",
            "Revit MCP", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
        return;
    }

    _port = port;
    if (port != 8080)
        McpLogger.Warn("SocketService", $"Port 8080 busy, using port {port}");

    WritePortFile(port);

    try
    {
        _listener = new TcpListener(IPAddress.Loopback, _port);
        _listener.Start();
        _isRunning = true;

        _listenerThread = new Thread(ListenForClients) { IsBackground = true };
        _listenerThread.Start();

        McpLogger.Info("SocketService", $"Server started on port {_port}");
    }
    catch (Exception ex)
    {
        McpLogger.Error("SocketService", "Failed to start TCP server", ex);
        _isRunning = false;
    }
}
```

- [ ] **Step 3: Add logging to empty catch blocks in ListenForClients**

Replace lines 158-165 in `ListenForClients()`:

```csharp
catch (SocketException ex)
{
    if (_isRunning)
        McpLogger.Error("SocketService", "Socket error in listener", ex);
}
catch (Exception ex)
{
    if (_isRunning)
        McpLogger.Error("SocketService", "Unexpected error in listener", ex);
}
```

- [ ] **Step 4: Add logging to Stop() method**

Add at the beginning of `Stop()`:
```csharp
McpLogger.Info("SocketService", "Server stopping");
```

- [ ] **Step 5: Build and verify**

```bash
dotnet build mcp-servers-for-revit.sln -c "Debug R25"
```
Expected: Build succeeds. When MCP Switch is clicked, `mcp-port.txt` is written in the plugin folder and the log shows which port was chosen.

- [ ] **Step 6: Commit**

```bash
git add plugin/Core/SocketService.cs
git commit -m "feat: port auto-discovery (8080-8089) with mcp-port.txt"
```

---

## Task 3: Safe Claude Desktop Auto-Configuration

**Files:**
- Modify: `plugin/Utils/ClaudeDesktopConfigurator.cs` (full rewrite of EnsureConfigured)

- [ ] **Step 1: Rewrite EnsureConfigured with safety guarantees**

Replace the `EnsureConfigured()` method (lines 24-131) with:

```csharp
public static void EnsureConfigured()
{
    try
    {
        McpLogger.Info("Configurator", "Checking Claude Desktop configuration");

        string claudeDir = GetClaudeDesktopDir();
        if (claudeDir == null)
        {
            McpLogger.Warn("Configurator", "Claude Desktop not installed");
            return; // Don't show dialog on every startup — too intrusive
        }

        string configPath = Path.Combine(claudeDir, ConfigFileName);
        string serverPath = GetServerPath();
        string nodePath = GetNodePath();

        if (serverPath == null || nodePath == null)
        {
            McpLogger.Warn("Configurator", $"Server path ({serverPath}) or node path ({nodePath}) not found");
            return;
        }

        // Read port from mcp-port.txt if it exists, default to 8080
        // (port file is written by SocketService when it starts)

        // Build the expected entry
        var expectedEntry = new
        {
            command = nodePath.Replace("\\", "\\\\"),
            args = new[] { serverPath.Replace("\\", "\\\\") }
        };

        string existingJson = null;
        if (File.Exists(configPath))
        {
            try
            {
                existingJson = File.ReadAllText(configPath);
                // Validate JSON by parsing it
                Newtonsoft.Json.Linq.JObject.Parse(existingJson);
            }
            catch (Exception ex)
            {
                // JSON is corrupted — backup and start fresh
                string backupPath = configPath + $".corrupted.{DateTime.Now:yyyyMMdd-HHmmss}.bak";
                File.Copy(configPath, backupPath, true);
                McpLogger.Warn("Configurator", $"Config corrupted, backed up to {backupPath}");
                existingJson = null;
            }
        }

        // Parse or create config
        Newtonsoft.Json.Linq.JObject config;
        if (existingJson != null)
        {
            config = Newtonsoft.Json.Linq.JObject.Parse(existingJson);
        }
        else
        {
            config = new Newtonsoft.Json.Linq.JObject();
        }

        // Ensure mcpServers exists
        if (config["mcpServers"] == null)
            config["mcpServers"] = new Newtonsoft.Json.Linq.JObject();

        var mcpServers = (Newtonsoft.Json.Linq.JObject)config["mcpServers"];

        // Build the correct entry
        var correctEntry = new Newtonsoft.Json.Linq.JObject
        {
            ["command"] = nodePath,
            ["args"] = new Newtonsoft.Json.Linq.JArray { serverPath }
        };

        // Check if entry already exists and is correct
        var existing = mcpServers["revit-mcp"] as Newtonsoft.Json.Linq.JObject;
        if (existing != null)
        {
            string existingCmd = existing["command"]?.ToString() ?? "";
            string existingArg = existing["args"]?[0]?.ToString() ?? "";
            if (existingCmd == nodePath && existingArg == serverPath)
            {
                McpLogger.Info("Configurator", "Claude Desktop config already correct");
                return; // Nothing to do
            }
            McpLogger.Info("Configurator", "Updating revit-mcp entry (paths changed)");
        }
        else
        {
            McpLogger.Info("Configurator", "Adding revit-mcp entry to Claude Desktop config");
        }

        // Backup before modifying
        if (File.Exists(configPath))
        {
            string backupPath = configPath + $".{DateTime.Now:yyyyMMdd-HHmmss}.bak";
            File.Copy(configPath, backupPath, true);
        }

        // Set the entry (preserves all other mcpServers)
        mcpServers["revit-mcp"] = correctEntry;

        // Write with UTF-8 (no BOM)
        string newJson = config.ToString(Newtonsoft.Json.Formatting.Indented);
        File.WriteAllText(configPath, newJson, new System.Text.UTF8Encoding(false));

        // Validate what we wrote
        try
        {
            string verify = File.ReadAllText(configPath);
            Newtonsoft.Json.Linq.JObject.Parse(verify);
            McpLogger.Info("Configurator", "Claude Desktop config updated and verified");
        }
        catch (Exception ex)
        {
            McpLogger.Error("Configurator", "Config verification failed after write", ex);
        }
    }
    catch (Exception ex)
    {
        McpLogger.Error("Configurator", "Failed to configure Claude Desktop", ex);
        // Never crash Revit because of config issues
    }
}
```

- [ ] **Step 2: Build and verify**

```bash
dotnet build mcp-servers-for-revit.sln -c "Debug R25"
```
Expected: Build succeeds.

- [ ] **Step 3: Commit**

```bash
git add plugin/Utils/ClaudeDesktopConfigurator.cs
git commit -m "feat: safe Claude Desktop auto-config with backup and validation"
```

---

## Task 4: Health-Check System (HealthChecker.cs)

**Files:**
- Create: `plugin/Helpers/HealthChecker.cs`

- [ ] **Step 1: Create HealthChecker.cs**

```csharp
// plugin/Helpers/HealthChecker.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;

namespace revit_mcp_plugin.Helpers
{
    public class HealthCheckResult
    {
        public string Name { get; set; }
        public bool Passed { get; set; }
        public bool AutoFixed { get; set; }
        public string Message { get; set; }
    }

    public static class HealthChecker
    {
        public static List<HealthCheckResult> RunAll(string pluginDir, int port)
        {
            var results = new List<HealthCheckResult>();
            results.Add(CheckDllBlocked(pluginDir));
            results.Add(CheckServerFiles(pluginDir));
            results.Add(CheckClaudeDesktopConfig());
            results.Add(CheckPortConnectivity(port));
            return results;
        }

        public static HealthCheckResult CheckDllBlocked(string pluginDir)
        {
            try
            {
                int unblocked = 0;
                foreach (var dll in Directory.GetFiles(pluginDir, "*.dll", SearchOption.AllDirectories))
                {
                    var zone = dll + ":Zone.Identifier";
                    if (NativeFileHasZone(dll))
                    {
                        try
                        {
                            DeleteAlternateDataStream(dll);
                            unblocked++;
                        }
                        catch { }
                    }
                }
                if (unblocked > 0)
                {
                    McpLogger.Info("HealthChecker", $"Unblocked {unblocked} DLL(s)");
                    return new HealthCheckResult
                    {
                        Name = "DLL Files",
                        Passed = true,
                        AutoFixed = true,
                        Message = $"Unblocked {unblocked} DLL(s) automatically"
                    };
                }
                return new HealthCheckResult
                {
                    Name = "DLL Files",
                    Passed = true,
                    Message = "All DLLs OK"
                };
            }
            catch (Exception ex)
            {
                McpLogger.Error("HealthChecker", "DLL check failed", ex);
                return new HealthCheckResult
                {
                    Name = "DLL Files",
                    Passed = false,
                    Message = $"Check failed: {ex.Message}"
                };
            }
        }

        public static HealthCheckResult CheckServerFiles(string pluginDir)
        {
            var serverDir = Path.Combine(pluginDir, "Commands", "RevitMCPCommandSet", "server");
            var nodeExe = Path.Combine(serverDir, "runtime", "node.exe");
            var indexJs = Path.Combine(serverDir, "build", "index.js");

            if (!File.Exists(nodeExe))
                return new HealthCheckResult
                {
                    Name = "MCP Server",
                    Passed = false,
                    Message = "node.exe missing — reinstall the plugin"
                };

            if (!File.Exists(indexJs))
                return new HealthCheckResult
                {
                    Name = "MCP Server",
                    Passed = false,
                    Message = "index.js missing — reinstall the plugin"
                };

            return new HealthCheckResult
            {
                Name = "MCP Server",
                Passed = true,
                Message = "Server files OK"
            };
        }

        public static HealthCheckResult CheckClaudeDesktopConfig()
        {
            try
            {
                // Just check if the config exists and has revit-mcp entry
                var claudeDir = FindClaudeDesktopDir();
                if (claudeDir == null)
                    return new HealthCheckResult
                    {
                        Name = "Claude Desktop",
                        Passed = false,
                        Message = "Claude Desktop not installed — download from claude.ai/download"
                    };

                var configPath = Path.Combine(claudeDir, "claude_desktop_config.json");
                if (!File.Exists(configPath))
                    return new HealthCheckResult
                    {
                        Name = "Claude Desktop",
                        Passed = false,
                        Message = "Config file missing — will be created on next startup"
                    };

                var json = File.ReadAllText(configPath);
                var config = Newtonsoft.Json.Linq.JObject.Parse(json);
                var entry = config["mcpServers"]?["revit-mcp"];
                if (entry == null)
                    return new HealthCheckResult
                    {
                        Name = "Claude Desktop",
                        Passed = false,
                        Message = "revit-mcp entry missing — will be added on next startup"
                    };

                return new HealthCheckResult
                {
                    Name = "Claude Desktop",
                    Passed = true,
                    Message = "Claude Desktop configured"
                };
            }
            catch (Exception ex)
            {
                return new HealthCheckResult
                {
                    Name = "Claude Desktop",
                    Passed = false,
                    Message = $"Config check error: {ex.Message}"
                };
            }
        }

        public static HealthCheckResult CheckPortConnectivity(int port)
        {
            if (port <= 0)
                return new HealthCheckResult
                {
                    Name = "TCP Connection",
                    Passed = false,
                    Message = "Server not started"
                };

            try
            {
                using (var client = new TcpClient())
                {
                    var result = client.BeginConnect("127.0.0.1", port, null, null);
                    bool connected = result.AsyncWaitHandle.WaitOne(TimeSpan.FromMilliseconds(500));
                    if (connected && client.Connected)
                    {
                        return new HealthCheckResult
                        {
                            Name = "TCP Connection",
                            Passed = true,
                            Message = $"Port {port} responding"
                        };
                    }
                }
            }
            catch { }

            return new HealthCheckResult
            {
                Name = "TCP Connection",
                Passed = false,
                Message = $"Port {port} not responding — click Revit MCP Switch"
            };
        }

        private static string FindClaudeDesktopDir()
        {
            var standard = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Claude");
            if (Directory.Exists(standard)) return standard;

            try
            {
                var packagesDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Packages");
                if (Directory.Exists(packagesDir))
                {
                    foreach (var dir in Directory.GetDirectories(packagesDir, "Claude_*"))
                    {
                        var msixPath = Path.Combine(dir, "LocalCache", "Roaming", "Claude");
                        if (Directory.Exists(msixPath)) return msixPath;
                    }
                }
            }
            catch { }

            return null;
        }

        private static bool NativeFileHasZone(string filePath)
        {
            // Check for Zone.Identifier alternate data stream
            try
            {
                return File.Exists(filePath + ":Zone.Identifier");
            }
            catch { return false; }
        }

        private static void DeleteAlternateDataStream(string filePath)
        {
            // Use FileInfo to remove the Zone.Identifier stream
            // This is equivalent to "Unblock-File" in PowerShell
            var streamPath = filePath + ":Zone.Identifier";
            try
            {
                NativeMethods.DeleteFile(streamPath);
            }
            catch { }
        }

        private static class NativeMethods
        {
            [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
            public static extern bool DeleteFile(string lpFileName);
        }
    }
}
```

- [ ] **Step 2: Build and verify**

```bash
dotnet build mcp-servers-for-revit.sln -c "Debug R25"
```

- [ ] **Step 3: Commit**

```bash
git add plugin/Helpers/HealthChecker.cs
git commit -m "feat: add HealthChecker with DLL unblock, server files, config, and port checks"
```

---

## Task 5: Integrate Health-Check into MCP Switch

**Files:**
- Modify: `plugin/Core/SocketService.cs` (Start method)

- [ ] **Step 1: Add health-check call at the top of Start()**

Add before the `FindAvailablePort` call in `Start()`:

```csharp
// Run pre-start health checks
var pluginDir = Path.GetDirectoryName(typeof(SocketService).Assembly.Location);
var healthResults = HealthChecker.RunAll(pluginDir, 0); // port 0 = not started yet, skip port check
bool hasAutoFix = false;
bool hasFail = false;
foreach (var r in healthResults)
{
    if (r.Name == "TCP Connection") continue; // skip port check before start
    if (r.AutoFixed) hasAutoFix = true;
    if (!r.Passed)
    {
        hasFail = true;
        McpLogger.Warn("SocketService", $"Health check [{r.Name}]: {r.Message}");
    }
}

// Notify dockable panel if auto-fixes happened
if (hasAutoFix)
    _panel?.Dispatcher.Invoke(() => _panel?.AddSystemMessage("Configuration updated automatically"));

// Block start only if server files are missing
var serverCheck = healthResults.Find(r => r.Name == "MCP Server");
if (serverCheck != null && !serverCheck.Passed)
{
    System.Windows.MessageBox.Show(
        serverCheck.Message,
        "Revit MCP", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
    return;
}
```

Note: `_panel` reference and `AddSystemMessage` will be added in Task 6.

- [ ] **Step 2: Build and verify**

```bash
dotnet build mcp-servers-for-revit.sln -c "Debug R25"
```

- [ ] **Step 3: Commit**

```bash
git add plugin/Core/SocketService.cs
git commit -m "feat: run health-check before starting MCP server"
```

---

## Task 6: "Verify Connection" Button in Dockable Panel

**Files:**
- Modify: `plugin/UI/MCPDockablePanel.xaml`
- Modify: `plugin/UI/MCPDockablePanel.xaml.cs`

- [ ] **Step 1: Add button to XAML**

In `MCPDockablePanel.xaml`, after the "Export" button (around line 36), add:

```xml
<TextBlock Text="&#xE9D9;" FontFamily="Segoe MDL2 Assets" FontSize="14"
           Cursor="Hand" Foreground="#888" ToolTip="Verify Connection"
           MouseDown="VerifyConnection_Click" Margin="8,0,0,0"/>
```

- [ ] **Step 2: Add handler and AddSystemMessage in code-behind**

In `MCPDockablePanel.xaml.cs`, add these methods:

```csharp
public void AddSystemMessage(string message)
{
    Dispatcher.Invoke(() =>
    {
        // Add a system message to the chat panel
        var block = new System.Windows.Controls.TextBlock
        {
            Text = $"[System] {message}",
            Foreground = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0x88, 0x88, 0x88)),
            FontStyle = System.Windows.FontStyles.Italic,
            Margin = new Thickness(8, 4, 8, 4),
            TextWrapping = TextWrapping.Wrap
        };
        MessagesPanel.Children.Add(block);
        ChatScrollViewer.ScrollToEnd();
    });
}

private void VerifyConnection_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
{
    var pluginDir = Path.GetDirectoryName(typeof(MCPDockablePanel).Assembly.Location);
    int port = SocketService.Instance?.Port ?? 0;
    var results = HealthChecker.RunAll(pluginDir, port);

    var sb = new System.Text.StringBuilder();
    sb.AppendLine("--- Connection Diagnostic ---");
    foreach (var r in results)
    {
        string icon = r.Passed ? "[OK]" : "[FAIL]";
        if (r.AutoFixed) icon = "[FIXED]";
        sb.AppendLine($"{icon} {r.Name}: {r.Message}");
    }
    sb.AppendLine($"Log folder: {McpLogger.LogDirectory}");
    sb.AppendLine("-----------------------------");

    AddSystemMessage(sb.ToString());
}
```

- [ ] **Step 3: Expose Port property on SocketService**

In `SocketService.cs`, add a public property:

```csharp
public int Port => _port;
```

- [ ] **Step 4: Build and verify**

```bash
dotnet build mcp-servers-for-revit.sln -c "Debug R25"
```

- [ ] **Step 5: Commit**

```bash
git add plugin/UI/MCPDockablePanel.xaml plugin/UI/MCPDockablePanel.xaml.cs plugin/Core/SocketService.cs
git commit -m "feat: add Verify Connection button with diagnostic output"
```

---

## Task 7: Contextual Error Messages

**Files:**
- Modify: `plugin/Core/SocketService.cs` (ProcessJsonRPCRequest method)

- [ ] **Step 1: Improve error messages in ProcessJsonRPCRequest**

In `SocketService.cs`, find the catch blocks in `ProcessJsonRPCRequest` (around line 230-304) and replace generic messages:

Replace any `"Internal error"` or `"Execution failed"` strings with:

```csharp
// In the timeout catch:
catch (TimeoutException)
{
    McpLogger.Error("SocketService", $"Command '{method}' timed out");
    return CreateErrorResponse(id, -32000,
        $"Operation '{method}' took too long. For large models, try filtering by category or reducing scope.");
}

// In the general exception catch:
catch (Exception ex)
{
    McpLogger.Error("SocketService", $"Command '{method}' failed", ex);
    return CreateErrorResponse(id, -32603,
        $"Command '{method}' failed: {ex.Message}. Check Revit is open with a project loaded.");
}
```

- [ ] **Step 2: Add logging to HandleClientCommunication catch blocks**

Replace empty catches in `HandleClientCommunication` (around line 168-228) with logged versions:

```csharp
catch (Exception ex)
{
    McpLogger.Error("SocketService", "Client communication error", ex);
}
```

- [ ] **Step 3: Build and verify**

```bash
dotnet build mcp-servers-for-revit.sln -c "Debug R25"
```

- [ ] **Step 4: Commit**

```bash
git add plugin/Core/SocketService.cs
git commit -m "fix: replace generic error messages with actionable descriptions"
```

---

## Task 8: Server-Side Port Reading (TypeScript)

**Files:**
- Modify: `server/src/utils/ConnectionManager.ts`

- [ ] **Step 1: Add port file reading to ConnectionManager.ts**

Replace the hard-coded port (line 23) with dynamic port reading:

```typescript
import { readFileSync, existsSync } from "fs";
import { join } from "path";

function readPortFromFile(): number {
  // Search for mcp-port.txt in standard Revit Addins locations
  const appData = process.env.APPDATA || "";
  const years = ["2027", "2026", "2025", "2024", "2023"];
  for (const year of years) {
    const portFile = join(
      appData,
      "Autodesk",
      "Revit",
      "Addins",
      year,
      "revit_mcp_plugin",
      "mcp-port.txt"
    );
    if (existsSync(portFile)) {
      try {
        const content = readFileSync(portFile, "utf-8").trim();
        const port = parseInt(content, 10);
        if (port >= 8080 && port <= 8089) {
          return port;
        }
      } catch {
        // ignore, try next year
      }
    }
  }
  return 8080; // fallback default
}
```

Then modify the `withRevitConnection` function to use it:

Replace line 23 (`new RevitClientConnection("localhost", 8080)`) with:

```typescript
const port = readPortFromFile();
const client = new RevitClientConnection("localhost", port);
```

- [ ] **Step 2: Build and verify**

```bash
cd server && npm run build
```

- [ ] **Step 3: Commit**

```bash
git add server/src/utils/ConnectionManager.ts
git commit -m "feat: read port from mcp-port.txt instead of hard-coded 8080"
```

---

## Task 9: Adaptive Timeouts (TypeScript)

**Files:**
- Modify: `server/src/utils/SocketClient.ts` (sendCommand method)
- Modify: `server/src/utils/ConnectionManager.ts` (withRevitConnection signature)

- [ ] **Step 1: Add timeout parameter to sendCommand**

In `SocketClient.ts`, change the `sendCommand` signature (line 116) from:

```typescript
sendCommand(command: string, params: any = {})
```

to:

```typescript
sendCommand(command: string, params: any = {}, timeoutMs: number = 120000)
```

Then replace the hard-coded timeout value on line 157-164:

```typescript
const timer = setTimeout(() => {
  delete this.pendingRequests[requestId];
  reject(
    new Error(
      `Command '${command}' timed out after ${Math.round(timeoutMs / 1000)}s. ` +
      `For large models, try filtering by category or reducing scope.`
    )
  );
  this.socket?.destroy();
}, timeoutMs);
```

- [ ] **Step 2: Add timeout parameter to withRevitConnection**

In `ConnectionManager.ts`, change the function signature to accept an optional timeout:

```typescript
export async function withRevitConnection<T>(
  operation: (client: RevitClientConnection) => Promise<T>,
  timeoutMs: number = 120000
): Promise<T> {
```

Then pass `timeoutMs` when creating the client or calling operations. Since `sendCommand` now accepts timeout, the tools will pass it through.

- [ ] **Step 3: Update heavy tools to use 300s timeout**

For each heavy tool file, change `withRevitConnection(async (revitClient) => {` to `withRevitConnection(async (revitClient) => {`, `300000)`:

Files to update (add `300000` as second argument to `withRevitConnection`):
- `server/src/tools/batch_export.ts`
- `server/src/tools/analyze_model_statistics.ts`
- `server/src/tools/purge_unused.ts`
- `server/src/tools/check_model_health.ts`
- `server/src/tools/clash_detection.ts`
- `server/src/tools/export_to_excel.ts`
- `server/src/tools/export_elements_data.ts`
- `server/src/tools/batch_rename.ts`
- `server/src/tools/bulk_modify_parameter_values.ts`
- `server/src/tools/workflow_model_audit.ts`
- `server/src/tools/send_code_to_revit.ts`
- `server/src/tools/import_from_excel.ts`

For fast tools (30s timeout), update:
- `server/src/tools/get_project_info.ts`
- `server/src/tools/get_current_view_info.ts`
- `server/src/tools/get_selected_elements.ts`
- `server/src/tools/say_hello.ts`
- `server/src/tools/get_phases.ts`
- `server/src/tools/get_worksets.ts`
- `server/src/tools/get_warnings.ts`
- `server/src/tools/get_materials.ts`
- `server/src/tools/get_shared_parameters.ts`
- `server/src/tools/load_selection.ts`
- `server/src/tools/save_selection.ts`
- `server/src/tools/delete_selection.ts`

All other tools keep the default 120s.

Example change for `batch_export.ts`:
```typescript
// Before:
return await withRevitConnection(async (revitClient) => {
// After:
return await withRevitConnection(async (revitClient) => {
  // ... same body ...
}, 300000);
```

Example change for `get_project_info.ts`:
```typescript
// Before:
return await withRevitConnection(async (revitClient) => {
// After:
return await withRevitConnection(async (revitClient) => {
  // ... same body ...
}, 30000);
```

- [ ] **Step 4: Build and verify**

```bash
cd server && npm run build
```

- [ ] **Step 5: Commit**

```bash
git add server/src/utils/SocketClient.ts server/src/utils/ConnectionManager.ts server/src/tools/
git commit -m "feat: adaptive timeouts — 30s fast / 120s standard / 300s heavy"
```

---

## Task 10: Connection Retry with Backoff

**Files:**
- Modify: `server/src/utils/ConnectionManager.ts`

- [ ] **Step 1: Add retry logic to withRevitConnection**

Wrap the connection attempt in a retry loop. Replace the connection section of `withRevitConnection`:

```typescript
const MAX_RETRIES = 3;
const BACKOFF_MS = [1000, 2000, 4000];

let lastError: Error | null = null;
for (let attempt = 0; attempt < MAX_RETRIES; attempt++) {
  try {
    const port = readPortFromFile();
    const client = new RevitClientConnection("localhost", port);

    // Connection with timeout
    await new Promise<void>((resolve, reject) => {
      const timer = setTimeout(
        () => reject(new Error(`Connection timeout (port ${port})`)),
        5000
      );
      client.connect();
      // Give the socket a moment to establish
      setTimeout(() => {
        clearTimeout(timer);
        if (client.isConnected()) {
          resolve();
        } else {
          reject(new Error(`Failed to connect to port ${port}`));
        }
      }, 200);
    });

    // Execute the operation
    const result = await operation(client);
    client.disconnect();
    return result;
  } catch (error) {
    lastError = error instanceof Error ? error : new Error(String(error));
    if (attempt < MAX_RETRIES - 1) {
      const delay = BACKOFF_MS[attempt];
      await new Promise((r) => setTimeout(r, delay));
    }
  }
}

throw new Error(
  `Cannot connect to Revit after ${MAX_RETRIES} attempts. ` +
  `Make sure Revit is open and MCP Switch is active (green indicator). ` +
  `Last error: ${lastError?.message}`
);
```

- [ ] **Step 2: Build and verify**

```bash
cd server && npm run build
```

- [ ] **Step 3: Commit**

```bash
git add server/src/utils/ConnectionManager.ts
git commit -m "feat: connection retry with exponential backoff (3 attempts)"
```

---

## Task 11: INSTALLA.bat and DISINSTALLA.bat

**Files:**
- Create: `INSTALLA.bat`
- Create: `DISINSTALLA.bat`

- [ ] **Step 1: Create INSTALLA.bat**

```batch
@echo off
chcp 65001 >nul 2>&1
echo.
echo   ================================================================
echo     Revit MCP Plugin — Installer
echo   ================================================================
echo.
echo   Installing...
echo.
powershell -ExecutionPolicy Bypass -File "%~dp0scripts\install.ps1" -LocalZip "%~dp0"
echo.
echo   Press any key to close.
pause >nul
```

- [ ] **Step 2: Create DISINSTALLA.bat**

```batch
@echo off
chcp 65001 >nul 2>&1
echo.
echo   ================================================================
echo     Revit MCP Plugin — Uninstaller
echo   ================================================================
echo.
powershell -ExecutionPolicy Bypass -File "%~dp0scripts\install.ps1" -Uninstall
echo.
echo   Press any key to close.
pause >nul
```

- [ ] **Step 3: Commit**

```bash
git add INSTALLA.bat DISINSTALLA.bat
git commit -m "feat: add INSTALLA.bat and DISINSTALLA.bat for one-click install"
```

---

## Task 12: install.ps1 — LocalZip Parameter

**Files:**
- Modify: `scripts/install.ps1`

- [ ] **Step 1: Add -LocalZip parameter**

Add to the `param()` block (after line 60):

```powershell
[string]$LocalZip
```

- [ ] **Step 2: Add local install logic**

In STEP 6 (around line 516, the `Install-ForVersion` function), add a branch for local install. Before the download section, add:

```powershell
# If -LocalZip is specified, install from local extracted folder instead of downloading
if ($LocalZip) {
    $localAddin = Join-Path $LocalZip "$ADDIN_FILE"
    $localPlugin = Join-Path $LocalZip $PLUGIN_FOLDER

    if (-not (Test-Path $localAddin)) {
        Write-Err "Local install: $ADDIN_FILE not found in $LocalZip"
        return $false
    }

    # Remove old installation
    Remove-Item "$addinsDir\$ADDIN_FILE" -Force -ErrorAction SilentlyContinue
    Remove-Item "$addinsDir\$PLUGIN_FOLDER" -Recurse -Force -ErrorAction SilentlyContinue

    # Copy from local
    if (-not (Test-Path $addinsDir)) {
        New-Item -ItemType Directory -Path $addinsDir -Force | Out-Null
    }
    Copy-Item $localAddin "$addinsDir\" -Force
    Copy-Item $localPlugin "$addinsDir\" -Recurse -Force

    # Unblock all files
    Get-ChildItem -Path $addinsDir -Recurse -File -ErrorAction SilentlyContinue |
        ForEach-Object { Unblock-File -Path $_.FullName -ErrorAction SilentlyContinue }

    return Test-PluginInstall -AddinsDir $addinsDir -Year $year -Tag "local"
}
```

- [ ] **Step 3: Build and verify**

Run the installer locally:
```powershell
powershell -ExecutionPolicy Bypass -File scripts\install.ps1 -LocalZip "." -RevitVersion 2025
```

- [ ] **Step 4: Commit**

```bash
git add scripts/install.ps1
git commit -m "feat: add -LocalZip parameter for offline installation from extracted ZIP"
```

---

## Task 13: Include .bat Files in Release ZIP

**Files:**
- Modify: `.github/workflows/release.yml`

- [ ] **Step 1: Add .bat and scripts to release assets**

In `release.yml`, in the "Create release assets" step, after the `Copy-Item` lines for the server (around line 115), add:

```powershell
              # Include installer scripts and .bat files
              Copy-Item -Path 'INSTALLA.bat'   -Destination "$addinDir/INSTALLA.bat" -Force
              Copy-Item -Path 'DISINSTALLA.bat' -Destination "$addinDir/DISINSTALLA.bat" -Force
              if (Test-Path 'scripts') {
                Copy-Item -Path 'scripts' -Destination "$addinDir/scripts" -Recurse -Force
              }
```

- [ ] **Step 2: Commit**

```bash
git add .github/workflows/release.yml
git commit -m "ci: include INSTALLA.bat, DISINSTALLA.bat, and scripts in release ZIPs"
```

---

## Task 14: Final Integration Commit

- [ ] **Step 1: Push all changes**

```bash
git push origin main
```

- [ ] **Step 2: Manual verification checklist**

1. Open Revit with the plugin
2. Check that `logs/mcp-YYYY-MM-DD.log` is created in the plugin folder
3. Check that `mcp-port.txt` is written when clicking MCP Switch
4. Check that `claude_desktop_config.json` has the correct `revit-mcp` entry
5. Click the "Verify Connection" button in the MCP panel — all checks should pass
6. Close Revit, block port 8080 with another program, reopen Revit — should auto-switch to 8081

- [ ] **Step 3: Tag new release**

```bash
# Only after all verification passes
scripts/release.ps1 -Version 2.2.0
git push origin main --tags
```
