#Requires -Version 5.1
<#
.SYNOPSIS
    Uninstalls the OffAxisCommandSet from Revit MCP Plugin.
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

Write-Host "=== Uninstalling OffAxisCommandSet for Revit $RevitVersion ===" -ForegroundColor Cyan

if (-not $AddinsPath) {
    $AddinsPath = Join-Path $env:APPDATA "Autodesk\Revit\Addins\$RevitVersion"
}

$pluginDir = Join-Path $AddinsPath "revit_mcp_plugin"
$commandsDir = Join-Path $pluginDir "Commands"
$destSetDir = Join-Path $commandsDir "OffAxisCommandSet"

if (Test-Path $destSetDir) {
    Remove-Item $destSetDir -Recurse -Force
    Write-Host "Removed $destSetDir" -ForegroundColor Green
}

$registryPath = Join-Path $commandsDir "commandRegistry.json"
if (Test-Path $registryPath) {
    try {
        $registryContent = Get-Content $registryPath -Raw -Encoding UTF8
        $registry = $registryContent | ConvertFrom-Json
        
        if ($registry.PSObject.Properties['commands'] -and $registry.commands) {
            $filtered = @($registry.commands | Where-Object { 
                $_.assemblyPath -notmatch "OffAxisCommandSet" -and 
                $_.commandName -notmatch "^(detect_off_axis|fix_off_axis|detect_spacing|fix_spacing)"
            })
            
            $updatedRegistry = [PSCustomObject]@{
                commands = $filtered
            }
            $newJson = $updatedRegistry | ConvertTo-Json -Depth 10
            [System.IO.File]::WriteAllText($registryPath, $newJson, [System.Text.Encoding]::UTF8)
            Write-Host "Removed OffAxis commands from commandRegistry.json" -ForegroundColor Green
        }
    }
    catch {
        Write-Warning "Could not update commandRegistry.json: $_"
    }
}

Write-Host "`n=== Uninstallation Complete! ===" -ForegroundColor Green
