#Requires -Version 5.1
<#
.SYNOPSIS
    Installs the OffAxisCommandSet into an existing Revit MCP Plugin installation.
.DESCRIPTION
    Copies the OffAxisCommandSet folder into the Revit MCP Plugin Commands directory
    and merges the new commands into commandRegistry.json.
.PARAMETER RevitVersion
    The target Autodesk Revit version (default: 2024).
.PARAMETER AddinsPath
    Optional custom path to the Revit Addins directory.
#>
param(
    [int]$RevitVersion = 2024,
    [string]$AddinsPath = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

Write-Host "=== Installing OffAxisCommandSet for Revit $RevitVersion ===" -ForegroundColor Cyan

# 1. Resolve Revit Addins path
if (-not $AddinsPath) {
    $AddinsPath = Join-Path $env:APPDATA "Autodesk\Revit\Addins\$RevitVersion"
}

if (-not (Test-Path $AddinsPath)) {
    Write-Error "Revit Addins folder not found: $AddinsPath`nPlease verify that Autodesk Revit $RevitVersion is installed."
    exit 1
}

# 2. Check if Revit MCP Plugin is installed
$pluginDir = Join-Path $AddinsPath "revit_mcp_plugin"
$commandsDir = Join-Path $pluginDir "Commands"

if (-not (Test-Path $pluginDir)) {
    Write-Error "Revit MCP Plugin was not found in $AddinsPath.`nPlease install the main mcp-servers-for-revit plugin first before installing this command set."
    exit 1
}

if (-not (Test-Path $commandsDir)) {
    New-Item -ItemType Directory -Path $commandsDir -Force | Out-Null
}

# 3. Copy OffAxisCommandSet files
$sourceSetDir = Join-Path $PSScriptRoot "OffAxisCommandSet"
if (-not (Test-Path (Join-Path $sourceSetDir "command.json"))) {
    # Check parent directory (e.g. running from offaxis-commandset\package\)
    $parentDir = Split-Path $PSScriptRoot -Parent
    if (Test-Path (Join-Path $parentDir "command.json")) {
        # Running in dev source repo - check if bin\Release R24 exists
        $binDir = Join-Path $parentDir "bin\Release R24"
        if (Test-Path $binDir) {
            $tempStage = Join-Path $env:TEMP "OffAxisCommandSet_Stage"
            if (Test-Path $tempStage) { Remove-Item $tempStage -Recurse -Force }
            New-Item -ItemType Directory -Path (Join-Path $tempStage "$RevitVersion") -Force | Out-Null
            Copy-Item "$binDir\*.dll" (Join-Path $tempStage "$RevitVersion") -Force
            Copy-Item "$binDir\*.pdb" (Join-Path $tempStage "$RevitVersion") -Force -ErrorAction SilentlyContinue
            Copy-Item (Join-Path $parentDir "command.json") $tempStage -Force
            $sourceSetDir = $tempStage
        }
    }
}

$sourceCommandJson = Join-Path $sourceSetDir "command.json"
if (-not (Test-Path $sourceCommandJson)) {
    Write-Error "command.json not found in $sourceSetDir. Invalid package structure."
    exit 1
}

$destSetDir = Join-Path $commandsDir "OffAxisCommandSet"
Write-Host "Copying OffAxisCommandSet to $destSetDir..." -ForegroundColor Yellow

if (Test-Path $destSetDir) {
    Remove-Item $destSetDir -Recurse -Force
}
Copy-Item -Path $sourceSetDir -Destination $destSetDir -Recurse -Force

Write-Host "Files copied successfully." -ForegroundColor Green

# 4. Merge commands into commandRegistry.json if it exists
$registryPath = Join-Path $commandsDir "commandRegistry.json"

if (Test-Path $registryPath) {
    Write-Host "Updating command registry at $registryPath..." -ForegroundColor Yellow
    try {
        $registryContent = Get-Content $registryPath -Raw -Encoding UTF8
        $registry = $registryContent | ConvertFrom-Json
        
        $commandJsonContent = Get-Content $sourceCommandJson -Raw -Encoding UTF8
        $commandSetData = $commandJsonContent | ConvertFrom-Json

        $existingNames = @{}
        if ($registry.PSObject.Properties['commands'] -and $registry.commands) {
            foreach ($cmd in $registry.commands) {
                if ($cmd.commandName) {
                    $existingNames[$cmd.commandName] = $cmd
                }
            }
        } else {
            $registry | Add-Member -NotePropertyName "commands" -NotePropertyValue @() -Force
        }

        $commandsList = [System.Collections.Generic.List[PSObject]]::new()
        if ($registry.commands) {
            foreach ($cmd in $registry.commands) {
                $commandsList.Add($cmd)
            }
        }

        $addedCount = 0
        $updatedCount = 0

        foreach ($newCmd in $commandSetData.commands) {
            $cmdName = $newCmd.commandName
            $asmPath = "OffAxisCommandSet\{VERSION}\" + [System.IO.Path]::GetFileName($newCmd.assemblyPath)

            if ($existingNames.ContainsKey($cmdName)) {
                $existing = $existingNames[$cmdName]
                $existing.assemblyPath = $asmPath
                $existing.enabled = $true
                $existing.description = $newCmd.description
                if (-not $existing.supportedRevitVersions -or $existing.supportedRevitVersions.Count -eq 0) {
                    $existing.supportedRevitVersions = @([string]$RevitVersion)
                } elseif (-not ($existing.supportedRevitVersions -contains [string]$RevitVersion)) {
                    $existing.supportedRevitVersions += [string]$RevitVersion
                }
                $updatedCount++
            } else {
                $entry = [PSCustomObject]@{
                    commandName = $cmdName
                    assemblyPath = $asmPath
                    enabled = $true
                    supportedRevitVersions = @([string]$RevitVersion)
                    developer = $commandSetData.developer
                    description = $newCmd.description
                }
                $commandsList.Add($entry)
                $addedCount++
            }
        }

        $updatedRegistry = [PSCustomObject]@{
            commands = $commandsList.ToArray()
        }

        $newJson = $updatedRegistry | ConvertTo-Json -Depth 10
        [System.IO.File]::WriteAllText($registryPath, $newJson, [System.Text.Encoding]::UTF8)
        Write-Host "Registry updated: $addedCount added, $updatedCount updated." -ForegroundColor Green
    }
    catch {
        Write-Warning "Could not automatically update commandRegistry.json: $_`nYou can enable the commands via Revit MCP Plugin Settings UI (Refresh -> Save)."
    }
} else {
    Write-Host "Note: commandRegistry.json will be auto-generated by the plugin upon next Revit launch." -ForegroundColor Cyan
}

Write-Host "`n=== Installation Complete! ===" -ForegroundColor Green
Write-Host "1. Restart Revit $RevitVersion if it is currently running."
Write-Host "2. OffAxis commands are now available via the MCP server and plugin interface."
