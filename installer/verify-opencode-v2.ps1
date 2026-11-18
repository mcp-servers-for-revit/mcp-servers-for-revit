$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'opencode-config.ps1')
$Quiet = $true

$det = Get-OpenCodeDetection
if (-not $det.Installed) {
    Write-Output 'SKIP: no OpenCode installation detected'
    exit 0
}
if ($det.Version -and $det.Version -notmatch '^2\.') {
    Write-Output "SKIP: OpenCode $($det.Version) is not version 2"
    exit 0
}
if (-not $det.Version) {
    Write-Output 'SKIP: OpenCode detected but version is unknown'
    exit 0
}
if ($det.Source -ne 'cli') {
    Write-Output 'SKIP: OpenCode Desktop detected. Open the desktop app and verify via /mcps, or call say_hello directly.'
    exit 0
}

$root = Join-Path $env:TEMP ('opencode-v2-smoke-' + [guid]::NewGuid().ToString('N'))
$configDir = Join-Path $root 'config'
$entry = (Resolve-Path (Join-Path $PSScriptRoot '..\server\build\index.js')).Path
$shim = Join-Path $root 'run-mcp-server.cmd'
New-Item -ItemType Directory -Path $configDir -Force | Out-Null
[System.IO.File]::WriteAllText($shim, "@echo off`r`nnode `"$entry`" %*`r`n")

try {
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'opencode-config.ps1') -Action Install -ConfigDir $configDir -ShimPath $shim -Native
    if ($LASTEXITCODE -ne 0) { throw 'opencode-config.ps1 failed' }

    $job = Start-Job -ArgumentList $configDir -ScriptBlock {
        param($cfg)
        $env:OPENCODE_CONFIG_DIR = $cfg
        & opencode mcp list 2>&1 | Out-String
    }
    if (-not (Wait-Job $job -Timeout 90)) {
        Stop-Job $job
        Remove-Job $job
        throw 'opencode mcp list timed out'
    }
    $output = Receive-Job $job
    Remove-Job $job

    if ($output -notmatch 'mcp-server-for-revit') {
        Write-Output $output
        throw 'opencode mcp list did not show mcp-server-for-revit'
    }

    Write-Output 'PASS: OpenCode 2 sees mcp-server-for-revit'
} finally {
    Remove-Item -Recurse -Force $root -ErrorAction SilentlyContinue
}
