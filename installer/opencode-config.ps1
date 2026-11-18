param(
    [ValidateSet('Install', 'Remove', 'Detect', 'Verify')]
    [string]$Action = 'Install',
    [string]$ConfigDir = '',
    [string]$ShimPath = '',
    [switch]$Native,
    [string]$LogPath = '',
    [string]$OutputPath = '',
    [switch]$Quiet
)

$ErrorActionPreference = 'Stop'
$script:ServerName = 'mcp-server-for-revit'

function Write-Log {
    param([string]$Message)
    if (-not $Quiet) { Write-Output $Message }
    if ($LogPath) {
        try {
            $line = ('[{0}] {1}' -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'), $Message)
            Add-Content -LiteralPath $LogPath -Value $line -Encoding UTF8
        } catch { }
    }
}

function Skip-Trivia {
    param([string]$Text, [int]$Index)
    $n = $Text.Length
    while ($Index -lt $n) {
        $c = $Text[$Index]
        if ([char]::IsWhiteSpace($c)) { $Index++; continue }
        if ($c -eq [char]'/' -and ($Index + 1) -lt $n) {
            $d = $Text[$Index + 1]
            if ($d -eq [char]'/') {
                $Index += 2
                while ($Index -lt $n -and $Text[$Index] -ne [char]"`n" -and $Text[$Index] -ne [char]"`r") { $Index++ }
                continue
            }
            if ($d -eq [char]'*') {
                $Index += 2
                while (($Index + 1) -lt $n -and -not ($Text[$Index] -eq [char]'*' -and $Text[$Index + 1] -eq [char]'/')) { $Index++ }
                if (($Index + 1) -lt $n) { $Index += 2 }
                continue
            }
        }
        break
    }
    return $Index
}

function Skip-JsoncString {
    param([string]$Text, [int]$Index)
    $n = $Text.Length
    $Index++
    while ($Index -lt $n) {
        $c = $Text[$Index]
        if ($c -eq [char]'\') { $Index += 2; continue }
        if ($c -eq [char]'"') { return $Index + 1 }
        $Index++
    }
    throw 'Unterminated JSON string'
}

function Get-JsoncValueEnd {
    param([string]$Text, [int]$Index)
    $n = $Text.Length
    if ($Index -ge $n) { throw 'Missing JSON value' }
    $c = $Text[$Index]
    if ($c -eq [char]'"') { return (Skip-JsoncString $Text $Index) }
    if ($c -eq [char]'{' -or $c -eq [char]'[') {
        $depth = 0
        $i = $Index
        while ($i -lt $n) {
            $ch = $Text[$i]
            if ($ch -eq [char]'"') { $i = Skip-JsoncString $Text $i; continue }
            if ($ch -eq [char]'/' -and ($i + 1) -lt $n) {
                $next = $Text[$i + 1]
                if ($next -eq [char]'/' -or $next -eq [char]'*') { $i = Skip-Trivia $Text $i; continue }
            }
            if ($ch -eq [char]'{' -or $ch -eq [char]'[') { $depth++ }
            elseif ($ch -eq [char]'}' -or $ch -eq [char]']') {
                $depth--
                if ($depth -eq 0) { return $i + 1 }
            }
            $i++
        }
        throw 'Unbalanced JSON value'
    }
    $i = $Index
    while ($i -lt $n) {
        $ch = $Text[$i]
        if ([char]::IsWhiteSpace($ch) -or $ch -eq [char]',' -or $ch -eq [char]'}' -or $ch -eq [char]']') { break }
        if ($ch -eq [char]'/' -and ($i + 1) -lt $n) {
            $next = $Text[$i + 1]
            if ($next -eq [char]'/' -or $next -eq [char]'*') { break }
        }
        $i++
    }
    return $i
}

function Get-JsoncObjectProperties {
    param([string]$Text, [int]$ObjectStart)
    $n = $Text.Length
    if ($ObjectStart -ge $n -or $Text[$ObjectStart] -ne [char]'{') { throw "Expected object at offset $ObjectStart" }
    $i = $ObjectStart + 1
    $props = @()
    while ($true) {
        $i = Skip-Trivia $Text $i
        if ($i -ge $n) { throw 'Unterminated JSON object' }
        if ($Text[$i] -eq [char]'}') {
            return [pscustomobject]@{ ObjectStart = $ObjectStart; CloseIndex = $i; Properties = $props }
        }
        if ($Text[$i] -ne [char]'"') { throw "Expected property key at offset $i" }
        $keyStart = $i
        $keyEnd = Skip-JsoncString $Text $i
        $keyRaw = $Text.Substring($keyStart + 1, $keyEnd - $keyStart - 2)
        $key = ConvertFrom-Json ('"' + $keyRaw + '"')
        $i = Skip-Trivia $Text $keyEnd
        if ($i -ge $n -or $Text[$i] -ne [char]':') { throw "Expected ':' at offset $i" }
        $i++
        $i = Skip-Trivia $Text $i
        $valueStart = $i
        $valueEnd = Get-JsoncValueEnd $Text $i
        $i = Skip-Trivia $Text $valueEnd
        $commaIndex = -1
        $trailing = $false
        if ($i -lt $n -and $Text[$i] -eq [char]',') {
            $commaIndex = $i
            $i++
            $after = Skip-Trivia $Text $i
            if ($after -lt $n -and $Text[$after] -eq [char]'}') { $trailing = $true }
        }
        $props += [pscustomobject]@{
            Name = $key
            KeyStart = $keyStart
            KeyEnd = $keyEnd
            ValueStart = $valueStart
            ValueEnd = $valueEnd
            CommaIndex = $commaIndex
            IsTrailingComma = $trailing
        }
        if ($commaIndex -lt 0) {
            $i = Skip-Trivia $Text $i
            if ($i -lt $n -and $Text[$i] -eq [char]'}') {
                return [pscustomobject]@{ ObjectStart = $ObjectStart; CloseIndex = $i; Properties = $props }
            }
            throw "Expected ',' or '}' at offset $i"
        }
    }
}

function Get-JsoncProperty {
    param($Properties, [string]$Name)
    foreach ($p in $Properties) {
        if ($p.Name -eq $Name) { return $p }
    }
    return $null
}

function Get-RootProperties {
    param([string]$Text)
    $start = Skip-Trivia $Text 0
    if ($start -ge $Text.Length -or $Text[$start] -ne [char]'{') { throw 'Config root must be a JSON object' }
    return (Get-JsoncObjectProperties $Text $start)
}

function Get-ObjectIndent {
    param([string]$Text, [int]$Index)
    $lineStart = $Text.LastIndexOf([char]"`n", [Math]::Min($Index, $Text.Length - 1))
    if ($lineStart -lt 0) { $lineStart = 0 } else { $lineStart++ }
    $prefix = $Text.Substring($lineStart, $Index - $lineStart)
    $m = [regex]::Match($prefix, '^[ \t]*')
    return $m.Value
}

function Get-IndentUnit {
    param([string]$Text, $Root)
    $rootIndent = Get-ObjectIndent $Text $Root.ObjectStart
    if ($Root.Properties.Count -gt 0) {
        $firstIndent = Get-ObjectIndent $Text $Root.Properties[0].KeyStart
        if ($firstIndent.Length -gt $rootIndent.Length -and $firstIndent.StartsWith($rootIndent)) {
            $unit = $firstIndent.Substring($rootIndent.Length)
            if ($unit.Length -gt 0) { return $unit }
        }
    }
    return '  '
}

function Get-ChildIndent {
    param([string]$Text, $Object, [string]$Unit)
    if ($Object.Properties.Count -gt 0) {
        return (Get-ObjectIndent $Text $Object.Properties[0].KeyStart)
    }
    return ((Get-ObjectIndent $Text $Object.ObjectStart) + $Unit)
}

function Get-NewLine {
    param([string]$Text)
    if ($Text.Contains("`r`n")) { return "`r`n" }
    return "`n"
}

function ConvertTo-JsonString {
    param([string]$Value)
    return (ConvertTo-Json -InputObject $Value -Compress)
}

function Get-CommandJson {
    param([string]$Shim)
    return ('["cmd", "/c", ' + (ConvertTo-JsonString $Shim) + ']')
}

function New-ServerJson {
    param([string]$Indent, [string]$Unit, [string]$NewLine, [string]$Shim, [bool]$IsNative)
    $p = $Indent + $Unit
    $lines = @('{')
    $lines += ($p + '"type": "local",')
    $lines += ($p + '"command": ' + (Get-CommandJson $Shim) + ',')
    if ($IsNative) {
        $lines += ($p + '"codemode": false')
    } else {
        $lines += ($p + '"enabled": true')
    }
    $lines += ($Indent + '}')
    return ($lines -join $NewLine)
}

function New-McpJson {
    param([string]$BaseIndent, [string]$Unit, [string]$NewLine, [string]$Shim, [bool]$IsNative)
    if ($IsNative) {
        $serversIndent = $BaseIndent + $Unit
        $serverIndent = $serversIndent + $Unit
        $serverJson = New-ServerJson -Indent $serverIndent -Unit $Unit -NewLine $NewLine -Shim $Shim -IsNative $true
        $lines = @('{')
        $lines += ($serversIndent + '"servers": {')
        $lines += ($serverIndent + '"' + $script:ServerName + '": ' + $serverJson)
        $lines += ($serversIndent + '}')
        $lines += ($BaseIndent + '}')
        return ($lines -join $NewLine)
    }
    $serverIndent = $BaseIndent + $Unit
    $serverJson = New-ServerJson -Indent $serverIndent -Unit $Unit -NewLine $NewLine -Shim $Shim -IsNative $false
    $lines = @('{')
    $lines += ($serverIndent + '"' + $script:ServerName + '": ' + $serverJson)
    $lines += ($BaseIndent + '}')
    return ($lines -join $NewLine)
}

function Insert-ObjectProperty {
    param([string]$Text, $Object, [string]$Name, [string]$ValueJson, [string]$Unit)
    $newLine = Get-NewLine $Text
    $objectIndent = Get-ObjectIndent $Text $Object.ObjectStart
    $childIndent = Get-ChildIndent $Text $Object $Unit
    $key = (ConvertTo-JsonString $Name) + ': ' + $ValueJson
    if ($Object.Properties.Count -eq 0) {
        $insert = $newLine + $childIndent + $key + $newLine + $objectIndent
        return $Text.Insert($Object.CloseIndex, $insert)
    }
    $last = $Object.Properties[$Object.Properties.Count - 1]
    if ($last.CommaIndex -ge 0) {
        $insert = $newLine + $childIndent + $key
        return $Text.Insert($last.CommaIndex + 1, $insert)
    }
    $insert = ',' + $newLine + $childIndent + $key
    return $Text.Insert($last.ValueEnd, $insert)
}

function Set-ObjectProperty {
    param([string]$Text, $Object, [string]$Name, [string]$ValueJson, [string]$Unit)
    $prop = Get-JsoncProperty $Object.Properties $Name
    if ($null -eq $prop) {
        return (Insert-ObjectProperty -Text $Text -Object $Object -Name $Name -ValueJson $ValueJson -Unit $Unit)
    }
    return $Text.Remove($prop.ValueStart, $prop.ValueEnd - $prop.ValueStart).Insert($prop.ValueStart, $ValueJson)
}

function Remove-ObjectProperty {
    param([string]$Text, $Object, [string]$Name)
    $prop = Get-JsoncProperty $Object.Properties $Name
    if ($null -eq $prop) { return $Text }
    $index = [Array]::IndexOf($Object.Properties, $prop)
    if ($index -lt 0) { return $Text }
    if ($index -lt ($Object.Properties.Count - 1)) {
        $end = $prop.CommaIndex
        if ($end -lt 0) { $end = $prop.ValueEnd - 1 }
        return $Text.Remove($prop.KeyStart, $end - $prop.KeyStart + 1)
    }
    if ($prop.CommaIndex -ge 0) {
        return $Text.Remove($prop.KeyStart, $prop.CommaIndex - $prop.KeyStart + 1)
    }
    if ($index -gt 0) {
        $prev = $Object.Properties[$index - 1]
        if ($prev.CommaIndex -ge 0) {
            return $Text.Remove($prev.CommaIndex, $prop.ValueEnd - $prev.CommaIndex)
        }
    }
    return $Text.Remove($prop.KeyStart, $prop.ValueEnd - $prop.KeyStart)
}

function Get-ObjectAtProperty {
    param([string]$Text, $Property)
    return (Get-JsoncObjectProperties $Text $Property.ValueStart)
}

function Set-ServerProperties {
    param([string]$Text, $ServerObject, [string]$Shim, [string]$Unit, [bool]$IsNative)
    $server = Get-ObjectAtProperty $Text $ServerObject
    $Text = Set-ObjectProperty -Text $Text -Object $server -Name 'type' -ValueJson '"local"' -Unit $Unit
    $server = Get-ObjectAtProperty $Text $ServerObject
    $Text = Set-ObjectProperty -Text $Text -Object $server -Name 'command' -ValueJson (Get-CommandJson $Shim) -Unit $Unit
    if ($IsNative) {
        $server = Get-ObjectAtProperty $Text $ServerObject
        if ($null -eq (Get-JsoncProperty $server.Properties 'codemode')) {
            $Text = Insert-ObjectProperty -Text $Text -Object $server -Name 'codemode' -ValueJson 'false' -Unit $Unit
        }
        $server = Get-ObjectAtProperty $Text $ServerObject
        $Text = Remove-ObjectProperty -Text $Text -Object $server -Name 'enabled'
    } else {
        $server = Get-ObjectAtProperty $Text $ServerObject
        if ($null -eq (Get-JsoncProperty $server.Properties 'enabled')) {
            $Text = Insert-ObjectProperty -Text $Text -Object $server -Name 'enabled' -ValueJson 'true' -Unit $Unit
        }
    }
    return $Text
}

function Set-NativeServer {
    param([string]$Text, $McpObject, [string]$Shim, [string]$Unit, [string]$NewLine)
    $mcp = Get-ObjectAtProperty $Text $McpObject
    $serversProp = Get-JsoncProperty $mcp.Properties 'servers'
    if ($null -eq $serversProp) {
        $baseIndent = Get-ChildIndent $Text $mcp $Unit
        $serversJson = '{' + $NewLine + ($baseIndent + $Unit) + '"' + $script:ServerName + '": ' + (New-ServerJson -Indent ($baseIndent + $Unit) -Unit $Unit -NewLine $NewLine -Shim $Shim -IsNative $true) + $NewLine + $baseIndent + '}'
        $Text = Insert-ObjectProperty -Text $Text -Object $mcp -Name 'servers' -ValueJson $serversJson -Unit $Unit
    } else {
        $servers = Get-ObjectAtProperty $Text $serversProp
        $serverProp = Get-JsoncProperty $servers.Properties $script:ServerName
        if ($null -eq $serverProp) {
            $keyIndent = Get-ChildIndent $Text $servers $Unit
            $serverJson = New-ServerJson -Indent $keyIndent -Unit $Unit -NewLine $NewLine -Shim $Shim -IsNative $true
            $Text = Insert-ObjectProperty -Text $Text -Object $servers -Name $script:ServerName -ValueJson $serverJson -Unit $Unit
        } else {
            $Text = Set-ServerProperties -Text $Text -ServerObject $serverProp -Shim $Shim -Unit $Unit -IsNative $true
        }
    }
    $mcp = Get-ObjectAtProperty $Text $McpObject
    $Text = Remove-ObjectProperty -Text $Text -Object $mcp -Name $script:ServerName
    return $Text
}

function Set-LegacyServer {
    param([string]$Text, $McpObject, [string]$Shim, [string]$Unit, [string]$NewLine)
    $mcp = Get-ObjectAtProperty $Text $McpObject
    $serverProp = Get-JsoncProperty $mcp.Properties $script:ServerName
    if ($null -eq $serverProp) {
        $keyIndent = Get-ChildIndent $Text $mcp $Unit
        $serverJson = New-ServerJson -Indent $keyIndent -Unit $Unit -NewLine $NewLine -Shim $Shim -IsNative $false
        $Text = Insert-ObjectProperty -Text $Text -Object $mcp -Name $script:ServerName -ValueJson $serverJson -Unit $Unit
    } else {
        $Text = Set-ServerProperties -Text $Text -ServerObject $serverProp -Shim $Shim -Unit $Unit -IsNative $false
    }
    $mcp = Get-ObjectAtProperty $Text $McpObject
    $serversProp = Get-JsoncProperty $mcp.Properties 'servers'
    if ($null -ne $serversProp) {
        $servers = Get-ObjectAtProperty $Text $serversProp
        $Text = Remove-ObjectProperty -Text $Text -Object $servers -Name $script:ServerName
    }
    return $Text
}

function Set-ServerEntry {
    param([string]$Text, [string]$Shim, [bool]$IsNative)
    if ([string]::IsNullOrWhiteSpace($Text)) { $Text = '{}' }
    $root = Get-RootProperties $Text
    $unit = Get-IndentUnit $Text $root
    $newLine = Get-NewLine $Text
    $mcpProp = Get-JsoncProperty $root.Properties 'mcp'
    if ($null -eq $mcpProp) {
        $baseIndent = Get-ChildIndent $Text $root $unit
        $mcpJson = New-McpJson -BaseIndent $baseIndent -Unit $unit -NewLine $newLine -Shim $Shim -IsNative $IsNative
        return (Insert-ObjectProperty -Text $Text -Object $root -Name 'mcp' -ValueJson $mcpJson -Unit $unit)
    }
    $mcp = Get-ObjectAtProperty $Text $mcpProp
    if ($IsNative) {
        return (Set-NativeServer -Text $Text -McpObject $mcpProp -Shim $Shim -Unit $unit -NewLine $newLine)
    }
    return (Set-LegacyServer -Text $Text -McpObject $mcpProp -Shim $Shim -Unit $unit -NewLine $newLine)
}

function Remove-ServerEntry {
    param([string]$Text)
    if ([string]::IsNullOrWhiteSpace($Text)) { return $Text }
    $root = Get-RootProperties $Text
    $mcpProp = Get-JsoncProperty $root.Properties 'mcp'
    if ($null -eq $mcpProp) { return $Text }
    $mcp = Get-ObjectAtProperty $Text $mcpProp
    $serversProp = Get-JsoncProperty $mcp.Properties 'servers'
    if ($null -ne $serversProp) {
        $servers = Get-ObjectAtProperty $Text $serversProp
        $Text = Remove-ObjectProperty -Text $Text -Object $servers -Name $script:ServerName
    }
    $mcp = Get-ObjectAtProperty $Text $mcpProp
    $Text = Remove-ObjectProperty -Text $Text -Object $mcp -Name $script:ServerName
    return $Text
}

function Get-ServerPresence {
    param([string]$Text)
    if ([string]::IsNullOrWhiteSpace($Text)) { return 'none' }
    $root = Get-RootProperties $Text
    $mcpProp = Get-JsoncProperty $root.Properties 'mcp'
    if ($null -eq $mcpProp) { return 'none' }
    $mcp = Get-ObjectAtProperty $Text $mcpProp
    $legacy = $null -ne (Get-JsoncProperty $mcp.Properties $script:ServerName)
    $native = $false
    $serversProp = Get-JsoncProperty $mcp.Properties 'servers'
    if ($null -ne $serversProp) {
        $servers = Get-ObjectAtProperty $Text $serversProp
        $native = $null -ne (Get-JsoncProperty $servers.Properties $script:ServerName)
    }
    if ($native -and $legacy) { return 'both' }
    if ($native) { return 'native' }
    if ($legacy) { return 'legacy' }
    return 'none'
}

function Get-McpShape {
    param([string]$Text)
    if ([string]::IsNullOrWhiteSpace($Text)) { return $null }
    $root = Get-RootProperties $Text
    $mcpProp = Get-JsoncProperty $root.Properties 'mcp'
    if ($null -eq $mcpProp) { return $null }
    $mcp = Get-ObjectAtProperty $Text $mcpProp
    if ($null -ne (Get-JsoncProperty $mcp.Properties 'servers')) { return 'native' }
    if ($mcp.Properties.Count -gt 0) { return 'legacy' }
    return $null
}

function Get-EntryCommandArray {
    param($Entry)
    if ($null -eq $Entry -or $null -eq $Entry.command) { return @() }
    return @($Entry.command | ForEach-Object { [string]$_ })
}

function Test-CommandOwnedByShim {
    param([string[]]$Command, [string]$Shim)
    if ([string]::IsNullOrWhiteSpace($Shim) -or $Command.Count -eq 0) { return $false }
    $target = $Shim.Trim().Trim('"')
    foreach ($part in $Command) {
        $candidate = ([string]$part).Trim().Trim('"')
        if ([string]::Equals($candidate, $target, [System.StringComparison]::OrdinalIgnoreCase)) { return $true }
    }
    return $false
}

function Get-EntryObjectFromProperty {
    param([string]$Text, $Property)
    $valueText = $Text.Substring($Property.ValueStart, $Property.ValueEnd - $Property.ValueStart)
    $clean = ConvertTo-ValidationJson $valueText
    return (ConvertFrom-Json $clean)
}

function Get-ServerEntryInfo {
    param([string]$Text, [string]$Shim)
    $info = [ordered]@{
        Present = $false
        Owned   = $false
        Shape   = 'none'
        Command = @()
        FoundShim = ''
    }
    if ([string]::IsNullOrWhiteSpace($Text)) { return [pscustomobject]$info }
    $root = Get-RootProperties $Text
    $mcpProp = Get-JsoncProperty $root.Properties 'mcp'
    if ($null -eq $mcpProp) { return [pscustomobject]$info }
    $mcp = Get-ObjectAtProperty $Text $mcpProp

    $shapes = @()
    $commands = @()
    $serversProp = Get-JsoncProperty $mcp.Properties 'servers'
    if ($null -ne $serversProp) {
        $servers = Get-ObjectAtProperty $Text $serversProp
        $serverProp = Get-JsoncProperty $servers.Properties $script:ServerName
        if ($null -ne $serverProp) {
            $shapes += 'native'
            $obj = Get-EntryObjectFromProperty $Text $serverProp
            $commands += Get-EntryCommandArray $obj
        }
    }
    $legacyProp = Get-JsoncProperty $mcp.Properties $script:ServerName
    if ($null -ne $legacyProp) {
        $shapes += 'legacy'
        $obj = Get-EntryObjectFromProperty $Text $legacyProp
        $commands += Get-EntryCommandArray $obj
    }

    if ($shapes.Count -eq 0) { return [pscustomobject]$info }
    $info.Present = $true
    $info.Shape = if ($shapes.Count -gt 1) { 'both' } else { $shapes[0] }
    $info.Command = @($commands)
    $info.Owned = Test-CommandOwnedByShim -Command $commands -Shim $Shim
    foreach ($part in $commands) {
        $candidate = ([string]$part).Trim().Trim('"')
        if ($candidate -match 'run-mcp-server\.cmd$') { $info.FoundShim = $candidate }
    }
    return [pscustomobject]$info
}

function Remove-OwnedServerEntry {
    param([string]$Text, [string]$Shim)
    $root = Get-RootProperties $Text
    $mcpProp = Get-JsoncProperty $root.Properties 'mcp'
    if ($null -eq $mcpProp) { return $Text }
    $mcp = Get-ObjectAtProperty $Text $mcpProp
    $serversProp = Get-JsoncProperty $mcp.Properties 'servers'
    if ($null -ne $serversProp) {
        $servers = Get-ObjectAtProperty $Text $serversProp
        $serverProp = Get-JsoncProperty $servers.Properties $script:ServerName
        if ($null -ne $serverProp) {
            $obj = Get-EntryObjectFromProperty $Text $serverProp
            if (Test-CommandOwnedByShim -Command (Get-EntryCommandArray $obj) -Shim $Shim) {
                $Text = Remove-ObjectProperty -Text $Text -Object $servers -Name $script:ServerName
            }
        }
    }

    $root = Get-RootProperties $Text
    $mcpProp = Get-JsoncProperty $root.Properties 'mcp'
    if ($null -eq $mcpProp) { return $Text }
    $mcp = Get-ObjectAtProperty $Text $mcpProp
    $legacyProp = Get-JsoncProperty $mcp.Properties $script:ServerName
    if ($null -ne $legacyProp) {
        $obj = Get-EntryObjectFromProperty $Text $legacyProp
        if (Test-CommandOwnedByShim -Command (Get-EntryCommandArray $obj) -Shim $Shim) {
            $Text = Remove-ObjectProperty -Text $Text -Object $mcp -Name $script:ServerName
        }
    }
    return $Text
}

function Invoke-Verify {
    param([string]$Dir, [string]$Shim, [string]$OutPath)
    $info = [pscustomobject]@{
        Present   = $false
        Owned     = $false
        Shape     = 'none'
        FoundShim = ''
    }
    foreach ($name in @('opencode.jsonc', 'opencode.json')) {
        $path = Join-Path $Dir $name
        if (-not (Test-Path -LiteralPath $path)) { continue }
        $text = Read-ConfigText $path
        if (-not (Test-JsoncParse $text)) { continue }
        try {
            $candidate = Get-ServerEntryInfo -Text $text -Shim $Shim
        } catch { continue }
        if ($candidate.Present) {
            $info = $candidate
            if ($candidate.Owned) { break }
        }
    }
    $installedValue = if ($info.Present) { '1' } else { '0' }
    $ownedValue = if ($info.Owned) { '1' } else { '0' }
    $lines = @(
        ('present=' + $installedValue),
        ('owned=' + $ownedValue),
        ('shape=' + $info.Shape),
        ('foundShim=' + $info.FoundShim),
        ('shim=' + $Shim)
    )
    if ($OutPath) {
        $parent = Split-Path -Parent $OutPath
        if ($parent -and -not (Test-Path -LiteralPath $parent)) { New-Item -ItemType Directory -Path $parent -Force | Out-Null }
        $utf8 = New-Object System.Text.UTF8Encoding($false)
        [System.IO.File]::WriteAllText($OutPath, ($lines -join "`r`n"), $utf8)
    } else {
        $lines | Write-Output
    }
}

function ConvertTo-ValidationJson {
    param([string]$Text)
    if ([string]::IsNullOrWhiteSpace($Text)) { return '{}' }
    $chars = $Text.ToCharArray()
    $n = $chars.Length
    $i = 0
    while ($i -lt $n) {
        $c = $chars[$i]
        if ($c -eq [char]'"') {
            $i++
            while ($i -lt $n) {
                if ($chars[$i] -eq [char]'\') { $i += 2; continue }
                if ($chars[$i] -eq [char]'"') { $i++; break }
                $i++
            }
            continue
        }
        if ($c -eq [char]'/' -and ($i + 1) -lt $n) {
            $d = $chars[$i + 1]
            if ($d -eq [char]'/') {
                $chars[$i] = ' '; $chars[$i + 1] = ' '
                $i += 2
                while ($i -lt $n -and $chars[$i] -ne [char]"`n" -and $chars[$i] -ne [char]"`r") { $chars[$i] = ' '; $i++ }
                continue
            }
            if ($d -eq [char]'*') {
                $chars[$i] = ' '; $chars[$i + 1] = ' '
                $i += 2
                while (($i + 1) -lt $n -and -not ($chars[$i] -eq [char]'*' -and $chars[$i + 1] -eq [char]'/')) {
                    if ($chars[$i] -ne [char]"`n" -and $chars[$i] -ne [char]"`r") { $chars[$i] = ' ' }
                    $i++
                }
                if (($i + 1) -lt $n) { $chars[$i] = ' '; $chars[$i + 1] = ' '; $i += 2 }
                continue
            }
        }
        $i++
    }
    $text2 = -join $chars
    $chars2 = $text2.ToCharArray()
    $n2 = $chars2.Length
    $i = 0
    while ($i -lt $n2) {
        $c = $chars2[$i]
        if ($c -eq [char]'"') {
            $i++
            while ($i -lt $n2) {
                if ($chars2[$i] -eq [char]'\') { $i += 2; continue }
                if ($chars2[$i] -eq [char]'"') { $i++; break }
                $i++
            }
            continue
        }
        if ($c -eq [char]',') {
            $j = $i + 1
            while ($j -lt $n2 -and [char]::IsWhiteSpace($chars2[$j])) { $j++ }
            if ($j -lt $n2 -and ($chars2[$j] -eq [char]'}' -or $chars2[$j] -eq [char]']')) { $chars2[$i] = ' ' }
        }
        $i++
    }
    return (-join $chars2)
}

function Test-JsoncParse {
    param([string]$Text)
    try {
        $clean = ConvertTo-ValidationJson $Text
        $null = ConvertFrom-Json $clean
        return $true
    } catch {
        return $false
    }
}

function Read-ConfigText {
    param([string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) { return $null }
    return [System.IO.File]::ReadAllText($Path)
}

function Write-ConfigText {
    param([string]$Path, [string]$Text)
    if (-not (Test-JsoncParse $Text)) { throw "Refusing to write invalid JSON: $Path" }
    $dir = Split-Path -Parent $Path
    if ($dir -and -not (Test-Path -LiteralPath $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
    $tmp = $Path + '.tmp-' + [guid]::NewGuid().ToString('N')
    try {
        $utf8 = New-Object System.Text.UTF8Encoding($false)
        [System.IO.File]::WriteAllText($tmp, $Text, $utf8)
        Move-Item -LiteralPath $tmp -Destination $Path -Force
    } finally {
        if (Test-Path -LiteralPath $tmp) { Remove-Item -LiteralPath $tmp -Force -ErrorAction SilentlyContinue }
    }
}

function Resolve-ConfigDir {
    param([string]$Explicit)
    if ($Explicit) { return $Explicit }
    if ($env:OPENCODE_CONFIG_DIR) { return $env:OPENCODE_CONFIG_DIR }
    if ($env:XDG_CONFIG_HOME) { return (Join-Path $env:XDG_CONFIG_HOME 'opencode') }
    return (Join-Path $env:USERPROFILE '.config\opencode')
}

function Resolve-ConfigTarget {
    param([string]$Dir, [bool]$IsNative)
    $json = Join-Path $Dir 'opencode.json'
    $jsonc = Join-Path $Dir 'opencode.jsonc'
    $jsonExists = Test-Path -LiteralPath $json
    $jsoncExists = Test-Path -LiteralPath $jsonc
    $jsonText = if ($jsonExists) { Read-ConfigText $json } else { $null }
    $jsoncText = if ($jsoncExists) { Read-ConfigText $jsonc } else { $null }
    if ($jsonExists -and -not (Test-JsoncParse $jsonText)) { throw "Invalid JSONC in $json" }
    if ($jsoncExists -and -not (Test-JsoncParse $jsoncText)) { throw "Invalid JSONC in $jsonc" }
    if ($IsNative) {
        if ($jsoncExists) { return $jsonc }
        if ($jsonExists) { return $json }
        return $jsonc
    }
    return $json
}

function Get-VersionFromText {
    param([string]$Text)
    if (-not $Text) { return '' }
    $m = [regex]::Match($Text, '(\d+\.\d+\.\d+)')
    if ($m.Success) { return $m.Groups[1].Value }
    return ''
}

function Get-OpenCodeDetection {
    param([string]$Dir)
    $configDir = Resolve-ConfigDir $Dir
    $installed = $false
    $source = 'none'
    $candidates = @()

    $cmd = Get-Command opencode -ErrorAction SilentlyContinue
    if ($cmd) {
        $installed = $true
        $source = 'cli'
        try {
            $text = (& opencode --version 2>$null | Select-Object -First 1).ToString()
            $v = Get-VersionFromText $text
            if ($v) { $candidates += [pscustomobject]@{ Version = $v; Source = 'cli' } }
        } catch { }
    }

    $desktopCandidates = @()
    if ($env:LOCALAPPDATA) { $desktopCandidates += (Join-Path $env:LOCALAPPDATA 'Programs\ai.opencode.desktop\OpenCode.exe') }
    if ($env:PROGRAMFILES) { $desktopCandidates += (Join-Path $env:PROGRAMFILES 'ai.opencode.desktop\OpenCode.exe') }
    if (${env:ProgramFiles(x86)}) { $desktopCandidates += (Join-Path ${env:ProgramFiles(x86)} 'ai.opencode.desktop\OpenCode.exe') }
    foreach ($p in $desktopCandidates) {
        if (Test-Path -LiteralPath $p) {
            $installed = $true
            if ($source -eq 'none') { $source = 'desktop' }
        }
    }

    $bundledCli = ''
    $cliRoot = if ($env:APPDATA) { Join-Path $env:APPDATA 'ai.opencode.desktop\cli' } else { '' }
    if ($cliRoot -and (Test-Path -LiteralPath $cliRoot)) {
        $installed = $true
        if ($source -eq 'none') { $source = 'desktop' }
        foreach ($cliDir in Get-ChildItem -LiteralPath $cliRoot -Directory -ErrorAction SilentlyContinue) {
            $exe = Join-Path $cliDir.FullName 'opencode-cli.exe'
            if (-not (Test-Path -LiteralPath $exe)) { continue }
            if (-not $bundledCli) { $bundledCli = $exe }
            $v = Get-VersionFromText $cliDir.Name
            if (-not $v) {
                try {
                    $text = (& $exe --version 2>$null | Select-Object -First 1).ToString()
                    $v = Get-VersionFromText $text
                } catch { }
            }
            if ($v) { $candidates += [pscustomobject]@{ Version = $v; Source = 'desktop'; Path = $exe } }
        }
    }

    $version = ''
    if ($candidates.Count -gt 0) {
        $best = $candidates | Sort-Object { [version]$_.Version } -Descending | Select-Object -First 1
        $version = $best.Version
        $source = $best.Source
        if ($best.PSObject.Properties['Path']) { $bundledCli = $best.Path }
    }

    $shape = 'none'
    foreach ($name in @('opencode.jsonc', 'opencode.json')) {
        $path = Join-Path $configDir $name
        if (-not (Test-Path -LiteralPath $path)) { continue }
        $text = Read-ConfigText $path
        if (-not (Test-JsoncParse $text)) { continue }
        try { $s = Get-McpShape $text } catch { continue }
        if ($s -eq 'native') { $shape = 'native'; break }
        if ($s -eq 'legacy') { $shape = 'legacy' }
    }

    return [pscustomobject]@{
        Installed = $installed
        Version = $version
        Source = $source
        ConfigShape = $shape
        ConfigDir = $configDir
        BundledCli = $bundledCli
    }
}

function Invoke-Detect {
    param([string]$Dir, [string]$OutPath)
    $d = Get-OpenCodeDetection $Dir
    $installedValue = if ($d.Installed) { '1' } else { '0' }
    $lines = @(
        ('installed=' + $installedValue),
        ('version=' + $d.Version),
        ('source=' + $d.Source),
        ('config=' + $d.ConfigShape),
        ('configDir=' + $d.ConfigDir),
        ('bundledCli=' + $d.BundledCli)
    )
    if ($OutPath) {
        $parent = Split-Path -Parent $OutPath
        if ($parent -and -not (Test-Path -LiteralPath $parent)) { New-Item -ItemType Directory -Path $parent -Force | Out-Null }
        $utf8 = New-Object System.Text.UTF8Encoding($false)
        [System.IO.File]::WriteAllText($OutPath, ($lines -join "`r`n"), $utf8)
    } else {
        $lines | Write-Output
    }
}

function Remove-ServerFromPath {
    param([string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) { return }
    $text = Read-ConfigText $Path
    if (-not (Test-JsoncParse $text)) {
        Write-Log "WARNING: skipped invalid JSONC while removing duplicate: $Path"
        return
    }
    if ((Get-ServerPresence $text) -eq 'none') { return }
    $after = Remove-ServerEntry -Text $text
    if ($after -ne $text) {
        Write-ConfigText -Path $Path -Text $after
        Write-Log "Removed duplicate server entry from opencode config: $Path"
    }
}

function Invoke-Install {
    param([string]$Dir, [string]$Shim, [bool]$IsNative)
    $target = Resolve-ConfigTarget -Dir $Dir -IsNative $IsNative
    $text = Read-ConfigText $target
    if ($null -eq $text) { $text = '{}' }
    if (-not (Test-JsoncParse $text)) { throw "Existing config is not valid JSONC: $target" }
    $before = $text
    $after = Set-ServerEntry -Text $text -Shim $Shim -IsNative $IsNative
    if ($after -eq $before) {
        Write-Log "opencode config already up to date: $target"
    } else {
        Write-ConfigText -Path $target -Text $after
        $shape = if ($IsNative) { 'native' } else { 'legacy' }
        Write-Log "Updated opencode config ($shape): $target"
    }
    $other = if ((Split-Path -Leaf $target) -eq 'opencode.jsonc') { Join-Path $Dir 'opencode.json' } else { Join-Path $Dir 'opencode.jsonc' }
    Remove-ServerFromPath $other
}

function Invoke-Remove {
    param([string]$Dir, [string]$Shim)
    $paths = @((Join-Path $Dir 'opencode.json'), (Join-Path $Dir 'opencode.jsonc'))
    $removed = 0
    foreach ($path in $paths) {
        if (-not (Test-Path -LiteralPath $path)) { continue }
        $text = Read-ConfigText $path
        if (-not (Test-JsoncParse $text)) {
            Write-Log "WARNING: skipped invalid JSONC during removal: $path"
            continue
        }
        $presence = Get-ServerPresence $text
        if ($presence -eq 'none') { continue }
        if ($Shim) {
            $info = Get-ServerEntryInfo -Text $text -Shim $Shim
            if (-not $info.Owned) {
                Write-Log "Server entry is not owned by this installer; left untouched: $path"
                continue
            }
            $after = Remove-OwnedServerEntry -Text $text -Shim $Shim
        } else {
            $after = Remove-ServerEntry -Text $text
        }
        if ($after -ne $text) {
            Write-ConfigText -Path $path -Text $after
            Write-Log "Removed server entry from opencode config: $path"
            $removed++
        }
    }
    if ($removed -eq 0) { Write-Log 'No owned opencode server entry found to remove' }
}

if ($MyInvocation.InvocationName -ne '.') {
    try {
        $resolvedDir = Resolve-ConfigDir $ConfigDir
        if ($Action -eq 'Install') {
            if ([string]::IsNullOrWhiteSpace($ShimPath)) { throw 'ShimPath is required for Install' }
            Invoke-Install -Dir $resolvedDir -Shim $ShimPath -IsNative ([bool]$Native)
        } elseif ($Action -eq 'Remove') {
            Invoke-Remove -Dir $resolvedDir -Shim $ShimPath
        } elseif ($Action -eq 'Verify') {
            Invoke-Verify -Dir $resolvedDir -Shim $ShimPath -OutPath $OutputPath
        } else {
            Invoke-Detect -Dir $resolvedDir -OutPath $OutputPath
        }
        exit 0
    } catch {
        Write-Log ('ERROR: ' + $_.Exception.Message)
        exit 1
    }
}
