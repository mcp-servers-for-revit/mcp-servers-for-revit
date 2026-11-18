$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'opencode-config.ps1')
$Quiet = $true

$failed = 0
function Assert {
    param([bool]$Condition, [string]$Name)
    if ($Condition) { Write-Output "PASS: $Name" }
    else { Write-Output ("FAIL: " + $Name); $script:failed++ }
}

function New-TestDir {
    $d = Join-Path $env:TEMP ('opencode-config-test-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $d -Force | Out-Null
    return $d
}

function Set-TestFile {
    param([string]$Dir, [string]$Name, [string]$Content)
    $p = Join-Path $Dir $Name
    $utf8 = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($p, $Content, $utf8)
    return $p
}

function Get-TestFile {
    param([string]$Dir, [string]$Name)
    $p = Join-Path $Dir $Name
    if (-not (Test-Path -LiteralPath $p)) { return $null }
    return [System.IO.File]::ReadAllText($p)
}

$shim = 'C:\Program Files (x86)\MCP Servers for Revit\Server\run-mcp-server.cmd'

$d = New-TestDir
try {
    Invoke-Install -Dir $d -Shim $shim -IsNative $true
    $text = Get-TestFile $d 'opencode.jsonc'
    Assert ($null -ne $text) 'native empty install creates opencode.jsonc'
    Assert (Test-JsoncParse $text) 'native empty install is valid JSONC'
    $j = ConvertFrom-Json (ConvertTo-ValidationJson $text)
    Assert ($null -ne $j.mcp.servers.'mcp-server-for-revit') 'native empty install has mcp.servers entry'
    Assert ($j.mcp.servers.'mcp-server-for-revit'.codemode -eq $false) 'native empty install sets codemode false'
    Assert ($null -eq $j.mcp.servers.'mcp-server-for-revit'.enabled) 'native empty install omits enabled'
    $before = $text
    Invoke-Install -Dir $d -Shim $shim -IsNative $true
    Assert ((Get-TestFile $d 'opencode.jsonc') -eq $before) 'native install is idempotent'
} finally { Remove-Item -Recurse -Force $d -ErrorAction SilentlyContinue }

$d = New-TestDir
try {
    Invoke-Install -Dir $d -Shim $shim -IsNative $false
    $text = Get-TestFile $d 'opencode.json'
    Assert ($null -ne $text) 'legacy empty install creates opencode.json'
    Assert (Test-JsoncParse $text) 'legacy empty install is valid JSONC'
    $j = ConvertFrom-Json (ConvertTo-ValidationJson $text)
    Assert ($null -ne $j.mcp.'mcp-server-for-revit') 'legacy empty install has direct mcp entry'
    Assert ($j.mcp.'mcp-server-for-revit'.enabled -eq $true) 'legacy empty install sets enabled true'
    Assert ($null -eq $j.mcp.servers) 'legacy empty install omits mcp.servers'
} finally { Remove-Item -Recurse -Force $d -ErrorAction SilentlyContinue }

$legacy = @'
{
  // keep this comment
  "mcp": {
    "playwright": {
      "type": "local",
      "command": ["npx", "-y", "@playwright/mcp"],
      "enabled": true
    },
    "mcp-server-for-revit": {
      "type": "local",
      "command": ["npx", "-y", "mcp-server-for-revit"],
      "enabled": false
    }
  }
}
'@

$d = New-TestDir
try {
    $p = Set-TestFile $d 'opencode.json' $legacy
    Invoke-Install -Dir $d -Shim $shim -IsNative $false
    $text = Get-TestFile $d 'opencode.json'
    Assert (Test-JsoncParse $text) 'legacy update is valid JSONC'
    Assert ($text.Contains('// keep this comment')) 'legacy update preserves comments'
    Assert ($text.Contains('@playwright/mcp')) 'legacy update preserves other servers'
    Assert ($text.Contains('run-mcp-server.cmd')) 'legacy update writes shim command'
    Assert ($text.Contains('"enabled": false')) 'legacy update preserves existing disabled state'
    Assert ($text.Contains('"enabled": true')) 'legacy update sets enabled true for new install'
    $before = $text
    Invoke-Install -Dir $d -Shim $shim -IsNative $false
    Assert ((Get-TestFile $d 'opencode.json') -eq $before) 'legacy update is idempotent'
} finally { Remove-Item -Recurse -Force $d -ErrorAction SilentlyContinue }

$nativeConfig = @'
{
  // native comment
  "mcp": {
    "servers": {
      "mcp-server-for-revit": {
        "type": "local",
        "command": ["old"],
        "enabled": true,
        "environment": { "A": "B" },
        "timeout": { "startup": 5 },
        "disabled": false
      },
      "other": { "type": "local", "command": ["x"] }
    }
  }
}
'@

$d = New-TestDir
try {
    Set-TestFile $d 'opencode.jsonc' $nativeConfig | Out-Null
    Invoke-Install -Dir $d -Shim $shim -IsNative $true
    $text = Get-TestFile $d 'opencode.jsonc'
    Assert (Test-JsoncParse $text) 'native update is valid JSONC'
    Assert ($text.Contains('// native comment')) 'native update preserves comments'
    Assert ($text.Contains('run-mcp-server.cmd')) 'native update writes shim command'
    Assert ($text.Contains('"A": "B"')) 'native update preserves environment'
    Assert ($text.Contains('"startup": 5')) 'native update preserves timeout'
    Assert ($text.Contains('"disabled": false')) 'native update preserves disabled'
    Assert ($text.Contains('"codemode": false')) 'native update adds codemode false'
    Assert (-not ($text -match '(?s)mcp-server-for-revit.*?"enabled"')) 'native update removes invalid enabled key'
    Assert ($text.Contains('"other"')) 'native update preserves other servers'
    $before = $text
    Invoke-Install -Dir $d -Shim $shim -IsNative $true
    Assert ((Get-TestFile $d 'opencode.jsonc') -eq $before) 'native update is idempotent'
} finally { Remove-Item -Recurse -Force $d -ErrorAction SilentlyContinue }

$d = New-TestDir
try {
    Set-TestFile $d 'opencode.json' $legacy | Out-Null
    Invoke-Install -Dir $d -Shim $shim -IsNative $true
    $text = Get-TestFile $d 'opencode.json'
    Assert (Test-JsoncParse $text) 'legacy to native migration is valid JSONC'
    Assert ($text.Contains('// keep this comment')) 'legacy to native migration preserves comments'
    Assert ($text.Contains('"servers"')) 'legacy to native migration adds mcp.servers'
    Assert ($text.Contains('"codemode": false')) 'legacy to native migration adds codemode false'
    $j = ConvertFrom-Json (ConvertTo-ValidationJson $text)
    Assert ($null -eq $j.mcp.'mcp-server-for-revit') 'legacy to native migration removes legacy server entry'
    Assert ($null -ne $j.mcp.servers.'mcp-server-for-revit') 'legacy to native migration adds native server entry'
    Assert ($text.Contains('@playwright/mcp')) 'legacy to native migration preserves other legacy servers'
} finally { Remove-Item -Recurse -Force $d -ErrorAction SilentlyContinue }

$d = New-TestDir
try {
    Set-TestFile $d 'opencode.json' $legacy | Out-Null
    Set-TestFile $d 'opencode.jsonc' '{ "$schema": "https://opencode.ai/config.json" }' | Out-Null
    Invoke-Install -Dir $d -Shim $shim -IsNative $true
    $jsonc = Get-TestFile $d 'opencode.jsonc'
    $json = Get-TestFile $d 'opencode.json'
    Assert ($jsonc.Contains('"servers"')) 'native install targets existing opencode.jsonc'
    Assert (-not ($json -match '(?s)mcp-server-for-revit')) 'native install removes duplicate from opencode.json'
    Assert ($json.Contains('@playwright/mcp')) 'native install preserves other opencode.json entries'
} finally { Remove-Item -Recurse -Force $d -ErrorAction SilentlyContinue }

$d = New-TestDir
try {
    Set-TestFile $d 'opencode.jsonc' $nativeConfig | Out-Null
    Invoke-Install -Dir $d -Shim $shim -IsNative $false
    $json = Get-TestFile $d 'opencode.json'
    $jsonc = Get-TestFile $d 'opencode.jsonc'
    Assert ($null -ne $json) 'legacy install creates opencode.json when only jsonc exists'
    Assert ($json.Contains('"mcp-server-for-revit"')) 'legacy install writes direct entry to opencode.json'
    Assert (-not ($jsonc -match 'mcp-server-for-revit')) 'legacy install removes native duplicate from jsonc'
    Assert ($jsonc.Contains('"other"')) 'legacy install preserves other native jsonc entries'
} finally { Remove-Item -Recurse -Force $d -ErrorAction SilentlyContinue }

$d = New-TestDir
try {
    Set-TestFile $d 'opencode.json' $nativeConfig | Out-Null
    Invoke-Install -Dir $d -Shim $shim -IsNative $false
    $text = Get-TestFile $d 'opencode.json'
    Assert (Test-JsoncParse $text) 'native to legacy migration is valid JSONC'
    $j = ConvertFrom-Json (ConvertTo-ValidationJson $text)
    Assert ($null -eq $j.mcp.servers.'mcp-server-for-revit') 'native to legacy migration removes native entry'
    Assert ($null -ne $j.mcp.'mcp-server-for-revit') 'native to legacy migration adds legacy entry'
    Assert ($null -ne $j.mcp.servers.other) 'native to legacy migration preserves other native servers'
} finally { Remove-Item -Recurse -Force $d -ErrorAction SilentlyContinue }

$d = New-TestDir
try {
    Set-TestFile $d 'opencode.json' $legacy | Out-Null
    Set-TestFile $d 'opencode.jsonc' $nativeConfig | Out-Null
    Invoke-Remove -Dir $d
    $json = Get-TestFile $d 'opencode.json'
    $jsonc = Get-TestFile $d 'opencode.jsonc'
    Assert (Test-JsoncParse $json) 'removal leaves valid opencode.json'
    Assert (Test-JsoncParse $jsonc) 'removal leaves valid opencode.jsonc'
    Assert (-not ($json -match '(?s)mcp-server-for-revit')) 'removal removes legacy entry from opencode.json'
    Assert (-not ($jsonc -match '(?s)mcp-server-for-revit')) 'removal removes native entry from opencode.jsonc'
    Assert ($json.Contains('@playwright/mcp')) 'removal preserves other opencode.json entries'
    Assert ($jsonc.Contains('"other"')) 'removal preserves other opencode.jsonc entries'
    Assert ($json.Contains('// keep this comment')) 'removal preserves opencode.json comments'
    Assert ($jsonc.Contains('// native comment')) 'removal preserves opencode.jsonc comments'
} finally { Remove-Item -Recurse -Force $d -ErrorAction SilentlyContinue }

$d = New-TestDir
try {
    $invalid = '{ "mcp": '
    $p = Set-TestFile $d 'opencode.json' $invalid
    $helper = Join-Path $PSScriptRoot 'opencode-config.ps1'
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $helper -Action Install -ConfigDir $d -ShimPath $shim -Native 2>&1 | Out-Null
    $rc = $LASTEXITCODE
    Assert ($rc -ne 0) 'invalid existing config exits nonzero'
    Assert ((Get-TestFile $d 'opencode.json') -eq $invalid) 'invalid existing config is left untouched'
} finally { Remove-Item -Recurse -Force $d -ErrorAction SilentlyContinue }

$d = New-TestDir
try {
    $jsonc = @'
{
  // trailing comma below
  "mcp": {
    "mcp-server-for-revit": {
      "type": "local",
      "command": ["old"],
      "enabled": true,
    },
  },
}
'@
    Set-TestFile $d 'opencode.json' $jsonc | Out-Null
    Invoke-Install -Dir $d -Shim $shim -IsNative $false
    $text = Get-TestFile $d 'opencode.json'
    Assert (Test-JsoncParse $text) 'trailing commas are accepted and output is valid'
    Assert ($text.Contains('// trailing comma below')) 'trailing-comma config preserves comments'
    Assert ($text.Contains('run-mcp-server.cmd')) 'trailing-comma config writes shim command'
} finally { Remove-Item -Recurse -Force $d -ErrorAction SilentlyContinue }

$oldLocal = $env:LOCALAPPDATA
$oldAppData = $env:APPDATA
$oldProgramFiles = $env:PROGRAMFILES
$oldProgramFilesX86 = ${env:ProgramFiles(x86)}
$oldPath = $env:PATH
$oldDetectConfig = $env:OPENCODE_CONFIG_DIR
$detectRoot = New-TestDir
try {
    $local = Join-Path $detectRoot 'local'
    $appData = Join-Path $detectRoot 'appdata'
    $programFiles = Join-Path $detectRoot 'programfiles'
    $programFilesX86 = Join-Path $detectRoot 'programfiles86'
    $bin = Join-Path $detectRoot 'bin'
    $cfg = Join-Path $detectRoot 'cfg'
    foreach ($p in @($local, $appData, $programFiles, $programFilesX86, $bin, $cfg)) {
        New-Item -ItemType Directory -Path $p -Force | Out-Null
    }
    $env:LOCALAPPDATA = $local
    $env:APPDATA = $appData
    $env:PROGRAMFILES = $programFiles
    ${env:ProgramFiles(x86)} = $programFilesX86
    $env:PATH = "$bin;$env:SystemRoot\System32"
    $env:OPENCODE_CONFIG_DIR = $cfg

    $det = Get-OpenCodeDetection
    Assert (-not $det.Installed) 'detection: no install is not detected'

    $desktopDir = Join-Path $local 'Programs\ai.opencode.desktop'
    $cliDir = Join-Path $appData 'ai.opencode.desktop\cli\2.2.0-x64'
    New-Item -ItemType Directory -Path $desktopDir -Force | Out-Null
    New-Item -ItemType Directory -Path $cliDir -Force | Out-Null
    Set-Content -LiteralPath (Join-Path $desktopDir 'OpenCode.exe') -Value ''
    Set-Content -LiteralPath (Join-Path $cliDir 'opencode-cli.exe') -Value ''

    $det = Get-OpenCodeDetection
    Assert ($det.Installed) 'detection: desktop install is detected'
    Assert ($det.Version -eq '2.2.0') 'detection: desktop version comes from bundled CLI folder'
    Assert ($det.Source -eq 'desktop') 'detection: desktop source is reported'

    Set-TestFile $cfg 'opencode.jsonc' $nativeConfig | Out-Null
    $det = Get-OpenCodeDetection
    Assert ($det.ConfigShape -eq 'native') 'detection: native config shape is reported'
    Remove-Item -LiteralPath (Join-Path $cfg 'opencode.jsonc') -Force
    Set-TestFile $cfg 'opencode.json' $legacy | Out-Null
    $det = Get-OpenCodeDetection
    Assert ($det.ConfigShape -eq 'legacy') 'detection: legacy config shape is reported'

    Remove-Item -Recurse -Force $local, $appData
    New-Item -ItemType Directory -Path $local, $appData -Force | Out-Null
    Set-Content -LiteralPath (Join-Path $bin 'opencode.cmd') -Value '@echo 1.19.3'
    $det = Get-OpenCodeDetection
    Assert ($det.Installed) 'detection: CLI install is detected'
    Assert ($det.Version -eq '1.19.3') 'detection: CLI version is reported'
    Assert ($det.Source -eq 'cli') 'detection: CLI source is reported'

    $out = Join-Path $detectRoot 'detect.txt'
    Invoke-Detect -Dir $cfg -OutPath $out
    $lines = Get-Content -LiteralPath $out
    Assert ($lines.Count -ge 6) 'detection: output file has one field per line'
    Assert ($lines -contains 'installed=1') 'detection: output file reports installed'
    Assert ($lines -contains 'version=1.19.3') 'detection: output file reports version'
    Assert ($lines -contains 'config=legacy') 'detection: output file reports config shape'
} finally {
    $env:LOCALAPPDATA = $oldLocal
    $env:APPDATA = $oldAppData
    $env:PROGRAMFILES = $oldProgramFiles
    ${env:ProgramFiles(x86)} = $oldProgramFilesX86
    $env:PATH = $oldPath
    $env:OPENCODE_CONFIG_DIR = $oldDetectConfig
    Remove-Item -Recurse -Force $detectRoot -ErrorAction SilentlyContinue
}

$oldConfigDir = $env:OPENCODE_CONFIG_DIR
$oldXdg = $env:XDG_CONFIG_HOME
try {
    $env:OPENCODE_CONFIG_DIR = 'C:\custom\opencode'
    Assert ((Resolve-ConfigDir '') -eq 'C:\custom\opencode') 'OPENCODE_CONFIG_DIR overrides config dir'
    $env:OPENCODE_CONFIG_DIR = ''
    $env:XDG_CONFIG_HOME = 'C:\xdg'
    Assert ((Resolve-ConfigDir '') -eq 'C:\xdg\opencode') 'XDG_CONFIG_HOME resolves to opencode subdir'
} finally {
    $env:OPENCODE_CONFIG_DIR = $oldConfigDir
    $env:XDG_CONFIG_HOME = $oldXdg
}

$d = New-TestDir
try {
    $otherShim = 'C:\Program Files (x86)\Other Installer\Server\run-mcp-server.cmd'
    $otherConfig = @'
{
  "mcp": {
    "mcp-server-for-revit": {
      "type": "local",
      "command": ["cmd", "/c", "C:\\Program Files (x86)\\Other Installer\\Server\\run-mcp-server.cmd"],
      "enabled": true
    }
  }
}
'@
    Set-TestFile $d 'opencode.json' $otherConfig | Out-Null
    Invoke-Remove -Dir $d -Shim $shim
    $text = Get-TestFile $d 'opencode.json'
    Assert ($text.Contains('Other Installer')) 'ownership: suite remove leaves another installer entry'
    Invoke-Remove -Dir $d -Shim $otherShim
    $text = Get-TestFile $d 'opencode.json'
    Assert (-not ($text -match 'mcp-server-for-revit')) 'ownership: owner remove removes its entry'

    Invoke-Install -Dir $d -Shim $shim -IsNative $true
    $verifyOwned = Join-Path $d 'verify-owned.txt'
    Invoke-Verify -Dir $d -Shim $shim -OutPath $verifyOwned
    $ownedLines = Get-Content -LiteralPath $verifyOwned
    Assert ($ownedLines -contains 'present=1') 'verify: owned entry is present'
    Assert ($ownedLines -contains 'owned=1') 'verify: owned entry reports owned'

    $verifyOther = Join-Path $d 'verify-other.txt'
    Invoke-Verify -Dir $d -Shim $otherShim -OutPath $verifyOther
    $otherLines = Get-Content -LiteralPath $verifyOther
    Assert ($otherLines -contains 'present=1') 'verify: foreign entry is present'
    Assert ($otherLines -contains 'owned=0') 'verify: foreign entry is not owned'
} finally { Remove-Item -Recurse -Force $d -ErrorAction SilentlyContinue }

Write-Output ''
if ($failed -eq 0) { Write-Output 'ALL ASSERTIONS PASSED'; exit 0 }
else { Write-Output "$failed ASSERTION(S) FAILED"; exit 1 }
