#Requires -Version 5.1
<#
.SYNOPSIS
    Deep diagnostic for mcp-servers-for-revit -- checks every file, path, and connection.

.DESCRIPTION
    Prints a detailed PASS/FAIL/WARN for every component in the chain.
    Designed to be run on the machine where the problem occurs; copy-paste output for support.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\diagnose.ps1

.EXAMPLE
    # One-liner from GitHub
    powershell -ExecutionPolicy Bypass -Command "irm https://raw.githubusercontent.com/mcp-servers-for-revit/mcp-servers-for-revit/main/scripts/diagnose.ps1 | iex"
#>

$ErrorActionPreference = 'Continue'
$ProgressPreference    = 'SilentlyContinue'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

# -- Load shared module --------------------------------------------------------
$_commonPath = if ($PSScriptRoot) { Join-Path $PSScriptRoot 'common.ps1' } else { $null }
if ($_commonPath -and (Test-Path $_commonPath)) {
    . $_commonPath
} else {
    # Inline fallback for irm | iex usage
    $REPO          = 'mcp-servers-for-revit/mcp-servers-for-revit'
    $PLUGIN_NAME   = 'mcp-servers-for-revit'
    $PLUGIN_FOLDER = 'revit_mcp_plugin'
    $NPM_PACKAGE   = 'mcp-server-for-revit'
    $ADDIN_FILE    = "$PLUGIN_NAME.addin"
    $MIN_NODE      = 18
    $REVIT_YEARS   = 2020..2027
    $MCP_HOST      = '127.0.0.1'
    $MCP_PORT      = 8080
    function Get-NodeStatus {
        $result = [PSCustomObject]@{
            Available = $false; Version = $null; Major = 0
            Path = $null; MeetsMinimum = $false
        }
        $nodeCmd = Get-Command node -ErrorAction SilentlyContinue
        if ($nodeCmd) {
            $result.Available = $true
            $result.Path      = $nodeCmd.Source
            $result.Version   = (& node --version 2>$null).TrimStart('v')
            $result.Major     = [int](($result.Version -split '\.')[0])
            $result.MeetsMinimum = $result.Major -ge $MIN_NODE
        }
        return $result
    }
    function Get-NpmGlobalPrefix {
        $prefix = (cmd /c "npm prefix -g" 2>$null | Select-Object -First 1)
        if ($prefix) { $prefix = $prefix.Trim() }
        if ([string]::IsNullOrWhiteSpace($prefix)) { return $null }
        return $prefix
    }
    function Get-ClaudeDesktopDir {
        $candidates = @(
            "$env:APPDATA\Claude",
            (Get-ChildItem "$env:LOCALAPPDATA\Packages" -Filter "Claude_*" -ErrorAction SilentlyContinue |
                Select-Object -First 1 |
                ForEach-Object { "$($_.FullName)\LocalCache\Roaming\Claude" })
        )
        foreach ($c in $candidates) {
            if ($c -and (Test-Path $c)) { return $c }
        }
        return $null
    }
    function Get-ClaudeDesktopCandidates {
        return @(
            "$env:APPDATA\Claude",
            (Get-ChildItem "$env:LOCALAPPDATA\Packages" -Filter "Claude_*" -ErrorAction SilentlyContinue |
                Select-Object -First 1 |
                ForEach-Object { "$($_.FullName)\LocalCache\Roaming\Claude" })
        )
    }
    function Get-ClaudeDesktopConfig {
        param([string]$ClaudeDir)
        $configPath = "$ClaudeDir\claude_desktop_config.json"
        $result = [PSCustomObject]@{
            Exists = $false; Path = $configPath; Config = $null
            HasRevitMcp = $false; RevitMcpEntry = $null
        }
        if (Test-Path $configPath) {
            $result.Exists = $true
            try {
                $result.Config = Get-Content $configPath -Raw | ConvertFrom-Json
                if ($result.Config.mcpServers -and $result.Config.mcpServers.'revit-mcp') {
                    $result.HasRevitMcp   = $true
                    $result.RevitMcpEntry = $result.Config.mcpServers.'revit-mcp'
                }
            } catch {}
        }
        return $result
    }
    function Get-RevitVersions {
        param([string[]]$Limit = @())
        $found = @()
        foreach ($year in $REVIT_YEARS) {
            if ($Limit.Count -gt 0 -and $year.ToString() -notin $Limit) { continue }
            $addinsDir = "$env:APPDATA\Autodesk\Revit\Addins\$year"
            $regPaths  = @(
                "HKLM:\SOFTWARE\Autodesk\Revit\Autodesk Revit $year",
                "HKLM:\SOFTWARE\WOW6432Node\Autodesk\Revit\Autodesk Revit $year"
            )
            $exePath = "C:\Program Files\Autodesk\Revit $year\Revit.exe"
            $inRegistry = ($regPaths | Where-Object { Test-Path $_ }).Count -gt 0
            $inExe      = Test-Path $exePath
            # An Addins folder on its own proves nothing: other add-ins create one for Revit
            # versions that were never installed. It counts only when our plugin is there.
            $hasPlugin  = Test-Path (Join-Path $addinsDir $ADDIN_FILE)
            if ($inRegistry -or $inExe -or $hasPlugin) {
                $found += [PSCustomObject]@{ Year = $year; AddinsDir = $addinsDir }
            }
        }
        return $found
    }
    function Test-RevitInstalled {
        param([int]$Year)
        $addinsDir = "$env:APPDATA\Autodesk\Revit\Addins\$Year"
        $regPaths  = @(
            "HKLM:\SOFTWARE\Autodesk\Revit\Autodesk Revit $Year",
            "HKLM:\SOFTWARE\WOW6432Node\Autodesk\Revit\Autodesk Revit $Year"
        )
        $exePath   = "C:\Program Files\Autodesk\Revit $Year\Revit.exe"
        $inReg     = ($regPaths | Where-Object { Test-Path $_ }).Count -gt 0
        $inExe     = Test-Path $exePath
        $inAddins  = Test-Path $addinsDir
        return ($inReg -or $inExe -or $inAddins)
    }
}

