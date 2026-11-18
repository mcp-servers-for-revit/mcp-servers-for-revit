#Requires -Version 5.1
<#
.SYNOPSIS
    Installer for mcp-servers-for-revit  --  Revit plugin + MCP server.

.DESCRIPTION
    Designed to work on a clean machine with only Revit and PowerShell installed.
    - Forces TLS 1.2 (required for GitHub API on older Windows)
    - Detects installed Revit versions (2020-2027) via registry and filesystem
    - Detects any previous installation and asks: Replace / Skip / Abort
    - Checks that target Revit is not running before installing (files would be locked)
    - Downloads the latest (or specified) pre-built Release from GitHub
    - Verifies download integrity (byte-exact size check)
    - Extracts to the correct Addins folder
    - Unblocks all files (removes Zone.Identifier so Windows loads the DLLs)
    - Verifies all required files and the .addin manifest after extraction
    - Checks Node.js (>= 18) and offers to install it (required for MCP server)
    - Optionally configures Claude Desktop claude_desktop_config.json

.PARAMETER RevitVersion
    Target a specific Revit version (2020-2027).
    If omitted, all detected Revit installations are targeted.

.PARAMETER Tag
    GitHub release tag to install (e.g. "v1.2.0"). Defaults to "latest".

.PARAMETER Uninstall
    Remove the plugin from all detected (or specified) Revit versions and exit.

.PARAMETER Force
    Skip the Replace/Skip/Abort prompt and always replace an existing installation.

.PARAMETER SkipNodeCheck
    Skip the Node.js prerequisite check.

.PARAMETER SkipMcpConfig
    Skip Claude Desktop MCP server configuration.

.EXAMPLE
    .\install.ps1
    # Auto-detect Revit versions, install latest release

.EXAMPLE
    .\install.ps1 -RevitVersion 2025 -Tag v1.2.0

.EXAMPLE
    .\install.ps1 -Uninstall

.EXAMPLE
    powershell -ExecutionPolicy Bypass -Command "irm https://raw.githubusercontent.com/mcp-servers-for-revit/mcp-servers-for-revit/main/scripts/install.ps1 | iex"
    # One-liner install directly from GitHub
#>
param(
    # NOTE: deliberately no [ValidateSet] here - see the validation after the constants.
    [string]$RevitVersion,
    [string]$Tag = 'latest',
    [switch]$Uninstall,
    [switch]$Force,
    [switch]$SkipNodeCheck,
    [switch]$SkipMcpConfig,
    [string]$LocalZip
)

$ErrorActionPreference = 'Stop'
$ProgressPreference    = 'SilentlyContinue'

# When run via `irm ... | iex` the script executes in the caller's scope and a
# pre-existing $Tag variable can override the param() default.  Guard against it.
if ([string]::IsNullOrWhiteSpace($Tag)) { $Tag = 'latest' }

# Force TLS 1.2  --  required for GitHub on older Windows 10 builds
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

