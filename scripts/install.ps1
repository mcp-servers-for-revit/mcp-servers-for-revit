<#
.SYNOPSIS
    Installs the mcp-servers-for-revit plugin for Autodesk Revit.

.DESCRIPTION
    Works on a clean machine with only Revit and PowerShell installed.
    - Forces TLS 1.2 (required for GitHub API on older Windows)
    - Checks for Node.js and offers to install it (required for the MCP server)
    - Detects installed Revit versions (2023-2026)
    - Downloads the latest (or specified) pre-built Release from GitHub
    - Extracts to the correct Addins folder
    - Unblocks all files so Windows does not prevent loading
    - Verifies all required files and dependencies are present
    - Optionally configures Claude Desktop MCP server

.PARAMETER RevitVersion
    Target Revit version (2023, 2024, 2025, 2026). If omitted, auto-detects.

.PARAMETER Tag
    GitHub release tag (e.g. "v1.2.0"). Defaults to "latest".

.PARAMETER Uninstall
    Remove the plugin instead of installing it.

.PARAMETER SkipNodeCheck
    Skip the Node.js prerequisite check.

.PARAMETER SkipMcpConfig
    Skip Claude Desktop MCP server configuration.

.EXAMPLE
    .\install.ps1
    # Auto-detect Revit, install latest release

.EXAMPLE
    .\install.ps1 -RevitVersion 2025
    # Install for Revit 2025 specifically

.EXAMPLE
    .\install.ps1 -Uninstall
    # Remove the plugin from all detected Revit versions

.EXAMPLE
    powershell -ExecutionPolicy Bypass -Command "irm https://raw.githubusercontent.com/LuDattilo/revit-mcp-server/main/scripts/install.ps1 | iex"
    # One-liner install from GitHub