function Pass  { param([string]$m) Write-Host "  [PASS] $m" -ForegroundColor Green  }
function Fail  { param([string]$m) Write-Host "  [FAIL] $m" -ForegroundColor Red    }
function Warn  { param([string]$m) Write-Host "  [WARN] $m" -ForegroundColor Yellow }
function Info  { param([string]$m) Write-Host "         $m" -ForegroundColor Gray   }
function InfoG { param([string]$m) Write-Host "         $m" -ForegroundColor Green  }
function Head  { param([string]$m) Write-Host "`n  ---- $m ----" -ForegroundColor Cyan }
function Sub   { param([string]$m) Write-Host "  >> $m" -ForegroundColor White }
function FileOk{ param([string]$p) Write-Host "         [FILE OK]  $p" -ForegroundColor DarkGreen }
function FileMiss{ param([string]$p) Write-Host "         [MISSING]  $p" -ForegroundColor Red }

Write-Host ""
Write-Host "  ================================================================" -ForegroundColor Cyan
Write-Host "      mcp-servers-for-revit  --  Deep Diagnostic"                    -ForegroundColor Cyan
Write-Host "      Machine : $env:COMPUTERNAME"                                  -ForegroundColor DarkCyan
Write-Host "      User    : $env:USERDOMAIN\$env:USERNAME"                      -ForegroundColor DarkCyan
Write-Host "      Date    : $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"          -ForegroundColor DarkCyan
Write-Host "      OS      : $([System.Environment]::OSVersion.VersionString)"   -ForegroundColor DarkCyan
Write-Host "      PS      : $($PSVersionTable.PSVersion)"                        -ForegroundColor DarkCyan
Write-Host "  ================================================================" -ForegroundColor Cyan


# ============================================================================
# 1. NODE.JS
# ============================================================================
Head "1. Node.js"

$nodeStatus = Get-NodeStatus
if ($nodeStatus.Available) {
    if ($nodeStatus.MeetsMinimum) {
        Pass "Node.js $($nodeStatus.Version)"
        FileOk $nodeStatus.Path
    } else {
        Fail "Node.js $($nodeStatus.Version) found -- v18+ required"
        FileOk $nodeStatus.Path
    }
} else {
    Fail "Node.js not found in PATH"
    Info "Install from: https://nodejs.org"
}