# -- Load shared module --------------------------------------------------------
# When run via `irm | iex`, $PSScriptRoot is empty and common.ps1 is unavailable.
# In that case, define constants and functions inline as a fallback.
$_commonPath = if ($PSScriptRoot) { Join-Path $PSScriptRoot 'common.ps1' } else { $null }
if ($_commonPath -and (Test-Path $_commonPath)) {
    . $_commonPath
} else {
    # Inline fallback: constants
    $REPO          = 'mcp-servers-for-revit/mcp-servers-for-revit'
    $PLUGIN_NAME   = 'mcp-servers-for-revit'
    $PLUGIN_FOLDER = 'revit_mcp_plugin'
    $NPM_PACKAGE   = 'mcp-server-for-revit'
    $ADDIN_FILE    = "$PLUGIN_NAME.addin"
    $MIN_NODE      = 18
    $REVIT_YEARS   = 2020..2027

    # Inline fallback: shared functions
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
    function Get-NodePath {
        $sysNode = Get-Command node -ErrorAction SilentlyContinue
        if ($sysNode) { return $sysNode.Source }
        foreach ($year in ($REVIT_YEARS | Sort-Object -Descending)) {
            $p = "$env:APPDATA\Autodesk\Revit\Addins\$year\$PLUGIN_FOLDER\Commands\RevitMCPCommandSet\server\runtime\node.exe"
            if (Test-Path $p) { return $p }
        }
        return $null
    }
    function Get-NodeStatus {
        $result = [PSCustomObject]@{
            Available = $false; Version = $null; Major = 0
            Path = $null; MeetsMinimum = $false; IsBundled = $false
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
    function Get-McpServerPath {
        foreach ($year in ($REVIT_YEARS | Sort-Object -Descending)) {
            $serverJs = "$env:APPDATA\Autodesk\Revit\Addins\$year\$PLUGIN_FOLDER\Commands\RevitMCPCommandSet\server\build\index.js"
            if (Test-Path $serverJs) { return $serverJs }
        }
        return $null
    }
    function New-RevitMcpEntry {
        param([string]$ServerPath)
        $nodePath = Get-NodePath
        if ($nodePath) {
            return [PSCustomObject]@{ command = $nodePath; args = @($ServerPath) }
        }
        return [PSCustomObject]@{ command = 'cmd'; args = @('/c', 'node', $ServerPath) }
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
}

# -- Colour helpers ------------------------------------------------------------
function Write-Step { param([string]$m) Write-Host "  [*] $m" -ForegroundColor Cyan   }
function Write-Ok   { param([string]$m) Write-Host "  [+] $m" -ForegroundColor Green  }
function Write-Warn { param([string]$m) Write-Host "  [!] $m" -ForegroundColor Yellow }
function Write-Err  { param([string]$m) Write-Host "  [-] $m" -ForegroundColor Red    }
function Write-Info { param([string]$m) Write-Host "      $m" -ForegroundColor Gray   }

# -- Validate -RevitVersion ----------------------------------------------------
# Done here instead of with [ValidateSet] on the param: run via `irm ... | iex` the
# script executes in the caller's scope, and attaching a ValidateSet attribute to the
# (empty) $RevitVersion variable there fails before the script even starts with
# "The attribute cannot be added because variable RevitVersion with value  would no
# longer be valid".
if ($RevitVersion) {
    $RevitVersion   = $RevitVersion.Trim()
    $supportedYears = $REVIT_YEARS | ForEach-Object { "$_" }
    if ($supportedYears -notcontains $RevitVersion) {
        Write-Err "Unsupported -RevitVersion '$RevitVersion'. Supported: $($supportedYears -join ', ')"
        exit 1
    }
}

# -- Banner --------------------------------------------------------------------
Write-Host ""
Write-Host "  ================================================================" -ForegroundColor Cyan
Write-Host "      mcp-servers-for-revit   --   Installer"                           -ForegroundColor Cyan
Write-Host "      https://github.com/$REPO"                                     -ForegroundColor DarkCyan
Write-Host "  ================================================================" -ForegroundColor Cyan
Write-Host ""

# =============================================================================
# STEP 1  --  SYSTEM CHECKS
# =============================================================================
Write-Host "  STEP 1  --  System checks" -ForegroundColor White

# PowerShell version
if ($PSVersionTable.PSVersion.Major -lt 5) {
    Write-Err "PowerShell 5.1 or later is required (found $($PSVersionTable.PSVersion))."
    Write-Info "Update: https://aka.ms/wmf5download"
    exit 1
}
Write-Ok "PowerShell $($PSVersionTable.PSVersion)"

# Windows 10+
$os = [System.Environment]::OSVersion.Version
if ($os.Major -lt 10) {
    Write-Err "Windows 10 or higher is required."
    exit 1
}
Write-Ok "Windows $($os.Major).$($os.Minor) build $($os.Build)"

# Internet connectivity
Write-Step "Testing internet connectivity..."
try {
    $null = Invoke-WebRequest -Uri 'https://api.github.com' -Method Head `
        -TimeoutSec 10 -Headers @{ 'User-Agent' = 'mcp-revit-installer' } -UseBasicParsing
    Write-Ok "Internet connection OK"
} catch {
    Write-Err "Cannot reach api.github.com  --  check your connection or proxy."
    exit 1
}
Write-Host ""

# =============================================================================
# STEP 2  --  DETECT REVIT INSTALLATIONS
# =============================================================================
Write-Host "  STEP 2  --  Detecting Revit installations" -ForegroundColor White

$limit = if ($RevitVersion) { @($RevitVersion) } else { @() }
$revitInstalls = Get-RevitVersions -Limit $limit

if ($revitInstalls.Count -eq 0) {
    if ($RevitVersion) {
        # Naming a version explicitly is the documented override: Revit may live outside
        # the default folder and without the usual registry keys.
        Write-Warn "Revit $RevitVersion was not detected (no registry entry, no Revit.exe in the default location)."
        Write-Info "Installing anyway because -RevitVersion $RevitVersion was given."
        $revitInstalls = @([PSCustomObject]@{
            Year      = [int]$RevitVersion
            AddinsDir = "$env:APPDATA\Autodesk\Revit\Addins\$RevitVersion"
        })
    } else {
        Write-Err "No Revit installation found (checked 2020-2027)."
        Write-Info "Use -RevitVersion to override: .\install.ps1 -RevitVersion 2025"
        exit 1
    }
}

foreach ($rv in $revitInstalls) {
    Write-Ok "Revit $($rv.Year)  ->  $($rv.AddinsDir)"
}
Write-Host ""

# =============================================================================
# STEP 3  --  UNINSTALL (if requested)
# =============================================================================
if ($Uninstall) {
    Write-Host "  STEP 3  --  Uninstall" -ForegroundColor White

    $toRemove = $revitInstalls | Where-Object {
        (Test-Path "$($_.AddinsDir)\$ADDIN_FILE") -or
        (Test-Path "$($_.AddinsDir)\$PLUGIN_FOLDER")
    }

    if ($toRemove.Count -eq 0) {
        Write-Warn "No installation found to remove."
        exit 0
    }

    Write-Warn "Will remove plugin from: $(($toRemove | ForEach-Object { $_.Year }) -join ', ')"
    $confirm = Read-Host "  Continue? [y/N]"
    if ($confirm -notmatch '^[yY]$') {
        Write-Warn "Uninstall cancelled."
        exit 0
    }

    foreach ($rv in $toRemove) {
        Write-Step "Removing Revit $($rv.Year)..."
        $revitRunning = Get-Process -Name "Revit" -ErrorAction SilentlyContinue |
            Where-Object { $_.Path -match "Revit $($rv.Year)" -or $_.MainWindowTitle -match "$($rv.Year)" }
        if ($revitRunning) {
            Write-Warn "Revit $($rv.Year) appears to be running  --  close it first for a clean removal."
        }
        Remove-Item "$($rv.AddinsDir)\$ADDIN_FILE"         -Force -ErrorAction SilentlyContinue
        Remove-Item "$($rv.AddinsDir)\$PLUGIN_FOLDER"       -Recurse -Force -ErrorAction SilentlyContinue
        Remove-Item "$($rv.AddinsDir)\RevitMCPCommandSet"   -Recurse -Force -ErrorAction SilentlyContinue
        Write-Ok "Revit $($rv.Year)  --  removed"
    }

    Write-Host ""
    Write-Ok "Uninstall complete. Restart Revit to apply changes."
    exit 0
}

# =============================================================================
# STEP 3  --  CHECK FOR EXISTING INSTALLATION
# =============================================================================
Write-Host "  STEP 3  --  Checking for existing installation" -ForegroundColor White

$alreadyInstalled = @($revitInstalls | Where-Object { Test-Path "$($_.AddinsDir)\$ADDIN_FILE" })

if ($alreadyInstalled.Count -gt 0) {
    foreach ($rv in $alreadyInstalled) {
        Write-Warn "Existing installation detected for Revit $($rv.Year)"
    }

    if ($Force) {
        Write-Step "-Force flag set  --  replacing existing installation."
    } else {
        Write-Host ""
        Write-Host "  An existing installation was detected." -ForegroundColor Yellow
        Write-Host "  [R] Replace   --  remove old version and install new  (default)" -ForegroundColor White
        Write-Host "  [S] Skip      --  keep existing, only install on new Revit versions" -ForegroundColor White
        Write-Host "  [A] Abort     --  cancel without any changes" -ForegroundColor White
        Write-Host ""

        do {
            $choice = (Read-Host "  Choose [R/s/a]").Trim().ToLower()
            if ($choice -eq '') { $choice = 'r' }
        } while ($choice -notin @('r','s','a'))

        switch ($choice) {
            'a' { Write-Warn "Installation aborted  --  no changes made."; exit 0 }
            's' {
                $skipYears = $alreadyInstalled | ForEach-Object { $_.Year }
                Write-Step "Skipping: $($skipYears -join ', ')"
                $revitInstalls = @($revitInstalls | Where-Object { $_.Year -notin $skipYears })
                if ($revitInstalls.Count -eq 0) {
                    Write-Warn "Nothing new to install."
                    exit 0
                }
            }
            'r' { Write-Step "Replacing existing installation." }
        }
    }
} else {
    Write-Ok "No existing installation found  --  fresh install"
}
Write-Host ""

# =============================================================================
# STEP 4  --  NODE.JS CHECK
# =============================================================================
if (-not $SkipNodeCheck) {
    Write-Host "  STEP 4  --  Node.js (required for MCP server)" -ForegroundColor White

    $nodeStatus = Get-NodeStatus
    $nodeOk     = $false

    if ($nodeStatus.Available) {
        if ($nodeStatus.MeetsMinimum) {
            if ($nodeStatus.IsBundled) {
                Write-Ok "Node.js $($nodeStatus.Version) (bundled portable runtime -- no system install needed)"
            } else {
                Write-Ok "Node.js $($nodeStatus.Version)"
            }
            $nodeOk = $true
        } else {
            Write-Warn "Node.js $($nodeStatus.Version) found but v$MIN_NODE+ is required"
        }
    } else {
        Write-Warn "Node.js not found (system or bundled)"
    }

    if (-not $nodeOk) {
        Write-Host ""
        Write-Host "  Node.js $MIN_NODE+ is needed to run the MCP server." -ForegroundColor Yellow
        Write-Host "  The Revit plugin will be installed regardless."       -ForegroundColor Yellow
        Write-Host "  Note: if you install from the official Release ZIP, Node.js is bundled automatically." -ForegroundColor DarkGray
        Write-Host ""
        Write-Host "  [1] Install Node.js LTS now (downloads installer)"  -ForegroundColor White
        Write-Host "  [2] Skip  --  I will install Node.js later"            -ForegroundColor White
        Write-Host "  [3] Skip  --  I only need the Revit plugin"            -ForegroundColor White
        Write-Host ""
        $nodeChoice = Read-Host "  Choose [1/2/3]"

        if ($nodeChoice -eq '1') {
            Write-Step "Fetching latest Node.js LTS version..."
            try {
                $nodeIndex = Invoke-RestMethod -Uri 'https://nodejs.org/dist/index.json' `
                    -Headers @{ 'User-Agent' = 'mcp-revit-installer' }
                $lts     = $nodeIndex | Where-Object { $_.lts -ne $false } | Select-Object -First 1
                $arch    = if ([Environment]::Is64BitOperatingSystem) { 'x64' } else { 'x86' }
                $msiUrl  = "https://nodejs.org/dist/$($lts.version)/node-$($lts.version)-$arch.msi"
                $msiPath = Join-Path $env:TEMP "node-lts-installer.msi"

                Write-Step "Downloading Node.js $($lts.version)..."
                Invoke-WebRequest -Uri $msiUrl -OutFile $msiPath `
                    -Headers @{ 'User-Agent' = 'mcp-revit-installer' }
                Write-Ok "Downloaded"

                Write-Step "Launching installer (follow the wizard, then press Enter here)..."
                Start-Process msiexec.exe -ArgumentList "/i `"$msiPath`"" -Wait

                $env:Path = [Environment]::GetEnvironmentVariable('Path','Machine') + ';' +
                            [Environment]::GetEnvironmentVariable('Path','User')

                if (Get-Command node -ErrorAction SilentlyContinue) {
                    Write-Ok "Node.js $( (& node --version 2>$null) ) installed"
                    $nodeOk = $true
                } else {
                    Write-Warn "Node.js installed but not yet in PATH  --  restart PowerShell after this script"
                }
            } catch {
                Write-Err "Node.js install failed: $_"
                Write-Info "Install manually: https://nodejs.org"
            } finally {
                Remove-Item (Join-Path $env:TEMP "node-lts-installer.msi") -Force -ErrorAction SilentlyContinue
            }
        } elseif ($nodeChoice -eq '3') {
            $SkipMcpConfig = $true
        } else {
            Write-Warn "Skipping Node.js  --  install later from https://nodejs.org"
        }
    }

    # No npm package needed -- the local server installed with the plugin is used directly.
    Write-Host ""
}

