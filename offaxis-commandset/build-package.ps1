#Requires -Version 5.1
<#
.SYNOPSIS
    Builds the OffAxisCommandSet project and generates the standalone distribution ZIP.
#>
param(
    [string]$Configuration = "Release R24",
    [int]$RevitVersion = 2024,
    [string]$OutputDir = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$PSScriptDir = $PSScriptRoot
$ProjectCsproj = Join-Path $PSScriptDir "OffAxisCommandSet.csproj"
if (-not $OutputDir) { $OutputDir = Join-Path $PSScriptDir "dist" }

Write-Host "=== Building OffAxisCommandSet ($Configuration) ===" -ForegroundColor Cyan
dotnet build $ProjectCsproj -c $Configuration --verbosity minimal
if ($LASTEXITCODE -ne 0) { throw "dotnet build failed" }

$binDir = Join-Path $PSScriptDir "bin\$Configuration"
if (-not (Test-Path $binDir)) {
    # Check alternate output path
    $binDir = Join-Path $PSScriptDir "bin\Release\$RevitVersion"
}
if (-not (Test-Path $binDir)) {
    throw "Build output directory not found: $binDir"
}

# Setup packaging staging directory
$stagingDir = Join-Path $OutputDir "staging"
if (Test-Path $stagingDir) { Remove-Item $stagingDir -Recurse -Force }
New-Item -ItemType Directory -Path $stagingDir -Force | Out-Null

$setStagingDir = Join-Path $stagingDir "OffAxisCommandSet"
$versionStagingDir = Join-Path $setStagingDir "$RevitVersion"
New-Item -ItemType Directory -Path $versionStagingDir -Force | Out-Null

# Copy binaries
Copy-Item -Path "$binDir\*.dll" -Destination $versionStagingDir -Force
Copy-Item -Path "$binDir\*.pdb" -Destination $versionStagingDir -Force -ErrorAction SilentlyContinue

# Copy command.json
Copy-Item -Path (Join-Path $PSScriptDir "command.json") -Destination $setStagingDir -Force

# Copy package scripts and README
$packageScriptsDir = Join-Path $PSScriptDir "package"
Copy-Item -Path "$packageScriptsDir\install.ps1" -Destination $stagingDir -Force
Copy-Item -Path "$packageScriptsDir\uninstall.ps1" -Destination $stagingDir -Force
Copy-Item -Path "$packageScriptsDir\README.md" -Destination $stagingDir -Force

# Create ZIP archive
$zipPath = Join-Path $OutputDir "OffAxisCommandSet.zip"
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }

Write-Host "`n=== Creating Distribution ZIP: $zipPath ===" -ForegroundColor Cyan
Compress-Archive -Path "$stagingDir\*" -DestinationPath $zipPath -Force

Remove-Item $stagingDir -Recurse -Force

$zipFile = Get-Item $zipPath
Write-Host "Package generated successfully!" -ForegroundColor Green
Write-Host "Path: $($zipFile.FullName)" -ForegroundColor Green
Write-Host "Size: $([math]::Round($zipFile.Length / 1KB, 2)) KB" -ForegroundColor Green