$npmCmd = Get-Command npm -ErrorAction SilentlyContinue
if ($npmCmd) {
    $npmVer = (cmd /c "npm --version" 2>$null | Select-Object -First 1).Trim()
    Pass "npm $npmVer"
    FileOk $npmCmd.Source

    # Global prefix (npm prefix -g works on all npm versions including v10)
    $npmPrefix = Get-NpmGlobalPrefix
    if ($npmPrefix) {
        Info "npm global prefix : $npmPrefix"
        if (Test-Path $npmPrefix) {
            FileOk $npmPrefix
        } else {
            FileMiss $npmPrefix
        }
    }

    # Check local MCP server (installed with the plugin)
    $serverJs = $null
    foreach ($year in ($REVIT_YEARS | Sort-Object -Descending)) {
        $candidate = "$env:APPDATA\Autodesk\Revit\Addins\$year\$PLUGIN_FOLDER\Commands\RevitMCPCommandSet\server\build\index.js"
        if (Test-Path $candidate) { $serverJs = $candidate; break }
    }
    if ($serverJs) {
        Pass "Local MCP server found"
        FileOk $serverJs
    } else {
        Fail "Local MCP server NOT found -- reinstall the plugin"
        Info "Expected: %APPDATA%\Autodesk\Revit\Addins\{year}\$PLUGIN_FOLDER\Commands\RevitMCPCommandSet\server\build\index.js"
    }
} else {
    Fail "npm not found in PATH"
}

$npxCmd = Get-Command npx -ErrorAction SilentlyContinue
if ($npxCmd) {
    Pass "npx found"
    FileOk $npxCmd.Source
} else {
    Warn "npx not found in PATH"
}


# ============================================================================
# 2. CLAUDE DESKTOP
# ============================================================================
Head "2. Claude Desktop"

$claudeDir  = Get-ClaudeDesktopDir
$candidates = Get-ClaudeDesktopCandidates

# Show all candidate paths (whether they exist or not)
Sub "Candidate paths checked:"
foreach ($c in $candidates) {
    if (-not $c) { continue }
    if (Test-Path $c) {
        FileOk $c
        if ($claudeDir -eq $c) { Info "  ^ ACTIVE path" }
    } else {
        FileMiss $c
    }
}

if ($claudeDir) {
    Pass "Claude Desktop folder: $claudeDir"
} else {
    Fail "Claude Desktop folder not found -- install Claude Desktop first"
}

$cfgInfo      = if ($claudeDir) { Get-ClaudeDesktopConfig $claudeDir } else { $null }
$claudeConfig = if ($cfgInfo) { $cfgInfo.Path } else { $null }

if ($cfgInfo -and $cfgInfo.Exists) {
    Pass "claude_desktop_config.json exists"
    FileOk $claudeConfig
    Sub "Contents:"
    Get-Content $claudeConfig | ForEach-Object { Info "  $_" }

    if ($cfgInfo.Config) {
        if ($cfgInfo.HasRevitMcp) {
            $srv = $cfgInfo.RevitMcpEntry
            Pass "revit-mcp entry found"
            Info "  command : $($srv.command)"
            if ($srv.args) {
                Info "  args    : $($srv.args -join ' ')"
                # Verify the .cmd path referenced in the config
                $configCmd = $srv.args | Where-Object { $_ -match '\.cmd$' } | Select-Object -First 1
                if ($configCmd) {
                    if (Test-Path $configCmd) {
                        Pass ".cmd path in config exists"
                        FileOk $configCmd
                    } else {
                        Fail ".cmd path in config MISSING"
                        FileMiss $configCmd
                    }
                }
            }
        } else {
            Fail "revit-mcp entry NOT found in mcpServers"
            Info "  Run the installer to configure it automatically"
        }
    } else {
        Fail "Could not parse claude_desktop_config.json"
    }
} elseif ($claudeDir) {
    Fail "claude_desktop_config.json NOT found in $claudeDir"
    FileMiss "$claudeDir\claude_desktop_config.json"
    Info "Run the installer to create it"
} else {
    Warn "Cannot check config -- Claude Desktop folder not found"
}