# =============================================================================
# STEP 5 & 6  --  FETCH RELEASE / INSTALL
# =============================================================================
if ($LocalZip) {
    Write-Host "  STEP 5  --  Using local files (skipping download)" -ForegroundColor White
    Write-Ok "Local installation from: $LocalZip"
    Write-Host ""

    # Go directly to install
    Write-Host "  STEP 6  --  Installing plugin" -ForegroundColor White
} else {
    # =============================================================================
    # STEP 5  --  FETCH RELEASE INFO FROM GITHUB
    # =============================================================================
    Write-Host "  STEP 5  --  Fetching release information" -ForegroundColor White

    $apiHeaders = @{ 'User-Agent' = 'mcp-revit-installer'; 'Accept' = 'application/vnd.github+json' }
    $releaseUrl = if ($Tag -eq 'latest') {
        "https://api.github.com/repos/$REPO/releases/latest"
    } else {
        "https://api.github.com/repos/$REPO/releases/tags/$Tag"
    }

    try {
        $release = Invoke-RestMethod -Uri $releaseUrl -Headers $apiHeaders -ErrorAction Stop
    } catch {
        if ($_.Exception.Response.StatusCode.value__ -eq 404) {
            Write-Err "Release '$Tag' not found."
            Write-Info "Available: https://github.com/$REPO/releases"
        } else {
            Write-Err "Could not fetch release: $_"
        }
        exit 1
    }

    $releaseTag  = $release.tag_name
    $releaseDate = $release.published_at.Substring(0,10)
    Write-Ok "Release: $releaseTag  ($releaseDate)"
    if ($release.body) {
        ($release.body -split "`n" | Select-Object -First 3) | ForEach-Object { Write-Info "  $_" }
    }
    Write-Host ""

    # =============================================================================
    # STEP 6  --  DOWNLOAD & INSTALL
    # =============================================================================
    Write-Host "  STEP 6  --  Installing plugin" -ForegroundColor White
}