#>
param(
    [ValidateSet('2023','2024','2025','2026')]
    [string]$RevitVersion,

    [string]$Tag = 'latest',

    [switch]$Uninstall,

    [switch]$SkipNodeCheck,

    [switch]$SkipMcpConfig
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'  # speeds up Invoke-WebRequest significantly

# Force TLS 1.2 — required for GitHub on Windows 10 builds without modern defaults
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$repo = 'LuDattilo/revit-mcp-server'
$pluginName = 'mcp-servers-for-revit'
$addinFile = "$pluginName.addin"
$pluginFolder = 'revit_mcp_plugin'

# ─── Output helpers ───────────────────────────────────────────────────────────
function Write-Step { param([string]$msg) Write-Host "  [*] $msg" -ForegroundColor Cyan }
function Write-Ok   { param([string]$msg) Write-Host "  [+] $msg" -ForegroundColor Green }
function Write-Warn { param([string]$msg) Write-Host "  [!] $msg" -ForegroundColor Yellow }
function Write-Err  { param([string]$msg) Write-Host "  [-] $msg" -ForegroundColor Red }

# ─── Check prerequisites ─────────────────────────────────────────────────────
function Test-Prerequisites {
    # PowerShell version (need 5.1+ for Expand-Archive)
    if ($PSVersionTable.PSVersion.Major -lt 5) {
        Write-Err "PowerShell 5.1 or later is required. Current: $($PSVersionTable.PSVersion)"
        Write-Err "Update Windows Management Framework: https://aka.ms/wmf5download"
        exit 1
    }
    Write-Ok "PowerShell $($PSVersionTable.PSVersion)"

    # Internet connectivity
    try {
        $null = Invoke-WebRequest -Uri 'https://api.github.com' -Method Head -TimeoutSec 10 -Headers @{ 'User-Agent' = 'mcp-revit-installer' }
        Write-Ok "Internet connection"
    } catch {
        Write-Err "Cannot reach GitHub. Check your internet connection or proxy settings."
        Write-Err "Error: $_"
        exit 1
    }

    # Node.js (required for the MCP server, not for the Revit plugin itself)
    if (-not $SkipNodeCheck) {
        Test-NodeJs
    }
}

function Test-NodeJs {
    $nodeCmd = Get-Command node -ErrorAction SilentlyContinue
    if ($nodeCmd) {
        $nodeVer = & node --version 2>$null
        # Check minimum version (18+)
        if ($nodeVer -match 'v(\d+)') {
            $major = [int]$Matches[1]
            if ($major -ge 18) {
                Write-Ok "Node.js $nodeVer"
                return
            } else {
                Write-Warn "Node.js $nodeVer found but v18+ is required"
            }
        }
    } else {
        Write-Warn "Node.js not found"
    }

    Write-Host ""
    Write-Host "  Node.js 18+ is required for the MCP server (the bridge between" -ForegroundColor Yellow
    Write-Host "  Claude Desktop/Claude Code and the Revit plugin)." -ForegroundColor Yellow
    Write-Host ""
    Write-Host "  The Revit plugin itself will be installed regardless." -ForegroundColor Gray
    Write-Host ""
    Write-Host "  Options:" -ForegroundColor White
    Write-Host "    [1] Download and install Node.js now (opens installer)" -ForegroundColor White
    Write-Host "    [2] Skip - I will install Node.js later" -ForegroundColor White
    Write-Host "    [3] Skip - I only need the Revit plugin (no MCP server)" -ForegroundColor White
    Write-Host ""

    $choice = Read-Host "  Choose [1/2/3]"

    if ($choice -eq '1') {
        Install-NodeJs
    } else {
        if ($choice -eq '3') {
            $script:SkipMcpConfig = $true
        }
        Write-Warn "Skipping Node.js. Install it later from https://nodejs.org"
    }
}

function Install-NodeJs {
    Write-Step "Downloading Node.js installer..."

    # Detect architecture
    $arch = if ([Environment]::Is64BitOperatingSystem) { 'x64' } else { 'x86' }
    # Use the latest LTS
    $nodeUrl = "https://nodejs.org/dist/v22.15.0/node-v22.15.0-$arch.msi"
    $installerPath = Join-Path $env:TEMP "node-installer.msi"

    try {
        Invoke-WebRequest -Uri $nodeUrl -OutFile $installerPath
        Write-Ok "Downloaded Node.js installer"

        Write-Step "Launching Node.js installer..."
        Write-Host "  Follow the installer wizard. After it finishes, press Enter here." -ForegroundColor Yellow
        Start-Process msiexec.exe -ArgumentList "/i `"$installerPath`"" -Wait

        # Refresh PATH so node is available in this session
        $env:Path = [Environment]::GetEnvironmentVariable('Path', 'Machine') + ';' + [Environment]::GetEnvironmentVariable('Path', 'User')

        # Verify
        $nodeCmd = Get-Command node -ErrorAction SilentlyContinue
        if ($nodeCmd) {
            $nodeVer = & node --version 2>$null
            Write-Ok "Node.js $nodeVer installed successfully"
        } else {
            Write-Warn "Node.js installed but not yet in PATH. Restart PowerShell after installation."
        }
    } catch {
        Write-Err "Failed to download Node.js: $_"
        Write-Warn "Install manually from https://nodejs.org"
    } finally {
        Remove-Item $installerPath -Force -ErrorAction SilentlyContinue
    }
}

# ─── Detect installed Revit versions ──────────────────────────────────────────
function Get-InstalledRevitVersions {
    $versions = @()
    $addinsRoot = "$env:APPDATA\Autodesk\Revit\Addins"

    foreach ($year in 2023..2026) {
        $addinsDir = "$addinsRoot\$year"
        if (Test-Path $addinsDir) {
            $versions += $year.ToString()
        }
    }

    # Also check registry
    foreach ($year in 2023..2026) {
        $regPath = "HKLM:\SOFTWARE\Autodesk\Revit\Autodesk Revit $year"
        if ((Test-Path $regPath) -and ($versions -notcontains $year.ToString())) {
            $versions += $year.ToString()
        }
    }

    return $versions | Sort-Object -Unique
}

# ─── Get Addins path ─────────────────────────────────────────────────────────
function Get-AddinsPath {
    param([string]$Year)
    return "$env:APPDATA\Autodesk\Revit\Addins\$Year"
}

# ─── Uninstall ────────────────────────────────────────────────────────────────
function Uninstall-Plugin {
    param([string[]]$Versions)

    Write-Host ""
    Write-Host "  Uninstalling $pluginName..." -ForegroundColor Magenta
    Write-Host ""

    foreach ($ver in $Versions) {
        $addinsPath = Get-AddinsPath $ver
        $addinFilePath = "$addinsPath\$addinFile"
        $pluginFolderPath = "$addinsPath\$pluginFolder"
        $removed = $false

        if (Test-Path $addinFilePath) {
            Remove-Item $addinFilePath -Force
            $removed = $true
        }
        if (Test-Path $pluginFolderPath) {
            Remove-Item $pluginFolderPath -Recurse -Force
            $removed = $true
        }

        if ($removed) {
            Write-Ok "Revit $ver - removed"
        } else {
            Write-Warn "Revit $ver - nothing to remove"
        }
    }

    Write-Host ""
    Write-Ok "Uninstall complete. Restart Revit if it is running."
    Write-Host ""
}

# ─── Resolve release tag ─────────────────────────────────────────────────────
function Get-ReleaseInfo {
    param([string]$TagName)

    if ($TagName -eq 'latest') {
        $url = "https://api.github.com/repos/$repo/releases/latest"
    } else {
        $url = "https://api.github.com/repos/$repo/releases/tags/$TagName"
    }

    try {
        $release = Invoke-RestMethod -Uri $url -Headers @{ 'User-Agent' = 'mcp-revit-installer' }
        return $release
    } catch {
        Write-Err "Could not fetch release info from GitHub."
        Write-Err "URL: $url"
        if ($_.Exception.Response.StatusCode -eq 404) {
            Write-Err "Release not found. Check the tag name or visit:"
            Write-Err "https://github.com/$repo/releases"
        } else {
            Write-Err "Error: $_"
        }
        exit 1
    }
}

# ─── Download and extract ────────────────────────────────────────────────────
function Install-ForVersion {
    param(
        [string]$Year,
        [object]$Release
    )

    $addinsPath = Get-AddinsPath $Year
    $tag = $Release.tag_name

    # Find the correct asset
    $assetName = "$pluginName-$tag-Revit$Year.zip"
    $asset = $Release.assets | Where-Object { $_.name -eq $assetName }

    if (-not $asset) {
        Write-Warn "Revit $Year - no asset '$assetName' in release $tag, skipping"
        $available = ($Release.assets | ForEach-Object { $_.name }) -join ', '
        if ($available) {
            Write-Warn "  Available assets: $available"
        }
        return $false
    }

    # Check if Revit is currently running (DLLs would be locked)
    $revitProcess = Get-Process -Name 'Revit' -ErrorAction SilentlyContinue
    if ($revitProcess) {
        Write-Warn "Revit is currently running. Files may be locked."
        Write-Warn "Close Revit first for a clean installation, or press Enter to try anyway."
        Read-Host "  Press Enter to continue"
    }

    Write-Step "Revit $Year - downloading $assetName ($([math]::Round($asset.size / 1MB, 1)) MB)..."

    # Create temp directory
    $tempDir = Join-Path $env:TEMP "mcp-revit-install-$Year"
    $tempZip = "$tempDir\$assetName"

    if (Test-Path $tempDir) { Remove-Item $tempDir -Recurse -Force }
    New-Item -ItemType Directory -Path $tempDir -Force | Out-Null

    # Download
    try {
        Invoke-WebRequest -Uri $asset.browser_download_url -OutFile $tempZip -Headers @{ 'User-Agent' = 'mcp-revit-installer' }
    } catch {
        Write-Err "Download failed: $_"
        return $false
    }

    # Verify download size
    $downloadedSize = (Get-Item $tempZip).Length
    if ($downloadedSize -ne $asset.size) {
        Write-Err "Downloaded file size ($downloadedSize bytes) does not match expected ($($asset.size) bytes)"
        Write-Err "The download may be corrupted. Try again."
        Remove-Item $tempDir -Recurse -Force -ErrorAction SilentlyContinue
        return $false
    }
    Write-Ok "Revit $Year - download verified ($downloadedSize bytes)"

    # Remove old installation
    $oldAddin = "$addinsPath\$addinFile"
    $oldPlugin = "$addinsPath\$pluginFolder"
    if (Test-Path $oldAddin) { Remove-Item $oldAddin -Force }
    if (Test-Path $oldPlugin) { Remove-Item $oldPlugin -Recurse -Force }

    # Ensure Addins directory exists
    if (-not (Test-Path $addinsPath)) {
        New-Item -ItemType Directory -Path $addinsPath -Force | Out-Null
    }

    # Extract
    Write-Step "Revit $Year - extracting to $addinsPath..."
    try {
        Expand-Archive -Path $tempZip -DestinationPath $addinsPath -Force
    } catch {
        Write-Err "Extraction failed: $_"
        Write-Err "The ZIP file may be corrupted. Try downloading again."
        Remove-Item $tempDir -Recurse -Force -ErrorAction SilentlyContinue
        return $false
    }

    # Unblock ALL downloaded files (Windows Zone.Identifier blocks execution)
    Write-Step "Revit $Year - unblocking files..."
    Get-ChildItem -Path $addinsPath -Recurse -File | ForEach-Object {
        Unblock-File -Path $_.FullName -ErrorAction SilentlyContinue
    }

    # Cleanup temp
    Remove-Item $tempDir -Recurse -Force -ErrorAction SilentlyContinue

    # ── Verify installation ──
    $ok = Test-Installation -AddinsPath $addinsPath -Year $Year -Tag $tag
    return $ok
}

# ─── Verify all required files ────────────────────────────────────────────────
function Test-Installation {
    param(
        [string]$AddinsPath,
        [string]$Year,
        [string]$Tag
    )

    Write-Step "Revit $Year - verifying installation..."

    $pluginRoot = "$AddinsPath\$pluginFolder"
    $commandSetDir = "$pluginRoot\Commands\RevitMCPCommandSet\$Year"

    # Critical files that must exist
    $requiredFiles = @(
        @{ Path = "$AddinsPath\$addinFile";                    Label = "Add-in manifest (.addin)" },
        @{ Path = "$pluginRoot\RevitMCPPlugin.dll";            Label = "Main plugin DLL" },
        @{ Path = "$pluginRoot\RevitMCPSDK.dll";               Label = "RevitMCP SDK" },
        @{ Path = "$pluginRoot\Newtonsoft.Json.dll";           Label = "Newtonsoft.Json" },
        @{ Path = "$pluginRoot\tool_schemas.json";             Label = "Tool schemas" },
        @{ Path = "$pluginRoot\Commands\commandRegistry.json"; Label = "Command registry" },
        @{ Path = "$commandSetDir\RevitMCPCommandSet.dll";     Label = "Command set DLL" }
    )

    $allOk = $true
    $missing = @()

    foreach ($file in $requiredFiles) {
        if (Test-Path $file.Path) {
            Write-Ok "  $($file.Label)"
        } else {
            Write-Err "  MISSING: $($file.Label)"
            Write-Err "    Expected at: $($file.Path)"
            $missing += $file.Label
            $allOk = $false
        }
    }

    # Check that we have DLLs, not source code
    $csFiles = Get-ChildItem -Path $pluginRoot -Filter '*.cs' -Recurse -ErrorAction SilentlyContinue
    if ($csFiles -and $csFiles.Count -gt 0) {
        Write-Err "  Found .cs source files in the plugin folder!"
        Write-Err "  This means source code was copied instead of compiled binaries."
        Write-Err "  Download the pre-built ZIP from: https://github.com/$repo/releases"
        $allOk = $false
    }

    $csprojFiles = Get-ChildItem -Path $pluginRoot -Filter '*.csproj' -Recurse -ErrorAction SilentlyContinue
    if ($csprojFiles -and $csprojFiles.Count -gt 0) {
        Write-Err "  Found .csproj project files in the plugin folder!"
        Write-Err "  This means source code was copied instead of compiled binaries."
        Write-Err "  Download the pre-built ZIP from: https://github.com/$repo/releases"
        $allOk = $false
    }

    # Count DLLs
    $dllCount = (Get-ChildItem -Path $pluginRoot -Filter '*.dll' -Recurse -ErrorAction SilentlyContinue).Count
    if ($dllCount -eq 0) {
        Write-Err "  No DLL files found at all! Installation is empty or corrupted."
        $allOk = $false
    } else {
        Write-Ok "  $dllCount DLL files found"
    }

    # Verify .addin manifest points to the right assembly
    if (Test-Path "$AddinsPath\$addinFile") {
        try {
            [xml]$addinXml = Get-Content "$AddinsPath\$addinFile" -Raw
            $assemblyPath = $addinXml.RevitAddIns.AddIn.Assembly
            $fullAssemblyPath = Join-Path $AddinsPath $assemblyPath
            if (Test-Path $fullAssemblyPath) {
                Write-Ok "  Manifest assembly path verified"
            } else {
                Write-Err "  Manifest points to '$assemblyPath' but file not found at:"
                Write-Err "    $fullAssemblyPath"
                $allOk = $false
            }
        } catch {
            Write-Warn "  Could not parse .addin manifest: $_"
        }
    }

    Write-Host ""
    if ($allOk) {
        Write-Ok "Revit $Year - installed and verified ($Tag)"
        return $true
    } else {
        Write-Err "Revit $Year - installation has problems (see above)"
        Write-Err "Try again or download the ZIP manually from:"
        Write-Err "  https://github.com/$repo/releases/tag/$Tag"
        return $false
    }
}

# ─── Configure Claude Desktop ────────────────────────────────────────────────
function Set-ClaudeDesktopConfig {
    $configPath = "$env:APPDATA\Claude\claude_desktop_config.json"
    $configDir = Split-Path $configPath

    # Check if Claude Desktop is installed
    if (-not (Test-Path $configDir)) {
        Write-Warn "Claude Desktop not found, skipping MCP configuration"
        Write-Host "  Install Claude Desktop from https://claude.ai/download" -ForegroundColor Gray
        return
    }

    # Read or create config
    if (Test-Path $configPath) {
        try {
            $config = Get-Content $configPath -Raw | ConvertFrom-Json
        } catch {
            Write-Warn "Could not parse existing claude_desktop_config.json, creating backup"
            Copy-Item $configPath "$configPath.bak" -Force
            $config = [PSCustomObject]@{}
        }
    } else {
        $config = [PSCustomObject]@{}
    }

    # Check if already configured
    if ($config.mcpServers -and $config.mcpServers.'revit-mcp') {
        Write-Ok "Claude Desktop - revit-mcp already configured"
        return
    }

    # Check that Node.js is available (required for npx)
    $nodeCmd = Get-Command node -ErrorAction SilentlyContinue
    if (-not $nodeCmd) {
        Write-Warn "Claude Desktop - skipping MCP config (Node.js not installed yet)"
        Write-Warn "After installing Node.js, run this script again or configure manually"
        return
    }

    # Add mcpServers if missing
    if (-not $config.mcpServers) {
        $config | Add-Member -NotePropertyName 'mcpServers' -NotePropertyValue ([PSCustomObject]@{})
    }

    # Add revit-mcp server
    $config.mcpServers | Add-Member -NotePropertyName 'revit-mcp' -NotePropertyValue ([PSCustomObject]@{
        command = 'cmd'
        args = @('/c', 'npx', '-y', 'mcp-server-for-revit')
    })

    $config | ConvertTo-Json -Depth 10 | Set-Content $configPath -Encoding UTF8
    Write-Ok "Claude Desktop - revit-mcp server configured"
}

# ═══════════════════════════════════════════════════════════════════════════════
# Main
# ═══════════════════════════════════════════════════════════════════════════════

Write-Host ""
Write-Host "  ================================================================" -ForegroundColor White
Write-Host "      mcp-servers-for-revit installer" -ForegroundColor White
Write-Host "  ================================================================" -ForegroundColor White
Write-Host ""

# ── Detect Revit versions ──
if ($RevitVersion) {
    $targetVersions = @($RevitVersion)
} else {
    $targetVersions = Get-InstalledRevitVersions
    if ($targetVersions.Count -eq 0) {
        Write-Err "No Revit installations detected (2023-2026)."
        Write-Err "Use -RevitVersion to specify manually:"
        Write-Err "  .\install.ps1 -RevitVersion 2025"
        exit 1
    }
    Write-Ok "Detected Revit: $($targetVersions -join ', ')"
}

# ── Uninstall mode (no prerequisites needed) ──
if ($Uninstall) {
    Uninstall-Plugin $targetVersions
    exit 0
}

# ── Prerequisites (only for install) ──
Write-Step "Checking prerequisites..."
Test-Prerequisites
Write-Host ""

# ── Fetch release info ──
Write-Step "Fetching release info ($Tag)..."
$release = Get-ReleaseInfo $Tag
$relDate = $release.published_at.Substring(0, 10)
Write-Ok "Release: $($release.tag_name) ($relDate)"
Write-Host ""

# ── Install ──
$installed = 0
foreach ($ver in $targetVersions) {
    if (Install-ForVersion -Year $ver -Release $release) {
        $installed++
    }
    Write-Host ""
}

if ($installed -eq 0) {
    Write-Err "No versions were installed successfully. See errors above."
    exit 1
}

# ── Configure Claude Desktop ──
if (-not $SkipMcpConfig) {
    Set-ClaudeDesktopConfig
}

# ── Summary ──
Write-Host ""
Write-Host "  ================================================================" -ForegroundColor Green
Write-Host "      Installation complete!" -ForegroundColor Green
Write-Host "  ================================================================" -ForegroundColor Green
Write-Host ""
Write-Host "  Next steps:" -ForegroundColor White
Write-Host "    1. Open (or restart) Revit" -ForegroundColor Gray
Write-Host "    2. Go to Add-Ins tab" -ForegroundColor Gray
Write-Host "    3. You should see 3 buttons: MCP Switch, MCP Panel, Settings" -ForegroundColor Gray
Write-Host "    4. Click 'Revit MCP Switch' to start the server" -ForegroundColor Gray
Write-Host "    5. Open Claude Desktop (or Claude Code) and start chatting" -ForegroundColor Gray
Write-Host ""
Write-Host "  Docs: https://github.com/$repo#readme" -ForegroundColor DarkGray
Write-Host ""