# MCP server logs
Sub "MCP server log files:"
$logCandidates = @(
    (& { if ($claudeDir) { "$claudeDir\logs" } }),
    "$env:APPDATA\Claude\logs"
) | Where-Object { $_ }
foreach ($logDir in $logCandidates) {
    if (Test-Path $logDir) {
        $logs = Get-ChildItem $logDir -Filter "mcp*.log" -ErrorAction SilentlyContinue
        if ($logs) {
            foreach ($log in $logs | Sort-Object LastWriteTime -Descending | Select-Object -First 3) {
                Info "  $($log.FullName)  ($([math]::Round($log.Length/1KB,1)) KB, $($log.LastWriteTime.ToString('yyyy-MM-dd HH:mm')))"
                # Show last 5 lines of the most recent log
                if ($log -eq ($logs | Sort-Object LastWriteTime -Descending | Select-Object -First 1)) {
                    $tail = Get-Content $log.FullName -ErrorAction SilentlyContinue | Select-Object -Last 8
                    if ($tail) {
                        Info "  -- last lines --"
                        $tail | ForEach-Object { Info "    $_" }
                    }
                }
            }
        } else {
            Info "  No mcp*.log in $logDir"
        }
    }
}


# ============================================================================
# 3. REVIT INSTALLATION + PLUGIN
# ============================================================================
Head "3. Revit plugin installation"