function Test-PluginInstall {
    param([string]$AddinsDir, [string]$Year, [string]$Tag)
    Write-Step "Revit $Year  --  verifying..."
    $pluginRoot    = "$AddinsDir\$PLUGIN_FOLDER"
    $commandSetDir = "$pluginRoot\Commands\RevitMCPCommandSet\$Year"
    $serverRoot    = "$pluginRoot\Commands\RevitMCPCommandSet\server"
    $required = @(
        @{ P = "$AddinsDir\$ADDIN_FILE";                    L = "Add-in manifest (.addin)"       },
        @{ P = "$pluginRoot\RevitMCPPlugin.dll";             L = "Main plugin DLL"                },
        @{ P = "$pluginRoot\RevitMCPSDK.dll";                L = "RevitMCP SDK DLL"               },
        @{ P = "$pluginRoot\Newtonsoft.Json.dll";            L = "Newtonsoft.Json"                },
        @{ P = "$pluginRoot\Commands\RevitMCPCommandSet\command.json"; L = "Command manifest"               },
        @{ P = "$commandSetDir\RevitMCPCommandSet.dll";      L = "Command set DLL (Revit $Year)"  },
        @{ P = "$serverRoot\build\index.js";                 L = "MCP server (index.js)"          },
        @{ P = "$serverRoot\runtime\node.exe";               L = "Bundled Node.js runtime"        }
    )
    $allOk = $true
    foreach ($f in $required) {
        if (Test-Path $f.P) {
            Write-Ok "    $($f.L)"
        } else {
            Write-Err "    MISSING: $($f.L)"
            Write-Info "      Expected: $($f.P)"
            $allOk = $false
        }
    }
    # Guard against source-code installs
    $srcFiles = (Get-ChildItem $pluginRoot -Include '*.cs','*.csproj' -Recurse -ErrorAction SilentlyContinue).Count
    if ($srcFiles -gt 0) {
        Write-Err "    Source files found instead of compiled binaries  --  download the ZIP from GitHub Releases"
        $allOk = $false
    }
    $dllCount = (Get-ChildItem $pluginRoot -Filter '*.dll' -Recurse -ErrorAction SilentlyContinue).Count
    Write-Ok "    $dllCount DLL files present"
    # Verify manifest assembly path
    if (Test-Path "$AddinsDir\$ADDIN_FILE") {
        try {
            [xml]$xml = Get-Content "$AddinsDir\$ADDIN_FILE" -Raw
            $asmFull  = Join-Path $AddinsDir $xml.RevitAddIns.AddIn.Assembly
            if (Test-Path $asmFull) {
                Write-Ok "    Manifest assembly path valid"
            } else {
                Write-Err "    Manifest assembly not found: $asmFull"
                $allOk = $false
            }
        } catch { Write-Warn "    Could not parse .addin manifest" }
    }
    Write-Host ""
    if ($allOk) {
        Write-Ok "Revit $Year  --  verified OK ($Tag)"
        return $true
    } else {
        Write-Err "Revit $Year  --  installation incomplete  --  see above"
        Write-Info "Download manually: https://github.com/$REPO/releases/tag/$Tag"
        return $false
    }
}

