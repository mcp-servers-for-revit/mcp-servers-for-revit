#Requires -Version 5.1
<#
.SYNOPSIS
    Shared constants and helper functions for mcp-servers-for-revit scripts.

.DESCRIPTION
    Dot-source this file from install.ps1, diagnose.ps1, and fix-mcp.ps1.
    Contains common logic for Revit detection, Node.js checks, Claude Desktop
    detection, npm package resolution, and config file management.

    Usage:  . "$PSScriptRoot\common.ps1"
#>

# -- Constants -----------------------------------------------------------------
$script:REPO           = 'mcp-servers-for-revit/mcp-servers-for-revit'
$script:PLUGIN_NAME    = 'mcp-servers-for-revit'
$script:PLUGIN_FOLDER  = 'revit_mcp_plugin'
$script:NPM_PACKAGE    = 'mcp-server-for-revit'
$script:ADDIN_FILE     = "$PLUGIN_NAME.addin"
$script:MIN_NODE       = 18
$script:REVIT_YEARS    = 2020..2027
$script:MCP_HOST       = '127.0.0.1'
$script:MCP_PORT       = 8080

# ==============================================================================
# Revit Detection
# ==============================================================================

<#
.SYNOPSIS
    Detect installed Revit versions by checking registry, Addins folder, and exe.

.PARAMETER Limit
    Optional array of year strings to restrict detection (e.g. @('2025')).

.OUTPUTS
    Array of PSCustomObject with Year and AddinsDir properties.
#>
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
        # An Addins folder on its own proves nothing: other add-ins (pyRevit, RevitLookup,
        # ...) create one for Revit versions that were never installed, and installing into
        # those wastes ~50 MB each. It counts only when our plugin is already there, so that
        # uninstall and repair still find existing installations.
        $hasPlugin  = Test-Path (Join-Path $addinsDir $ADDIN_FILE)
        if ($inRegistry -or $inExe -or $hasPlugin) {
            $found += [PSCustomObject]@{ Year = $year; AddinsDir = $addinsDir }
        }
    }
    return $found
}

<#
.SYNOPSIS
    Test whether a specific Revit year is detected on this machine.

.PARAMETER Year
    Revit year (e.g. 2025).

.OUTPUTS
    Boolean.
#>
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
    # See Get-RevitVersions: an Addins folder alone is created by other add-ins too.
    $hasPlugin = Test-Path (Join-Path $addinsDir $ADDIN_FILE)
    return ($inReg -or $inExe -or $hasPlugin)
}

# ==============================================================================
# Node.js Detection
# ==============================================================================

<#
.SYNOPSIS
    Find the best available Node.js executable.
    Checks the system PATH first, then falls back to the portable node.exe
    bundled alongside the plugin (shipped in server/runtime/node.exe).

.OUTPUTS
    String full path to node.exe, or $null if not found anywhere.
#>
function Get-NodePath {
    # 1. System node (already in PATH -- preferred for developers)
    $sysNode = Get-Command node -ErrorAction SilentlyContinue
    if ($sysNode) { return $sysNode.Source }

    # 2. Portable node.exe bundled with the plugin (for end-users without Node.js)
    foreach ($year in ($REVIT_YEARS | Sort-Object -Descending)) {
        $portableNode = "$env:APPDATA\Autodesk\Revit\Addins\$year\$PLUGIN_FOLDER\Commands\RevitMCPCommandSet\server\runtime\node.exe"
        if (Test-Path $portableNode) { return $portableNode }
    }

    return $null
}

<#
.SYNOPSIS
    Check if Node.js is installed and meets the minimum version requirement.
    Checks system PATH first, then the portable runtime bundled with the plugin.

.OUTPUTS
    PSCustomObject with properties: Available (bool), Version (string),
    Major (int), Path (string), MeetsMinimum (bool), IsBundled (bool).
#>
function Get-NodeStatus {
    $result = [PSCustomObject]@{
        Available    = $false
        Version      = $null
        Major        = 0
        Path         = $null
        MeetsMinimum = $false
        IsBundled    = $false
    }
    $nodePath = Get-NodePath
    if ($nodePath) {
        $result.Available = $true
        $result.Path      = $nodePath
        $result.IsBundled = -not [bool](Get-Command node -ErrorAction SilentlyContinue)
        $result.Version   = (& "$nodePath" --version 2>$null).TrimStart('v')
        $result.Major     = [int](($result.Version -split '\.')[0])
        $result.MeetsMinimum = $result.Major -ge $MIN_NODE
    }
    return $result
}

