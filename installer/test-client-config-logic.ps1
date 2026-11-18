# Installer client-config logic verification harness.
# Ports the emitted Inno Pascal procedures (ReplaceNpxServerEntry,
# GetOpenCodeEntry, CleanTrailingCommas, ConfigParseOK) from
# build-installer.ps1 into PowerShell and asserts the fix does not
# reproduce the "opencode.json corrupted with duplicated key prefix"
# runtime bug, while proving the old logic does.
# Run with: powershell -ExecutionPolicy Bypass -File test-client-config-logic.ps1
# Exit 0 = all assertions passed; exit 1 = failure.

$ErrorActionPreference = 'Stop'
$failed = 0

function Assert { param($Condition, $Name)
    if ($Condition) { Write-Output "PASS: $Name" }
    else { Write-Output ("FAIL: " + $Name); $script:failed++ }
}

function Get-ShimPathJson {
    # Port of ShimPathJson(): ExpandConstant('{app}\Server\run-mcp-server.cmd')
    # with backslashes JSON-escaped. {app} mirrored from a real install.
    return 'C:\Program Files (x86)\MCP Servers for Revit\Server\run-mcp-server.cmd'.Replace('\', '\\')
}

function Get-OpenCodeEntry {
    # Port of GetOpenCodeEntry.
    $nl = [char]13 + [char]10
    return ('"mcp-server-for-revit": {' + $nl +
        '            "type": "local",' + $nl +
        '            "command": ["cmd", "/c", "' + (Get-ShimPathJson) + '"],' + $nl +
        '            "enabled": true' + $nl +
        '        }')
}

function Clear-TrailingCommas { param([string]$C)
    # Port of CleanTrailingCommas: blank out a comma whose next non-ws char
    # is a closing } or ] .
    $sb = New-Object System.Text.StringBuilder
    $len = $C.Length
    for ($i = 0; $i -lt $len; $i++) {
        $ch = $C[$i]
        if ($ch -eq ',') {
            $j = $i + 1
            while ($j -lt $len -and ($C[$j] -eq ' ' -or $C[$j] -eq "`t" -or $C[$j] -eq "`r" -or $C[$j] -eq "`n")) { $j++ }
            if ($j -lt $len -and ($C[$j] -eq '}' -or $C[$j] -eq ']')) { $ch = ' ' }
        }
        [void]$sb.Append($ch)
    }
    return $sb.ToString()
}

function Test-ConfigParseOk { param([string]$C)
    # Port of ConfigParseOK (structural sanity, not a full JSON parser):
    # object root, no duplicate-key prefix of the server key, no ',,',
    # balanced braces outside quoted values (backslash-escape aware).
    $S = $C.Trim()
    if (-not $S) { return $false }
    if ($S[0] -ne '{' -or $S[$S.Length - 1] -ne '}') { return $false }
    $bad = 'mcp-server-for-revit": "mcp-server-for-revit'
    if ($S.Contains($bad)) { return $false }
    if ($S.Contains(',,')) { return $false }
    $inQ = $false; $esc = $false; $depth = 0
    for ($i = 0; $i -lt $S.Length; $i++) {
        if ($esc) { $esc = $false; continue }
        if ($inQ) {
            if ($S[$i] -eq '\') { $esc = $true }
            elseif ($S[$i] -eq '"') { $inQ = $false }
            continue
        }
        if ($S[$i] -eq '"') { $inQ = $true; continue }
        if ($S[$i] -eq '{') { $depth++ }
        elseif ($S[$i] -eq '}') { $depth--; if ($depth -lt 0) { return $false } }
    }
    if ($depth -ne 0 -or $inQ) { return $false }
    return $true
}

function Update-ServerEntry { param([string]$C, [bool]$OpenCodeStyle, [bool]$Fixed)
    # Port of ReplaceNpxServerEntry. $Fixed = $true walks with S held at the
    # key start (the corrected emission); $Fixed = $false keeps the old
    # reassignment of S to the first '{'. Returns the possibly-mutated string.
    $serverKey = 'mcp-server-for-revit'
    $search = if ($Fixed) { '"' + $serverKey + '"' } else { $serverKey }
    # FIXED = corrected emission: quoted-key search keeps the opening quote
    # inside the deleted block, and S is held at the block start (the old
    # logic re-assigned it to the first '{').
    $S = $C.IndexOf($search)
    if ($S -lt 0) { return $C }
    $E = -1; $depth = 0; $blockStart = $S
    for ($i = $S; $i -lt $C.Length; $i++) {
        $ch = $C[$i]
        if ($ch -eq '{') {
            if (-not $Fixed -and $depth -eq 0) { $blockStart = $i }
            $depth++
        }
        elseif ($ch -eq '}') {
            $depth--
            if ($depth -eq 0) { $E = $i; break }
        }
    }
    if ($E -lt 0) { return $C }
    $block = $C.Substring($blockStart, $E - $blockStart + 1)
    if ($block.IndexOf('npx') -lt 0 -and $block.IndexOf('run-mcp-server') -ge 0) { return $C }
    $fresh = Get-OpenCodeEntry
    return $C.Remove($blockStart, $E - $blockStart + 1).Insert($blockStart, $fresh)
}

function Insert-ServerEntry { param([string]$C)
    # Port of the StringChangeEx-based insertion used when the key is absent
    # (opencode variant: '"mcp": {').
    $nl = [char]13 + [char]10
    $target = '"mcp": {'
    $newKey = '"mcp": {' + $nl + '        "mcp-server-for-revit": {' + $nl +
        '            "type": "local",' + $nl +
        '            "command": ["cmd", "/c", "' + (Get-ShimPathJson) + '"],' + $nl +
        '            "enabled": true' + $nl + '        },'
    return $C.Replace($target, $newKey)
}

# --- fixtures -----------------------------------------------------------

$fixtureNpx = @'
{
  "$schema": "https://opencode.ai/config.json",
  "plugin": ["opencode-dir"],
  "mcp": {
        "mcp-server-for-revit": {
            "type": "local",
            "command": ["npx", "-y", "mcp-server-for-revit"],
            "enabled": true
        },


    "playwright": {
      "type": "local",
      "command": ["npx", "-y", "@playwright/mcp"],
      "enabled": true
    }
  }
}
'@

$fixtureNoKey = @'
{
  "$schema": "https://opencode.ai/config.json",
  "mcp": {
    "playwright": {
      "type": "local",
      "command": ["npx", "-y", "@playwright/mcp"],
      "enabled": true
    }
  }
}
'@

$fixtureShim = @'
{
  "$schema": "https://opencode.ai/config.json",
  "plugin": ["opencode-dir"],
  "mcp": {
        "mcp-server-for-revit": {
'@ + '            "command": ["cmd", "/c", "' + (Get-ShimPathJson) + '"],' + @'
            "enabled": true
        },
    "playwright": {
      "type": "local",
      "command": ["npx", "-y", "@playwright/mcp"],
      "enabled": true
    }
  }
}
'@

$badFixturePath = Join-Path $PSScriptRoot '..\opencode.json.BAD'
if (Test-Path $badFixturePath) {
    $fixtureBad = Get-Content $badFixturePath -Raw
} else {
    $fixtureBad = @"
{
  "mcp": {
    "mcp-server-for-revit": "mcp-server-for-revit": {
      "type": "local"
    }
  }
}
"@
}

# --- assertions ---------------------------------------------------------

# 1. The old (buggy) walk reproduces the real-world corruption.
$buggy = Update-ServerEntry -C $fixtureNpx -OpenCodeStyle $true -Fixed $false
Assert (-not (Test-ConfigParseOk $buggy)) 'old logic on npx fixture: output FAILS sanity'
Assert ($buggy.Contains('mcp-server-for-revit": "mcp-server-for-revit')) 'old logic on npx fixture: duplicated-key signature present (proves root cause)'

# 2. The fixed walk upgrades cleanly and preserves other entries.
$fixed = Update-ServerEntry -C $fixtureNpx -OpenCodeStyle $true -Fixed $true
Assert (Test-ConfigParseOk $fixed) 'fixed logic on npx fixture: output passes structural sanity'
try { $null = $fixed | ConvertFrom-Json; Assert $true 'fixed logic on npx fixture: output parses as JSON' }
catch { Assert $false 'fixed logic on npx fixture: output parses as JSON' }
Assert ($fixed.Contains('run-mcp-server.cmd')) 'fixed logic on npx fixture: shim entry present'
# Note: bare 'npx' remains legitimately inside the playwright entry; assert
# the SERVER entry specifically was not left in npx form.
Assert ($fixed.IndexOf('"npx", "-y", "mcp-server-for-revit"') -lt 0) 'fixed logic on npx fixture: stale server npx form removed'
Assert ($fixed.Contains('@playwright/mcp')) 'fixed logic on npx fixture: playwright entry preserved'

# 3. Key-absent file: insertion path produces valid JSON.
$inserted = Clear-TrailingCommas (Insert-ServerEntry $fixtureNoKey)
Assert (Test-ConfigParseOk $inserted) 'insert path on key-less fixture: output passes structural sanity'
try { $j = $inserted | ConvertFrom-Json; Assert $true 'insert path on key-less fixture: output parses as JSON'; Assert ($null -ne $j.mcp.'mcp-server-for-revit') 'insert path: server key present'; Assert ($null -ne $j.mcp.playwright) 'insert path: playwright retained' }
catch { Assert $false 'insert path on key-less fixture: output parses as JSON' }

# 4. Already-upgraded file: no-op, byte-identical.
$noop = Update-ServerEntry -C $fixtureShim -OpenCodeStyle $true -Fixed $true
Assert ($noop -eq $fixtureShim) 'already-shim fixture: fixed logic is byte-identical (no-op)'

# 5. Sanity function flags the real-world .BAD content and accepts good content.
Assert (-not (Test-ConfigParseOk $fixtureBad)) 'sanity: real-world .BAD content rejected'
Assert (Test-ConfigParseOk $fixtureNpx) 'sanity: valid npx fixture accepted'

# 6. Guard behavior: insane content -> nothing written.
$sanityGuardTemp = Join-Path $env:TEMP ('opencode_guard_test_' + [guid]::NewGuid() + '.json')
$originalContent = $fixtureNpx
Set-Content -Path $sanityGuardTemp -Value $originalContent -NoNewline
$mutated = Update-ServerEntry -C (Get-Content $sanityGuardTemp -Raw) -OpenCodeStyle $true -Fixed $false
if (Test-ConfigParseOk (Clear-TrailingCommas $mutated)) {
    Set-Content -Path $sanityGuardTemp -Value (Clear-TrailingCommas $mutated) -NoNewline
    $saved = 'DANGEROUSLY-SAVED'
}
else { $saved = 'KEPT-ORIGINAL' }
Assert ($saved -eq 'KEPT-ORIGINAL') 'guarded save: insane mutation leaves original file untouched'
Remove-Item $sanityGuardTemp -Force -ErrorAction SilentlyContinue

# 7. Freshly-created client configs (the installer's own templates) contain
#    ordinary separators/array commas and must pass sanity. This is the
#    regression for the guard that rejected every config containing a comma.
$fixtureCreatedAnythingLLM = @'
{
    "mcpServers": {
        "mcp-server-for-revit": {
            "command": "cmd",
            "args": ["/c", "C:\\Program Files\\MCP Servers for Revit\\Server\\run-mcp-server.cmd"]
        }
    }
}
'@
Assert (Test-ConfigParseOk $fixtureCreatedAnythingLLM) 'sanity: created AnythingLLM template accepted'
try { $null = $fixtureCreatedAnythingLLM | ConvertFrom-Json; Assert $true 'sanity: created AnythingLLM template parses as JSON' }
catch { Assert $false 'sanity: created AnythingLLM template parses as JSON' }

$fixtureCreatedClaude = @'
{
    "mcpServers": {
        "mcp-server-for-revit": {
            "command": "cmd",
            "args": ["/c", "C:\\Program Files\\MCP Servers for Revit\\Server\\run-mcp-server.cmd"]
        },
        "playwright": {
            "command": "npx",
            "args": ["-y", "@playwright/mcp"]
        }
    }
}
'@
Assert (Test-ConfigParseOk $fixtureCreatedClaude) 'sanity: created multi-entry template accepted'

# 8. Doubled commas (the failure mode the guard exists to catch) still fail.
Assert (-not (Test-ConfigParseOk '{ "a": 1,, "b": 2 }')) 'sanity: doubled-comma content rejected'

# 9. Generator drift guard: decode the ConfigParseOK function actually emitted
#    by build-installer.ps1 and assert its comma check is the doubled-comma
#    form. The harness port above previously diverged from the generator,
#    which is why CI passed while the shipped installer rejected all commas.
function Get-GeneratedPascal { param([string]$FunctionName)
    $genLines = Get-Content (Join-Path $PSScriptRoot 'build-installer.ps1')
    $start = -1
    for ($i = 0; $i -lt $genLines.Count; $i++) {
        if ($genLines[$i].Contains("`$issLines += 'function $FunctionName")) { $start = $i; break }
    }
    if ($start -lt 0) { return $null }
    $prefix = "`$issLines += '"
    $sb = New-Object System.Text.StringBuilder
    for ($i = $start; $i -lt $genLines.Count; $i++) {
        $line = $genLines[$i]
        if (-not $line.StartsWith($prefix)) { break }
        $body = $line.Substring($prefix.Length)
        if ($body.EndsWith("';")) { $body = $body.Substring(0, $body.Length - 2) }
        elseif ($body.EndsWith("'")) { $body = $body.Substring(0, $body.Length - 1) }
        [void]$sb.AppendLine($body.Replace("''", "'"))
        if ($body -eq 'end;') { break }
    }
    return $sb.ToString()
}

$emittedParseOk = Get-GeneratedPascal 'ConfigParseOK'
Assert ($null -ne $emittedParseOk) 'generator: ConfigParseOK function found'
Assert ($emittedParseOk.Contains("Pos(',,', S)")) 'generator: comma check uses doubled-comma form'
Assert (-not $emittedParseOk.Contains("Pos(',', S)")) 'generator: comma check does not reject every comma'

Write-Output ''
if ($failed -eq 0) { Write-Output 'ALL ASSERTIONS PASSED'; exit 0 }
else { Write-Output "$failed ASSERTION(S) FAILED"; exit 1 }