function Install-ForVersion {
    param([PSCustomObject]$Rv, [object]$Release)
    $year      = $Rv.Year
    $addinsDir = $Rv.AddinsDir

    # -- LOCAL INSTALL (from extracted ZIP / bat launcher) ---------------------
    if ($LocalZip) {
        $localAddin = Join-Path $LocalZip "$ADDIN_FILE"
        $localPlugin = Join-Path $LocalZip $PLUGIN_FOLDER

        if (-not (Test-Path $localAddin)) {
            Write-Err "Local install: $ADDIN_FILE not found in $LocalZip"
            return $false
        }

        # Check for running Revit (same check as remote install)
        $allRevitProcs = Get-Process -Name "Revit" -ErrorAction SilentlyContinue
        $thisRunning = $allRevitProcs | Where-Object {
            $_.Path -match [regex]::Escape("Revit $year") -or
            $_.MainWindowTitle -match $year
        }
        if ($thisRunning) {
            Write-Warn "Revit $year is currently running -- close it first."
            $wait = (Read-Host "  [Enter / skip]").Trim().ToLower()
            if ($wait -eq 'skip') { Write-Warn "Skipped Revit $year."; return $false }
        }

        # Remove old installation
        Remove-Item "$addinsDir\$ADDIN_FILE" -Force -ErrorAction SilentlyContinue
        Remove-Item "$addinsDir\$PLUGIN_FOLDER" -Recurse -Force -ErrorAction SilentlyContinue

        # Ensure Addins directory exists
        if (-not (Test-Path $addinsDir)) {
            New-Item -ItemType Directory -Path $addinsDir -Force | Out-Null
        }

        # Copy from local
        Write-Step "Revit $year -- installing from local files..."
        Copy-Item $localAddin "$addinsDir\" -Force
        Copy-Item $localPlugin "$addinsDir\" -Recurse -Force

        # Unblock all files
        Write-Step "Revit $year -- unblocking files..."
        Get-ChildItem -Path "$addinsDir\$PLUGIN_FOLDER" -Recurse -File -ErrorAction SilentlyContinue |
            ForEach-Object { Unblock-File -Path $_.FullName -ErrorAction SilentlyContinue }

        return Test-PluginInstall -AddinsDir $addinsDir -Year $year -Tag "local"
    }
    # -- END LOCAL INSTALL ----------------------------------------------------

    $tag       = $Release.tag_name
    $assetName = "$PLUGIN_NAME-$tag-Revit$year.zip"
    $asset     = $Release.assets | Where-Object { $_.name -eq $assetName }

    if (-not $asset) {
        Write-Warn "Revit $year  --  asset '$assetName' not found in release $tag"
        $avail = ($Release.assets | ForEach-Object { $_.name }) -join ', '
        if ($avail) { Write-Info "Available assets: $avail" }
        return $false
    }

    # Check for running Revit process for this version
    $allRevitProcs = Get-Process -Name "Revit" -ErrorAction SilentlyContinue
    $thisRunning   = $allRevitProcs | Where-Object {
        $_.Path -match [regex]::Escape("Revit $year") -or
        $_.MainWindowTitle -match $year
    }
    if (-not $thisRunning -and $allRevitProcs -and $revitInstalls.Count -eq 1) {
        $thisRunning = $allRevitProcs | Select-Object -First 1
    }
    if ($thisRunning) {
        Write-Warn "Revit $year is currently running  --  DLL files will be locked."
        Write-Warn "Close Revit $year and press Enter to retry, or type 'skip'."
        $wait = (Read-Host "  [Enter / skip]").Trim().ToLower()
        if ($wait -eq 'skip') { Write-Warn "Skipped Revit $year."; return $false }
        if (Get-Process -Name "Revit" -ErrorAction SilentlyContinue) {
            Write-Warn "Revit still running  --  attempting install anyway (some files may fail)."
        }
    }

    $sizeMb  = [math]::Round($asset.size / 1MB, 1)
    Write-Step "Revit $year  --  downloading $assetName ($sizeMb MB)..."

    $tempDir = Join-Path $env:TEMP "mcp-revit-$year"
    $tempZip = Join-Path $tempDir "$assetName"
    if (Test-Path $tempDir) { Remove-Item $tempDir -Recurse -Force }
    New-Item -ItemType Directory -Path $tempDir -Force | Out-Null

    try {
        Invoke-WebRequest -Uri $asset.browser_download_url -OutFile $tempZip `
            -Headers @{ 'User-Agent' = 'mcp-revit-installer' }
    } catch {
        Write-Err "Download failed: $_"
        Remove-Item $tempDir -Recurse -Force -ErrorAction SilentlyContinue
        return $false
    }

    # Byte-exact integrity check
    $dlSize = (Get-Item $tempZip).Length
    if ($dlSize -ne $asset.size) {
        Write-Err "Size mismatch: got $dlSize bytes, expected $($asset.size)  --  download may be corrupt"
        Remove-Item $tempDir -Recurse -Force -ErrorAction SilentlyContinue
        return $false
    }
    Write-Ok "Revit $year  --  download verified"

    # Remove old installation
    Remove-Item "$addinsDir\$ADDIN_FILE"         -Force -ErrorAction SilentlyContinue
    Remove-Item "$addinsDir\$PLUGIN_FOLDER"       -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item "$addinsDir\RevitMCPCommandSet"   -Recurse -Force -ErrorAction SilentlyContinue

    # Ensure Addins directory exists
    if (-not (Test-Path $addinsDir)) {
        New-Item -ItemType Directory -Path $addinsDir -Force | Out-Null
    }

    Write-Step "Revit $year  --  extracting..."
    try {
        Expand-Archive -Path $tempZip -DestinationPath $addinsDir -Force
    } catch {
        Write-Err "Extraction failed: $_"
        Remove-Item $tempDir -Recurse -Force -ErrorAction SilentlyContinue
        return $false
    }

    # Unblock all files  --  Windows adds Zone.Identifier to downloaded files which
    # prevents .NET from loading DLLs without a security prompt
    Write-Step "Revit $year  --  unblocking files..."
    Get-ChildItem -Path $addinsDir -Recurse -File -ErrorAction SilentlyContinue |
        ForEach-Object { Unblock-File -Path $_.FullName -ErrorAction SilentlyContinue }

    Remove-Item $tempDir -Recurse -Force -ErrorAction SilentlyContinue
    return Test-PluginInstall -AddinsDir $addinsDir -Year $year -Tag $tag
}

$installed = 0
foreach ($rv in $revitInstalls) {
    if (Install-ForVersion -Rv $rv -Release $release) { $installed++ }
    Write-Host ""
}

if ($installed -eq 0) {
    Write-Err "No version was installed successfully."
    exit 1
}

# =============================================================================
# STEP 7  --  CONFIGURE CLAUDE DESKTOP
# =============================================================================
if (-not $SkipMcpConfig) {
    Write-Host "  STEP 7  --  Claude Desktop configuration" -ForegroundColor White

    $claudeDir = Get-ClaudeDesktopDir

    if (-not $claudeDir) {
        Write-Warn "Claude Desktop not found  --  skipping automatic configuration"
        Write-Info "Install Claude Desktop from https://claude.ai/download"
        Write-Info "Then re-run: .\install.ps1 -SkipNodeCheck"
    } else {
        Write-Ok "Claude Desktop found: $claudeDir"
        $cfgInfo    = Get-ClaudeDesktopConfig $claudeDir
        $configPath = $cfgInfo.Path

        $config = if ($cfgInfo.Exists -and $cfgInfo.Config) {
            $cfgInfo.Config
        } elseif ($cfgInfo.Exists) {
            Write-Warn "Could not parse existing config  --  backing up"
            Copy-Item $configPath "$configPath.bak" -Force
            [PSCustomObject]@{}
        } else { [PSCustomObject]@{} }

        # Use the local server installed with the plugin (not the npm package)
        $serverPath = Get-McpServerPath
        if (-not $serverPath) {
            Write-Warn "Claude Desktop  --  local server not found, skipping config"
            Write-Info "This should not happen  --  check that the plugin was installed correctly"
        } else {
            $nodePath = Get-NodePath
            if (-not $nodePath) {
                Write-Warn "Claude Desktop  --  Node.js not found (system or bundled), skipping config"
                Write-Info "Install Node.js from https://nodejs.org then re-run: .\install.ps1 -SkipNodeCheck"
            } else {
                if (-not $config.mcpServers) {
                    $config | Add-Member -NotePropertyName 'mcpServers' -NotePropertyValue ([PSCustomObject]@{})
                }
                $revitMcpEntry = New-RevitMcpEntry $serverPath
                Write-Info "Node: $nodePath"
                Write-Info "Server: $serverPath"
                $config.mcpServers | Add-Member -NotePropertyName 'revit-mcp' -NotePropertyValue $revitMcpEntry -Force
                $config | ConvertTo-Json -Depth 10 | Set-Content $configPath -Encoding UTF8
                Write-Ok "Claude Desktop  --  revit-mcp configured"
                Write-Info "Config: $configPath"
            }
        }
    }
    Write-Host ""
}

# =============================================================================
# SUMMARY
# =============================================================================
Write-Host "  ================================================================" -ForegroundColor Green
Write-Host "      Installation complete!  ($installed version(s) installed)"     -ForegroundColor Green
Write-Host "  ================================================================" -ForegroundColor Green
Write-Host ""
Write-Host "  Next steps:" -ForegroundColor White
Write-Host "    1. Open (or restart) Revit"                                     -ForegroundColor Gray
Write-Host "    2. Go to the Add-Ins tab  --  you will see the Revit MCP panel"    -ForegroundColor Gray
Write-Host "    3. Click 'Revit MCP Switch' to start the local server"          -ForegroundColor Gray
Write-Host "    4. Open Claude Desktop / Claude Code and start chatting"        -ForegroundColor Gray
Write-Host ""
Write-Host "  Docs:   https://github.com/$REPO#readme"   -ForegroundColor DarkGray
Write-Host "  Issues: https://github.com/$REPO/issues"   -ForegroundColor DarkGray
Write-Host ""