# ==============================================================================
# npm Package Resolution
# ==============================================================================

<#
.SYNOPSIS
    Get the npm global prefix directory.

.OUTPUTS
    String path or $null.
#>
function Get-NpmGlobalPrefix {
    $prefix = (cmd /c "npm prefix -g" 2>$null | Select-Object -First 1)
    if ($prefix) { $prefix = $prefix.Trim() }
    if ([string]::IsNullOrWhiteSpace($prefix)) { return $null }
    return $prefix
}

<#
.SYNOPSIS
    Resolve the .cmd file path for a globally installed npm package.

.PARAMETER PkgName
    The npm package name (e.g. 'mcp-server-for-revit').

.OUTPUTS
    String path to the .cmd file, or $null if not found.
#>
function Get-NpmCmdPath {
    param([string]$PkgName)
    $prefix = Get-NpmGlobalPrefix
    if (-not $prefix) { return $null }
    $p = Join-Path $prefix "$PkgName.cmd"
    if (Test-Path $p) { return $p }
    return $null
}

# ==============================================================================
# Claude Desktop Detection
# ==============================================================================

<#
.SYNOPSIS
    Find the Claude Desktop configuration directory (standard or MSIX install).

.OUTPUTS
    String path to the Claude directory, or $null if not found.
#>
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

<#
.SYNOPSIS
    Get all Claude Desktop candidate paths (for diagnostic display).

.OUTPUTS
    Array of strings (some may be $null if MSIX is not present).
#>
function Get-ClaudeDesktopCandidates {
    return @(
        "$env:APPDATA\Claude",
        (Get-ChildItem "$env:LOCALAPPDATA\Packages" -Filter "Claude_*" -ErrorAction SilentlyContinue |
            Select-Object -First 1 |
            ForEach-Object { "$($_.FullName)\LocalCache\Roaming\Claude" })
    )
}

# ==============================================================================
# Claude Desktop Config Management
# ==============================================================================

<#
.SYNOPSIS
    Read and parse the claude_desktop_config.json file.

.PARAMETER ClaudeDir
    Path to the Claude Desktop directory.

.OUTPUTS
    PSCustomObject with properties: Exists (bool), Path (string),
    Config (parsed object or $null), HasRevitMcp (bool),
    RevitMcpEntry (object or $null).
#>
# ==============================================================================
# MCP Server (local installation)
# ==============================================================================

<#
.SYNOPSIS
    Find the path to the locally installed MCP server (index.js).
    Searches all Revit versions from newest to oldest and returns the first found.

.OUTPUTS
    String path to index.js, or $null if not installed.
#>
function Get-McpServerPath {
    foreach ($year in ($REVIT_YEARS | Sort-Object -Descending)) {
        $serverJs = "$env:APPDATA\Autodesk\Revit\Addins\$year\$PLUGIN_FOLDER\Commands\RevitMCPCommandSet\server\build\index.js"
        if (Test-Path $serverJs) { return $serverJs }
    }
    return $null
}

<#
.SYNOPSIS
    Build the Claude Desktop mcpServers entry for revit-mcp using the local server.

.PARAMETER ServerPath
    Full path to the server index.js file.

.OUTPUTS
    PSCustomObject suitable for JSON serialisation.
#>
function New-RevitMcpEntry {
    param([string]$ServerPath)
    $nodePath = Get-NodePath
    if ($nodePath) {
        # Use the node executable directly (system or bundled portable)
        return [PSCustomObject]@{ command = $nodePath; args = @($ServerPath) }
    }
    # Last resort: rely on node in PATH via cmd shell
    return [PSCustomObject]@{ command = 'cmd'; args = @('/c', 'node', $ServerPath) }
}

function Get-ClaudeDesktopConfig {
    param([string]$ClaudeDir)
    $configPath = "$ClaudeDir\claude_desktop_config.json"
    $result = [PSCustomObject]@{
        Exists        = $false
        Path          = $configPath
        Config        = $null
        HasRevitMcp   = $false
        RevitMcpEntry = $null
    }
    if (Test-Path $configPath) {
        $result.Exists = $true
        try {
            $result.Config = Get-Content $configPath -Raw | ConvertFrom-Json
            if ($result.Config.mcpServers -and $result.Config.mcpServers.'revit-mcp') {
                $result.HasRevitMcp   = $true
                $result.RevitMcpEntry = $result.Config.mcpServers.'revit-mcp'
            }
        } catch {
            # Config exists but cannot be parsed; caller handles this
        }
    }
    return $result
}
