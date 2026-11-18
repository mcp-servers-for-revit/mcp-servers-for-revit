[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$NetworkRootPath,
    [Parameter(Mandatory)][ValidateRange(2020, 2027)][int]$Year
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot
$release = 'R' + ($Year % 100)
$commands = Join-Path $repo "plugin/bin/AddIn $Year Release $release/revit_mcp_plugin/Commands"
$server = Join-Path $repo 'server/build/index.js'
$sets = @(Get-ChildItem -LiteralPath $commands -Directory | Where-Object {
    Test-Path -LiteralPath (Join-Path $_.FullName "$Year/$($_.Name).dll")
})
if ('RevitMCPCommandSet' -notin $sets.Name -or !(Test-Path -LiteralPath $server)) {
    throw "Build the server and the Release $release solution before publishing."
}
foreach ($set in $sets) {
    $destination = Join-Path $NetworkRootPath "Commands/$($set.Name)"
    New-Item -ItemType Directory -Path $destination -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $set.FullName 'command.json') -Destination $destination -Force
    Copy-Item -LiteralPath (Join-Path $set.FullName $Year) -Destination $destination -Recurse -Force
}
$serverDirectory = Join-Path $NetworkRootPath 'Server/build'
New-Item -ItemType Directory -Path $serverDirectory -Force | Out-Null
Copy-Item -LiteralPath $server -Destination $serverDirectory -Force
