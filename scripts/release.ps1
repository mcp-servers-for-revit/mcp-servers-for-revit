param(
    [Parameter(Mandatory)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

# --- confirm ---
Write-Host "This will discard all uncommitted changes, checkout main, and pull." -ForegroundColor Yellow
$answer = Read-Host "Continue? (y/N)"
if ($answer -ne 'y') {
    Write-Host "Aborted."
    exit 0
}

# --- reset to latest main ---
Push-Location $root
git checkout main
git reset --hard
git pull
Pop-Location

# --- server/package.json ---
$pkg = Get-Content "$root/server/package.json" -Raw | ConvertFrom-Json
$pkg.version = $Version
$pkg | ConvertTo-Json -Depth 10 | Set-Content "$root/server/package.json" -NoNewline
Write-Host "server/package.json -> $Version"

# --- server/package-lock.json ---
Push-Location "$root/server"
npm install --package-lock-only --silent 2>$null
Pop-Location
Write-Host "server/package-lock.json -> $Version"

# --- plugin/Properties/AssemblyInfo.cs ---
$assemblyInfo = "$root/plugin/Properties/AssemblyInfo.cs"
$fourPart = "$Version.0"
(Get-Content $assemblyInfo -Raw) `
    -replace 'AssemblyVersion\("[^"]+"\)',    "AssemblyVersion(`"$fourPart`")" `
    -replace 'AssemblyFileVersion\("[^"]+"\)', "AssemblyFileVersion(`"$fourPart`")" |
    Set-Content $assemblyInfo -NoNewline
Write-Host "plugin/Properties/AssemblyInfo.cs -> $fourPart"

# --- git commit + tag ---
Push-Location $root
git add server/package.json server/package-lock.json plugin/Properties/AssemblyInfo.cs
git commit -m "$Version"
git tag "v$Version"
Pop-Location

Write-Host ""
Write-Host "Done! Run 'git push origin main --tags' to trigger the release."