$anyRevit = $false
foreach ($year in $REVIT_YEARS) {
    if (-not (Test-RevitInstalled $year)) { continue }

    $addinsDir   = "$env:APPDATA\Autodesk\Revit\Addins\$year"
    $addinFile   = "$addinsDir\$ADDIN_FILE"
    $pluginRoot  = "$addinsDir\$PLUGIN_FOLDER"
    $anyRevit = $true

    Write-Host ""
    Sub "Revit $year"

    # Revit executable
    $revitExe = "C:\Program Files\Autodesk\Revit $year\Revit.exe"
    if (Test-Path $revitExe) {
        $rev = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($revitExe)
        Info "  Revit.exe  $($rev.FileVersion)  --  $revitExe"
    }

    # Addins folder
    if (Test-Path $addinsDir) {
        FileOk $addinsDir
    } else {
        FileMiss $addinsDir
    }

    # .addin manifest
    if (Test-Path $addinFile) {
        Pass "  .addin manifest present"
        FileOk "  $addinFile"
        try {
            [xml]$xml = Get-Content $addinFile -Raw
            $asmRel   = $xml.RevitAddIns.AddIn.Assembly
            $asmFull  = Join-Path $addinsDir $asmRel
            Info "  Assembly (relative) : $asmRel"
            if (Test-Path $asmFull) {
                Pass "  Assembly DLL exists"
                FileOk "  $asmFull"
                $ver = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($asmFull)
                Info "  DLL version : $($ver.FileVersion)"
            } else {
                Fail "  Assembly DLL MISSING"
                FileMiss "  $asmFull"
            }
            $clientId = $xml.RevitAddIns.AddIn.ClientId
            Info "  ClientId : $clientId"
        } catch { Warn "  Cannot parse .addin: $_" }
    } else {
        Fail "  .addin manifest MISSING"
        FileMiss "  $addinFile"
    }

    # Plugin folder tree
    if (Test-Path $pluginRoot) {
        $allDlls = Get-ChildItem $pluginRoot -Filter '*.dll' -Recurse -ErrorAction SilentlyContinue
        Pass "  Plugin folder present ($($allDlls.Count) DLLs)"
        FileOk "  $pluginRoot"

        # List all DLLs with version
        Sub "  DLLs:"
        foreach ($dll in $allDlls | Sort-Object FullName) {
            $dllVer = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($dll.FullName)
            $blocked = (Get-Item $dll.FullName -Stream 'Zone.Identifier' -ErrorAction SilentlyContinue) -ne $null
            $blockedLabel = if ($blocked) { " [BLOCKED]" } else { "" }
            Info "    $($dll.FullName.Replace($pluginRoot,''))  v$($dllVer.FileVersion)$blockedLabel"
        }
    } else {
        Fail "  Plugin folder MISSING"
        FileMiss "  $pluginRoot"
    }

    # CommandSet DLL per Revit year
    $cmdSetDir = "$pluginRoot\Commands\RevitMCPCommandSet\$year"
    $cmdSetDll = "$cmdSetDir\RevitMCPCommandSet.dll"
    if (Test-Path $cmdSetDll) {
        Pass "  RevitMCPCommandSet.dll for Revit $year"
        FileOk "  $cmdSetDll"
    } else {
        Fail "  RevitMCPCommandSet.dll MISSING for Revit $year"
        FileMiss "  $cmdSetDll"
        Info "  Expected: $cmdSetDir"
        # List what IS in Commands folder
        $cmdDir = "$pluginRoot\Commands"
        if (Test-Path $cmdDir) {
            Info "  Commands folder contents:"
            Get-ChildItem $cmdDir -Recurse -ErrorAction SilentlyContinue |
                ForEach-Object { Info "    $($_.FullName)" }
        }
    }

    # commandRegistry.json
    $registryFile = "$pluginRoot\Commands\commandRegistry.json"
    if (Test-Path $registryFile) {
        FileOk "  $registryFile"
        try {
            $reg = Get-Content $registryFile -Raw | ConvertFrom-Json
            $totalCount = $reg.Commands.Count
            # A missing "enabled" field means enabled: the plugin's CommandConfig.Enabled
            # defaults to true and the shipped registry omits the field, so treating it as
            # disabled reported a healthy install as "ALL DISABLED".
            $enabled  = @($reg.Commands | Where-Object { $null -eq $_.enabled -or $_.enabled })
            $disabled = @($reg.Commands | Where-Object { $null -ne $_.enabled -and -not $_.enabled })
            $enabledCount = $enabled.Count
            if ($totalCount -eq 0) {
                Fail "  commandRegistry.json is EMPTY (no commands)"
                Info "  Open Revit -> Add-Ins -> Settings -> Select All -> Save"
            } elseif ($enabledCount -eq 0) {
                Warn "  commandRegistry.json has $totalCount commands but ALL DISABLED"
                Info "  Open Revit -> Add-Ins -> Settings -> Select All -> Save"
            } else {
                Pass "  commandRegistry.json: $enabledCount/$totalCount commands enabled"
            }
            # The registry entries are keyed by "commandName" - $_.CommandId printed blanks.
            if ($disabled.Count -gt 0) {
                Sub "  Disabled commands:"
                $disabled | ForEach-Object { Info "    $($_.commandName)" }
            }
        } catch {
            Fail "  commandRegistry.json parse error: $_"
        }
    } else {
        Fail "  commandRegistry.json MISSING"
        FileMiss "  $registryFile"
        Info "  This file is created when Revit loads the plugin for the first time"
        Info "  -> Start Revit, open a project, then check Add-Ins tab"
    }

    # Blocked DLLs
    if (Test-Path $pluginRoot) {
        $blocked = Get-ChildItem $pluginRoot -Recurse -Filter '*.dll' -ErrorAction SilentlyContinue |
            Where-Object { (Get-Item $_.FullName -Stream 'Zone.Identifier' -ErrorAction SilentlyContinue) -ne $null }
        if ($blocked.Count -gt 0) {
            Fail "  $($blocked.Count) DLL(s) BLOCKED by Windows (Zone.Identifier)"
            $blocked | ForEach-Object { FileMiss "  [blocked] $($_.FullName)" }
            Info "  Fix: Get-ChildItem '$pluginRoot' -Recurse | Unblock-File"
        } else {
            Pass "  No blocked DLLs"
        }
    }
}

if (-not $anyRevit) {
    Fail "No Revit installation detected (2020-2027)"
}


# ============================================================================
# 4. REVIT PROCESS
# ============================================================================
Head "4. Revit process"
$revitProcs = Get-Process -Name "Revit" -ErrorAction SilentlyContinue
if ($revitProcs) {
    foreach ($p in $revitProcs) {
        Pass "Revit running  (PID $($p.Id))  '$($p.MainWindowTitle)'"
        $pPath = (Get-Process -Id $p.Id -ErrorAction SilentlyContinue).Path
        if ($pPath) { FileOk $pPath }
    }
} else {
    Fail "Revit is NOT running"
    Info "Start Revit and click Add-Ins -> Revit MCP Switch before using Claude"
}


# ============================================================================
# 5. TCP CONNECTION TO REVIT PLUGIN (port 8080)
# ============================================================================
Head "5. TCP connection to Revit plugin (${MCP_HOST}:${MCP_PORT})"
try {
    $tcp = New-Object System.Net.Sockets.TcpClient
    $ar  = $tcp.BeginConnect($MCP_HOST, $MCP_PORT, $null, $null)
    $ok  = $ar.AsyncWaitHandle.WaitOne(3000, $false)
    if ($ok -and $tcp.Connected) {
        Pass "Port $MCP_PORT is OPEN -- Revit plugin server listening"
        $tcp.Close()

        # JSON-RPC test
        Head "5b. JSON-RPC test (get_project_info)"
        try {
            $tcp2   = New-Object System.Net.Sockets.TcpClient
            $tcp2.Connect($MCP_HOST, $MCP_PORT)
            $stream = $tcp2.GetStream()
            $stream.ReadTimeout  = 5000
            $stream.WriteTimeout = 5000

            $payload = '{"jsonrpc":"2.0","method":"get_project_info","params":{},"id":"diag-001"}' + "`n"
            $bytes   = [System.Text.Encoding]::UTF8.GetBytes($payload)
            $stream.Write($bytes, 0, $bytes.Length)

            $buffer   = New-Object byte[] 65536
            $sb       = New-Object System.Text.StringBuilder
            $deadline = [DateTime]::Now.AddSeconds(5)
            while ([DateTime]::Now -lt $deadline) {
                if ($stream.DataAvailable) {
                    $n = $stream.Read($buffer, 0, $buffer.Length)
                    $sb.Append([System.Text.Encoding]::UTF8.GetString($buffer, 0, $n)) | Out-Null
                    if ($sb.ToString().Contains("`n")) { break }
                }
                Start-Sleep -Milliseconds 50
            }

            $raw = $sb.ToString().Trim()
            if ($raw -ne '') {
                try {
                    $resp = $raw | ConvertFrom-Json
                    if ($resp.error) {
                        Warn "JSON-RPC error: $($resp.error.message)"
                    } else {
                        Pass "JSON-RPC response received"
                        if ($resp.result) {
                            $result   = $resp.result
                            $projName = if ($result.projectName) { $result.projectName } elseif ($result.name) { $result.name } else { '(no name)' }
                            $revVer   = if ($result.revitVersion) { $result.revitVersion } elseif ($result.version) { $result.version } else { '?' }
                            InfoG "  Project : $projName"
                            InfoG "  Version : $revVer"
                        }
                        Sub "Raw response:"
                        Info "  $raw"
                    }
                } catch {
                    Warn "Response received but could not parse JSON: $raw"
                }
            } else {
                Fail "No response within 5 seconds"
                Info "Make sure 'Revit MCP Switch' is ON in Revit (Add-Ins tab)"
            }
            $stream.Close(); $tcp2.Close()
        } catch {
            Fail "JSON-RPC test failed: $_"
        }
    } else {
        $tcp.Close()
        Fail "Port $MCP_PORT is CLOSED -- plugin server NOT listening"
        if ($revitProcs) {
            Info "Revit is running but MCP server not started"
            Info "-> In Revit: Add-Ins tab -> click 'Revit MCP Switch'"
        } else {
            Info "-> Start Revit first, then click 'Revit MCP Switch'"
        }
    }
} catch {
    Fail "TCP test failed: $_"
}


# ============================================================================
# 6. ENVIRONMENT PATH SUMMARY
# ============================================================================
Head "6. PATH entries (nodejs/npm related)"
$env:PATH -split ';' | Where-Object { $_ -match 'node|npm|nvm|appdata\\roaming\\npm' -or $_ -match 'nodejs' } |
    ForEach-Object { Info "  $_" }


# ============================================================================
# SUMMARY
# ============================================================================
Write-Host ""
Write-Host "  ================================================================" -ForegroundColor Cyan
Write-Host "      Diagnostic complete. Copy-paste the full output above for support." -ForegroundColor Cyan
Write-Host "  ================================================================" -ForegroundColor Cyan
Write-Host ""
