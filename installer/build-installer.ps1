#Requires -Version 5.1
<#
.SYNOPSIS
    Builds mcp-servers-for-revit and compiles the Inno Setup installer.
#>
param(
    [int[]]$RevitVersions = @(2020, 2021, 2022, 2023, 2024, 2025, 2026),
    [switch]$VerifyServerBuild,
    [switch]$SkipPluginBuild,
    [string]$OutputDir = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$RepoRoot = Split-Path $PSScriptRoot -Parent
$InstallerDir = $PSScriptRoot
$StagedDir = Join-Path $InstallerDir "staged"
if (-not $OutputDir) { $OutputDir = Join-Path $InstallerDir "output" }

$YearToConfig = @{
    2020 = "R20"; 2021 = "R21"; 2022 = "R22"; 2023 = "R23"
    2024 = "R24"; 2025 = "R25"; 2026 = "R26"
}

function Invoke-Build {
    param([string]$Description, [scriptblock]$Action)
    Write-Host "`n=== $Description ===" -ForegroundColor Cyan
    & $Action
    if ($LASTEXITCODE -ne 0 -and $null -ne $LASTEXITCODE) {
        throw "$Description failed with exit code $LASTEXITCODE"
    }
}

# ---------------------------------------------------------------
# Step 1: Stage the MCP Server runtime into the installer.
# The published npm package (mcp-server-for-revit@1.0.0) is broken
# (indirect ajv dep, never republished) and cannot be republished by
# this fork, so AI-client configs written by the installer run the
# server from the bundled, compiled build instead of `npx`.
# ---------------------------------------------------------------
$ServerSrc = Join-Path $RepoRoot "server"
if (-not (Test-Path (Join-Path $ServerSrc "build\index.js"))) {
    Push-Location $ServerSrc
    try {
        Write-Host "`n=== Building MCP Server ===" -ForegroundColor Cyan
        if (-not (Test-Path (Join-Path $ServerSrc "node_modules"))) {
            Write-Host "  node_modules missing - running npm ci" -ForegroundColor Yellow
            & cmd /c "npm ci --no-progress"
            if ($LASTEXITCODE -ne 0) { throw "npm ci failed - run it manually in server/ first" }
        }
        & cmd /c "npm run build" 2>&1 | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "npm run build failed" }
    } finally { Pop-Location }
}
$ServerBuildDir = Join-Path $ServerSrc "build"
$ServerModulesDir = Join-Path $ServerSrc "node_modules"
if (-not (Test-Path $ServerBuildDir)) { throw "server/build missing - build the MCP server first (npm run build)" }
if (-not (Test-Path $ServerModulesDir)) { throw "server/node_modules missing - run npm ci in server/ first" }
Write-Host "`n=== MCP Server ready for bundling ($((Get-ChildItem $ServerModulesDir | Measure-Object).Count) modules) ===" -ForegroundColor Cyan

# ---------------------------------------------------------------
# Step 2: Build Revit Plugin + CommandSet
# ---------------------------------------------------------------
if (-not $SkipPluginBuild) {
    if (Test-Path $StagedDir) { Remove-Item $StagedDir -Recurse -Force }
    New-Item -ItemType Directory -Path $StagedDir -Force | Out-Null

    $slnPath = Join-Path $RepoRoot "mcp-servers-for-revit.sln"
    $builtVersions = @()

    foreach ($year in $RevitVersions) {
        $config = $YearToConfig[$year]
        if (-not $config) { continue }
        $buildConfig = "Release $config"
        $addinOutputDir = Join-Path $RepoRoot "plugin\bin\AddIn $year $buildConfig"

        Write-Host "`n=== Building Revit $year ($buildConfig) ===" -ForegroundColor Cyan
        dotnet build $slnPath -c $buildConfig --verbosity minimal
        if ($LASTEXITCODE -ne 0) { Write-Host "  Skipped" -ForegroundColor Yellow; continue }

        # Build any *-commandset projects present in the repo (auto-discovered,
        # so new command set features require no installer edits). Their deploy
        # targets copy into the plugin's AddIn output directory before staging.
        Get-ChildItem $RepoRoot -Directory -Filter "*-commandset" | ForEach-Object {
            $csproj = Get-ChildItem $_.FullName -Filter "*.csproj" | Select-Object -First 1
            if ($csproj) {
                Write-Host "  Building $($csproj.BaseName) ($buildConfig)..." -ForegroundColor Cyan
                dotnet build $csproj.FullName -c $buildConfig --verbosity minimal
            }
        }

        if (-not (Test-Path $addinOutputDir)) { Write-Host "  Output not found" -ForegroundColor Yellow; continue }

        Copy-Item -Path $addinOutputDir -Destination (Join-Path $StagedDir "$year") -Recurse -Force
        $builtVersions += $year
        Write-Host "  Staged" -ForegroundColor Green
    }

    if ($builtVersions.Count -eq 0) { throw "No Revit versions built" }
    Write-Host "`nBuilt: $($builtVersions -join ', ')" -ForegroundColor Green
} else {
    $builtVersions = @()
    if (Test-Path $StagedDir) {
        Get-ChildItem $StagedDir -Directory | ForEach-Object {
            $y = 0
            if ([int]::TryParse($_.Name, [ref]$y) -and $YearToConfig.ContainsKey($y)) { $builtVersions += $y }
        }
    }
    if ($builtVersions.Count -eq 0) { throw "No staged versions found." }
}

# ---------------------------------------------------------------
# Step 2a: Stage the MCP Server bundle (build/ + node_modules)
# into staged\Server so the compiled exe can run it locally.
# ---------------------------------------------------------------
$StagedServerDir = Join-Path $StagedDir "Server"
if (Test-Path $StagedServerDir) { Remove-Item $StagedServerDir -Recurse -Force }
New-Item -ItemType Directory -Path $StagedServerDir -Force | Out-Null
Copy-Item -Path $ServerBuildDir -Destination (Join-Path $StagedServerDir "build") -Recurse -Force
Copy-Item -Path (Join-Path $ServerSrc "package.json") -Destination $StagedServerDir -Force
Copy-Item -Path $ServerModulesDir -Destination $StagedServerDir -Recurse -Force
$nItems = (Get-ChildItem $StagedServerDir -Recurse -File | Measure-Object).Count
Write-Host "`nStaged MCP Server bundle: $nItems files under staged\Server" -ForegroundColor Green

# ---------------------------------------------------------------
# Step 2b-2: Stage a portable Node runtime (node.exe only) so the
# client configs + AnythingLLM entries never depend on a system
# Node install. Cached under .node-cache so only the first build
# (or a version bump) pays the download cost.
# ---------------------------------------------------------------
$NodeMajor = "22"
$NodeCacheDir = Join-Path $InstallerDir ".node-cache"
$NodeCacheExe = Join-Path $NodeCacheDir "node.exe"
if (-not (Test-Path $NodeCacheExe)) {
    New-Item -ItemType Directory -Force $NodeCacheDir | Out-Null
    $idx = (Invoke-WebRequest -UseBasicParsing "https://nodejs.org/dist/index.json").Content | ConvertFrom-Json
    $ver = ($idx | Where-Object { $_.version -like "v$NodeMajor.*" } | Select-Object -First 1).version
    if (-not $ver) { throw "no v$NodeMajor node release found on nodejs.org" }
    $name = "node-$ver-win-x64"
    $zipUrl = "https://nodejs.org/dist/$ver/$name.zip"
    $zipTmp = Join-Path $env:TEMP "$name.zip"
    $dirTmp = Join-Path $env:TEMP $name
    Write-Host "Downloading portable $name"
    Invoke-WebRequest -Uri $zipUrl -OutFile $zipTmp
    if (Test-Path $dirTmp) { Remove-Item $dirTmp -Recurse -Force }
    Expand-Archive -Path $zipTmp -DestinationPath $dirTmp -Force
    Copy-Item (Join-Path $dirTmp "$name\node.exe") $NodeCacheExe -Force
    Remove-Item $dirTmp, $zipTmp -Recurse -Force
}
$StagedNodeDir = Join-Path $StagedDir "node"
if (Test-Path $StagedNodeDir) { Remove-Item $StagedNodeDir -Recurse -Force }
New-Item -ItemType Directory -Force $StagedNodeDir | Out-Null
Copy-Item $NodeCacheExe (Join-Path $StagedNodeDir "node.exe") -Force
Write-Host "Staged portable node.exe -> $StagedNodeDir" -ForegroundColor Green

# ---------------------------------------------------------------
# Step 2b: Seed per-set registry fragments into each staged plugin tree
# ---------------------------------------------------------------
# The plugin's MCP dispatch is driven solely by Commands\commandRegistry.json.
# A fresh install has none, so the plugin would start with an empty registry
# (commands visible in Settings but not callable). For each staged year we
# write, next to every command set's command.json, a registry-entries.json
# fragment (JSON array of that set's command entries). At install time the
# [Code] section assembles Commands\commandRegistry.json from the fragments
# of the sets the user kept selected - so pruning a set at install time also
# prunes its registry entries. All entries are shipped disabled to preserve
# the opt-in design: the user still enables commands via the plugin's
# Settings page (check -> Save), but nothing is ever "displayed but
# unreachable". On reinstall the installer preserves an existing registry
# (see CopyToRevitAddins) so user opt-ins survive upgrades.
foreach ($year in $builtVersions) {
    $commandsDir = Join-Path $StagedDir "$year\revit_mcp_plugin\Commands"
    if (-not (Test-Path $commandsDir)) { continue }

    foreach ($setDir in (Get-ChildItem $commandsDir -Directory)) {
        $manifestPath = Join-Path $setDir.FullName "command.json"
        if (-not (Test-Path $manifestPath)) { continue }
        try { $manifest = Get-Content $manifestPath -Raw | ConvertFrom-Json } catch { continue }
        if (-not $manifest.commands) { continue }

        $entries = @()
        foreach ($cmd in $manifest.commands) {
            $entries += [pscustomobject]@{
                commandName           = $cmd.commandName
                description           = $cmd.description
                assemblyPath          = "$($setDir.Name)\{VERSION}\$($cmd.assemblyPath)"
                enabled               = $false
                supportedRevitVersions = @([string]$year)
                developer             = $manifest.developer
            }
        }

        $fragmentPath = Join-Path $setDir.FullName "registry-entries.json"
        [System.IO.File]::WriteAllText($fragmentPath, (($entries | ConvertTo-Json -Depth 6)), (New-Object System.Text.UTF8Encoding $false))
        Write-Host "  Seeded registry fragment for ${year}/$($setDir.Name): $($entries.Count) commands (disabled by default)" -ForegroundColor DarkCyan
    }
}

# ---------------------------------------------------------------
# Step 3: Generate .iss
# ---------------------------------------------------------------
Write-Host "`n=== Generating .iss ===" -ForegroundColor Cyan

# The package version must match the plugin version (the version that matters
# to the user). The plugin assembly version is the source of truth, defined in
# plugin/Properties/AssemblyInfo.cs (updated by scripts/release.ps1).
$assemblyInfoPath = Join-Path $RepoRoot "plugin\Properties\AssemblyInfo.cs"
$pluginVersion = "1.0.0"
if (Test-Path $assemblyInfoPath) {
    $assemblyInfoText = Get-Content $assemblyInfoPath -Raw
    if ($assemblyInfoText -match 'AssemblyFileVersion\("([^"]+)"\)') {
        $pluginVersion = ($Matches[1] -split '\.')[0..2] -join '.'
    }
}

$issLines = @()
$issLines += '; MCP Servers for Revit Installer - Generated by build-installer.ps1'
$issLines += ''
$issLines += '[Setup]'
$issLines += 'AppName=MCP Servers for Revit'
$issLines += "AppVersion=$pluginVersion"
$issLines += 'AppPublisher=MCP Servers for Revit'
$issLines += 'AppPublisherURL=https://github.com/chris4d/mcp-servers-for-revit'
$issLines += 'DefaultDirName={autopf}\MCP Servers for Revit'
$issLines += 'DefaultGroupName=MCP Servers for Revit'
$issLines += "OutputDir=$OutputDir"
$issLines += 'OutputBaseFilename=mcp-servers-for-revit-setup'
$issLines += 'Compression=lzma2/ultra64'
$issLines += 'SolidCompression=yes'
$issLines += 'WizardStyle=modern'
$issLines += 'PrivilegesRequired=lowest'
$issLines += 'PrivilegesRequiredOverridesAllowed=dialog'
$issLines += ''
$issLines += '[Languages]'
$issLines += 'Name: "english"; MessagesFile: "compiler:Default.isl"'
$issLines += ''
$issLines += '[Files]'

foreach ($year in $builtVersions) {
    $issLines += "Source: `"staged\$year\*`"; DestDir: `"{app}\RevitPlugins\$year`"; Flags: recursesubdirs ignoreversion"
}

# Bundled MCP server runtime (runs locally; the npm package is stale/broken upstream)
$issLines += 'Source: "staged\Server\*"; DestDir: "{app}\Server"; Flags: recursesubdirs ignoreversion'

# Portable Node runtime + stdio launcher shim ({app}\node\node.exe -> run-mcp-server.cmd)
$issLines += 'Source: "staged\node\node.exe"; DestDir: "{app}\node"; Flags: ignoreversion'
$issLines += 'Source: "run-mcp-server.cmd"; DestDir: "{app}\Server"; Flags: ignoreversion'
$issLines += 'Source: "opencode-config.ps1"; DestDir: "{app}\Installer"; Flags: ignoreversion'
$issLines += 'Source: "opencode-config.ps1"; DestDir: "{tmp}"; Flags: dontcopy'

$issLines += ''
$issLines += '[Icons]'
$issLines += 'Name: "{group}\Uninstall MCP Servers for Revit"; Filename: "{uninstallexe}"'
$issLines += ''
$issLines += '[Code]'
$issLines += 'const'
$issLines += "  RevitRegKey = 'SOFTWARE\\Autodesk\\Revit\\Autodesk Revit ';"
$issLines += "  ServerKey = 'mcp-server-for-revit';"
$issLines += "  CommandSetFolder = 'revit_mcp_plugin\\Commands';"
$issLines += ''
$issLines += 'var'
$issLines += '  Page: TWizardPage;'
$issLines += '  SetPage: TWizardPage;'
$issLines += '  ClaudeCB, CursorCB, OpencodeCB, OpenCode2CB, AnythingLLMCB: TNewCheckBox;'
$issLines += '  OpenCodeVersion, OpenCodeSource, OpenCodeConfigShape: String;'
$issLines += '  OpenCodeInstalled: Boolean;'
$issLines += ''

# CommandSet selection: one checkbox per available command set, all defaulted on.
$commandSetNames = @()
foreach ($year in $builtVersions) {
    $cmdDir = Join-Path $StagedDir "$year\revit_mcp_plugin\Commands"
    if (Test-Path $cmdDir) {
        Get-ChildItem $cmdDir -Directory | ForEach-Object {
            $n = $_.Name
            if ($commandSetNames -notcontains $n) { $commandSetNames += $n }
        }
    }
}
if ($commandSetNames.Count -eq 0) { $commandSetNames = @("RevitMCPCommandSet", "OffAxisCommandSet") }

$cbVarNames = @()
foreach ($n in $commandSetNames) {
    $safe = $n -replace '[^A-Za-z0-9]', ''
    $cbVarNames += $safe
}
$issLines += 'var SetCBs: array[' + '0..' + ($commandSetNames.Count - 1) + '] of TNewCheckBox;'
$issLines += ''
$issLines += 'function GetAppDataDir: String;'
$issLines += 'begin'
$issLines += '  Result := GetEnv(''APPDATA'');'
$issLines += 'end;'
$issLines += ''
$issLines += 'function GetUserProfilePath: String;'
$issLines += 'begin'
$issLines += '  Result := GetEnv(''USERPROFILE'');'
$issLines += 'end;'
$issLines += ''
$issLines += 'function GetOpenCodeConfigDir: String;'
$issLines += 'var D: String;'
$issLines += 'begin'
$issLines += '  D := GetEnv(''OPENCODE_CONFIG_DIR'');'
$issLines += '  if D <> '''' then Result := D'
$issLines += '  else begin'
$issLines += '    D := GetEnv(''XDG_CONFIG_HOME'');'
$issLines += '    if D <> '''' then Result := D + ''\opencode'''
$issLines += '    else Result := GetUserProfilePath + ''\.config\opencode'';'
$issLines += '  end;'
$issLines += 'end;'
$issLines += ''
$issLines += 'function DetectOpenCode: Boolean;'
$issLines += 'var RC: Integer; Script, OutFile, Line, Key, Value: String; SL: TStringList; I: Integer;'
$issLines += 'begin'
$issLines += '  Result := False;'
$issLines += '  OpenCodeInstalled := False;'
$issLines += '  OpenCodeVersion := '''';'
$issLines += '  OpenCodeSource := ''none'';'
$issLines += '  OpenCodeConfigShape := ''none'';'
$issLines += '  try'
$issLines += '    ExtractTemporaryFile(''opencode-config.ps1'');'
$issLines += '  except'
$issLines += '    Log(''WARNING: could not extract opencode detection helper'');'
$issLines += '    Exit;'
$issLines += '  end;'
$issLines += '  Script := ExpandConstant(''{tmp}\opencode-config.ps1'');'
$issLines += '  OutFile := ExpandConstant(''{tmp}\opencode-detect.txt'');'
$issLines += '  if FileExists(OutFile) then DeleteFile(OutFile);'
$issLines += '  if Exec(''powershell.exe'', ''-NoProfile -ExecutionPolicy Bypass -File "'' + Script + ''" -Action Detect -OutputPath "'' + OutFile + ''"'', '''', SW_HIDE, ewWaitUntilTerminated, RC) then begin'
$issLines += '    Log(''opencode detection helper exit code: '' + IntToStr(RC));'
$issLines += '    if (RC = 0) and FileExists(OutFile) then begin'
$issLines += '      SL := TStringList.Create;'
$issLines += '      try'
$issLines += '        SL.LoadFromFile(OutFile);'
$issLines += '        Log(''opencode detection output lines: '' + IntToStr(SL.Count));'
$issLines += '        for I := 0 to SL.Count - 1 do begin'
$issLines += '          Line := SL[I];'
$issLines += '          Log(''opencode detect line: '' + Line);'
$issLines += '          if Pos(''='', Line) > 0 then begin'
$issLines += '            Key := Copy(Line, 1, Pos(''='', Line) - 1);'
$issLines += '            Value := Copy(Line, Pos(''='', Line) + 1, Length(Line));'
$issLines += '            if Key = ''installed'' then OpenCodeInstalled := Value = ''1'''
$issLines += '            else if Key = ''version'' then OpenCodeVersion := Value'
$issLines += '            else if Key = ''source'' then OpenCodeSource := Value'
$issLines += '            else if Key = ''config'' then OpenCodeConfigShape := Value;'
$issLines += '          end;'
$issLines += '        end;'
$issLines += '      finally SL.Free; end;'
$issLines += '      Result := True;'
$issLines += '    end else'
$issLines += '      Log(''WARNING: opencode detection output missing or helper failed: '' + OutFile);'
$issLines += '  end else'
$issLines += '    Log(''WARNING: failed to launch opencode detection helper'');'
$issLines += '  if OpenCodeInstalled then'
$issLines += '    Log(''opencode detected: version='' + OpenCodeVersion + '' source='' + OpenCodeSource + '' config='' + OpenCodeConfigShape)'
$issLines += '  else'
$issLines += '    Log(''opencode not detected'');'
$issLines += 'end;'
$issLines += ''
$issLines += 'function RunOpenCodeConfig(ActionName: String; NativeMode: Boolean): Integer;'
$issLines += 'var RC: Integer; Params, Helper, Shim, Dir: String;'
$issLines += 'begin'
$issLines += '  Result := 1;'
$issLines += '  Helper := ExpandConstant(''{app}\Installer\opencode-config.ps1'');'
$issLines += '  if not FileExists(Helper) then begin'
$issLines += '    Log(''WARNING: opencode config helper not found: '' + Helper);'
$issLines += '    Exit;'
$issLines += '  end;'
$issLines += '  Dir := GetOpenCodeConfigDir;'
$issLines += '  Shim := ExpandConstant(''{app}\Server\run-mcp-server.cmd'');'
$issLines += '  Params := ''-NoProfile -ExecutionPolicy Bypass -File "'' + Helper + ''" -Action '' + ActionName + '' -ConfigDir "'' + Dir + ''" -ShimPath "'' + Shim + ''" -LogPath "'' + ExpandConstant(''{tmp}\opencode-config.log'') + ''"'';'
$issLines += '  if NativeMode then Params := Params + '' -Native'';'
$issLines += '  if Exec(''powershell.exe'', Params, '''', SW_HIDE, ewWaitUntilTerminated, RC) then begin'
$issLines += '    Result := RC;'
$issLines += '    Log(''opencode config helper exit code: '' + IntToStr(RC));'
$issLines += '  end else'
$issLines += '    Log(''WARNING: failed to launch opencode config helper'');'
$issLines += 'end;'
$issLines += ''
$issLines += 'function IsRevitInstalled(Y: String): Boolean;'
$issLines += 'begin'
$issLines += '  Result := RegKeyExists(HKLM, RevitRegKey + Y) or RegKeyExists(HKCU, RevitRegKey + Y);'
$issLines += 'end;'
$issLines += ''
$issLines += 'procedure PruneCommandSets(Src: String);'
$issLines += 'var CmdDir: String; I: Integer;'
$issLines += 'begin'
$issLines += '  CmdDir := Src + ''\\revit_mcp_plugin\\Commands'';'
$issLines += '  if not DirExists(CmdDir) then Exit;'
$issLines += '  for I := 0 to High(SetCBs) do begin'
$issLines += '    if not SetCBs[I].Checked then begin'
$issLines += "      DelTree(CmdDir + '\\' + SetCBs[I].Caption, True, True, True);"
$issLines += "      Log('Removed unselected command set: ' + SetCBs[I].Caption);"
$issLines += '    end;'
$issLines += '  end;'
$issLines += 'end;'
$issLines += ''
$issLines += '// Assemble Commands\commandRegistry.json from the registry-entries.json'
$issLines += '// fragments of the command sets the user kept selected. Skipped when the'
$issLines += '// user already has a registry (their opt-ins are preserved).'
$issLines += 'procedure WriteCommandRegistry(Year: String);'
$issLines += 'var D, P, F, S, Entry: String; I: Integer; First: Boolean; Ch: Char; SL: TStringList;'
$issLines += 'begin'
$issLines += '  D := GetAppDataDir + ''\\Autodesk\\Revit\\Addins\\'' + Year + ''\\revit_mcp_plugin\\Commands'';'
$issLines += '  if not DirExists(D) then Exit;'
$issLines += '  P := D + ''\\commandRegistry.json'';'
$issLines += '  if FileExists(P) then Exit;'
$issLines += '  S := ''{"commands": ['';'
$issLines += '  First := True;'
$issLines += '  for I := 0 to High(SetCBs) do'
$issLines += '  begin'
$issLines += '    if not SetCBs[I].Checked then Continue;'
$issLines += '    F := D + ''\\'' + SetCBs[I].Caption + ''\\registry-entries.json'';'
$issLines += '    if not FileExists(F) then Continue;'
$issLines += '    SL := TStringList.Create;'
$issLines += '    try'
$issLines += '      SL.LoadFromFile(F);'
$issLines += '      Entry := SL.Text;'
$issLines += '    finally SL.Free; end;'
$issLines += '    if (Length(Entry) >= 2) and (Entry[1] = ''['') then Delete(Entry, 1, 1);'
$issLines += '    while Length(Entry) > 0 do'
$issLines += '    begin'
$issLines += '      Ch := Entry[Length(Entry)];'
$issLines += '      if (Ch = '']'') or (Ch = #13) or (Ch = #10) then'
$issLines += '        Delete(Entry, Length(Entry), 1)'
$issLines += '      else'
$issLines += '        Break;'
$issLines += '    end;'
$issLines += '    if Entry = '''' then Continue;'
$issLines += '    if not First then S := S + '','';'
$issLines += '    S := S + Entry;'
$issLines += '    First := False;'
$issLines += '  end;'
$issLines += "  S := S + ']}';"
$issLines += '  SaveStringToFile(P, S, False);'
$issLines += "  Log('Seeded command registry for ' + Year + ' from surviving set fragments');"
$issLines += 'end;'
$issLines += ''
$issLines += 'procedure CopyToRevitAddins(Year: String);'
$issLines += 'var Src, Dst: String; BAT: TStringList; RC: Integer; HadReg: Boolean;'
$issLines += 'begin'
$issLines += '  Src := ExpandConstant(''{app}\\RevitPlugins\\'' + Year);'
$issLines += '  Dst := GetAppDataDir + ''\\Autodesk\\Revit\\Addins\\'' + Year;'
$issLines += '  if not DirExists(Src) then begin Log(''Missing: '' + Src); Exit; end;'
$issLines += '  PruneCommandSets(Src);'
$issLines += '  ForceDirectories(Dst);'
$issLines += '  // Preserve an existing command registry (user opt-ins) across reinstalls:'
$issLines += '  // back it up before xcopy, restore after. Fresh installs get a registry'
$issLines += '  // assembled from the surviving sets'' fragments (all commands disabled).'
$issLines += '  HadReg := FileExists(Dst + ''\\revit_mcp_plugin\\Commands\\commandRegistry.json'');'
$issLines += '  if HadReg then'
$issLines += '    if not CopyFile(Dst + ''\\revit_mcp_plugin\\Commands\\commandRegistry.json'', ExpandConstant(''{tmp}\\cmdreg_'' + Year + ''.json''), False) then HadReg := False;'
$issLines += '  BAT := TStringList.Create;'
$issLines += '  try'
$issLines += '    BAT.Add(''@echo off'');'
$issLines += '    BAT.Add(''xcopy "'' + Src + ''\\*" "'' + Dst + ''\\" /E /I /Y /Q'');'
$issLines += '    BAT.SaveToFile(ExpandConstant(''{tmp}\\copy_'' + Year + ''.bat''));'
$issLines += '  finally BAT.Free; end;'
$issLines += '  Exec(''cmd'', ''/c "'' + ExpandConstant(''{tmp}\\copy_'' + Year + ''.bat'') + ''"'', '''', SW_HIDE, ewWaitUntilTerminated, RC);'
$issLines += '  if HadReg then'
$issLines += '    CopyFile(ExpandConstant(''{tmp}\\cmdreg_'' + Year + ''.json''), Dst + ''\\revit_mcp_plugin\\Commands\\commandRegistry.json'', False);'
$issLines += '  if not HadReg then WriteCommandRegistry(Year);'
$issLines += '  Log(''Copied plugin to '' + Dst);'
$issLines += 'end;'
$issLines += ''
$issLines += 'procedure RemoveFromRevitAddins(Year: String);'
$issLines += 'var D: String;'
$issLines += 'begin'
$issLines += '  D := GetAppDataDir + ''\\Autodesk\\Revit\\Addins\\'' + Year;'
$issLines += '  if FileExists(D + ''\\mcp-servers-for-revit.addin'') then DeleteFile(D + ''\\mcp-servers-for-revit.addin'');'
$issLines += '  if DirExists(D + ''\\revit_mcp_plugin'') then DelTree(D + ''\\revit_mcp_plugin'', True, True, True);'
$issLines += 'end;'
$issLines += ''
$issLines += 'procedure CleanTrailingCommas(var C: String);'
$issLines += 'var I, J: Integer;'
$issLines += 'begin'
$issLines += '  for I := 1 to Length(C) do begin'
$issLines += '    if C[I] = '','' then begin'
$issLines += '      J := I + 1;'
$issLines += '      while (J <= Length(C)) and ((C[J] = '' '') or (C[J] = #9) or (C[J] = #13) or (C[J] = #10)) do Inc(J);'
$issLines += '      if (J <= Length(C)) and ((C[J] = ''}'') or (C[J] = '']'')) then C[I] := '' '';'
$issLines += '    end;'
$issLines += '  end;'
$issLines += 'end;'
$issLines += ''
$issLines += '// Structural JSON sanity for the four AI-client configs. The Pascal'
$issLines += '// runtime has no JSON parser, so this checks the failure modes string'
$issLines += '// surgery can actually produce: unbalanced braces (ignoring quoted'
$issLines += '// values with backslash escapes), duplicate-key prefixes ("k": "k": {),'
$issLines += '// and doubled commas.'
$issLines += 'function ConfigParseOK(C: String): Boolean;'
$issLines += 'var I, Depth: Integer; S, Bad: String; InQ, Esc: Boolean;'
$issLines += 'begin'
$issLines += '  Result := False;'
$issLines += '  S := Trim(C);'
$issLines += '  if (S = '''') or (S[1] <> ''{'') or (S[Length(S)] <> ''}'') then Exit;'
$issLines += '  Bad := ServerKey + ''": "'' + ServerKey;'
$issLines += '  if Pos(Bad, S) > 0 then Exit;'
$issLines += '  if Pos('',,'', S) > 0 then Exit;'
$issLines += '  InQ := False; Esc := False; Depth := 0;'
$issLines += '  for I := 1 to Length(S) do begin'
$issLines += '    if Esc then begin Esc := False; Continue; end;'
$issLines += '    if InQ then begin'
$issLines += '      if S[I] = ''\'' then Esc := True'
$issLines += '      else if S[I] = ''"'' then InQ := False;'
$issLines += '      Continue;'
$issLines += '    end;'
$issLines += '    if S[I] = ''"'' then begin InQ := True; Continue; end;'
$issLines += '    if S[I] = ''{'' then Depth := Depth + 1'
$issLines += '    else if S[I] = ''}'' then begin'
$issLines += '      Depth := Depth - 1;'
$issLines += '      if Depth < 0 then Exit;'
$issLines += '    end;'
$issLines += '  end;'
$issLines += '  if (Depth <> 0) or InQ then Exit;'
$issLines += '  Result := True;'
$issLines += 'end;'
$issLines += ''
$issLines += '// Saves a mutated config only if it still parses as JSON (structurally).'
$issLines += '// On sanity failure the original file is left untouched, the reason is'
$issLines += '// logged, and a warning dialog is shown.'
$issLines += 'procedure GuardedSaveConfig(SL: TStringList; P, C, Client: String);'
$issLines += 'begin'
$issLines += '  CleanTrailingCommas(C);'
$issLines += '  if ConfigParseOK(C) then begin'
$issLines += '    SL.Text := C;'
$issLines += '    SL.SaveToFile(P);'
$issLines += '    Log(''Saved '' + Client + '' config: '' + P);'
$issLines += '  end else begin'
$issLines += '    Log(''WARNING: '' + Client + '' config edit rejected (would be invalid JSON); file left untouched: '' + P);'
$issLines += '    SuppressibleMsgBox(''Unable to update the '' + Client + '' AI-client config safely (the edit would produce invalid JSON). The existing file was left unchanged: '' + P + ''. Please contact the developer at https://github.com/chris4d/mcp-servers-for-revit/issues and include the installer log.'', mbError, MB_OK, IDOK);'
$issLines += '  end;'
$issLines += 'end;'
$issLines += ''
$issLines += '// Entry REMOVAL guard (uninstall path): same JSON sanity, generic messaging.'
$issLines += 'procedure GuardedRemoveConfig(SL: TStringList; P, C: String);'
$issLines += 'begin'
$issLines += '  CleanTrailingCommas(C);'
$issLines += '  if ConfigParseOK(C) then begin'
$issLines += '    SL.Text := C;'
$issLines += '    SL.SaveToFile(P);'
$issLines += '    Log(''Removed server entry from config: '' + P);'
$issLines += '  end else begin'
$issLines += '    Log(''WARNING: config removal rejected (would be invalid JSON); file left untouched: '' + P);'
$issLines += '    SuppressibleMsgBox(''Unable to remove the MCP server entry from an AI-client config safely. The existing file was left unchanged: '' + P + ''. Please contact the developer at https://github.com/chris4d/mcp-servers-for-revit/issues and include the installer log.'', mbError, MB_OK, IDOK);'
$issLines += '  end;'
$issLines += 'end;'
$issLines += ''
$issLines += '// Path of the stdio launcher shim for this install, JSON-escaped (backslashes doubled).';
$issLines += 'function ShimPathJson(): String;';
$issLines += 'begin';
$issLines += '  Result := ExpandConstant(''{app}\Server\run-mcp-server.cmd'');';
$issLines += '  StringChangeEx(Result, ''\'', ''\\'', True);';
$issLines += 'end;';
$issLines += ''
$issLines += 'procedure GetNodeEntry(var Entry: String);';
$issLines += 'begin';
$issLines += '  Entry := ''"mcp-server-for-revit": {'' + #13#10';
$issLines += '         + ''            "command": "cmd",'' + #13#10';
$issLines += '         + ''            "args": ["/c", "'' + ShimPathJson() + ''"]'' + #13#10';
$issLines += '         + ''        }'';';
$issLines += 'end;';
$issLines += ''
$issLines += 'procedure GetOpenCodeEntry(var Entry: String);';
$issLines += 'begin';
$issLines += '  Entry := ''"mcp-server-for-revit": {'' + #13#10';
$issLines += '         + ''            "type": "local",'' + #13#10';
$issLines += '         + ''            "command": ["cmd", "/c", "'' + ShimPathJson() + ''"],'' + #13#10';
$issLines += '         + ''            "enabled": true'' + #13#10';
$issLines += '         + ''        }'';';
$issLines += 'end;';
$issLines += ''
$issLines += 'procedure ReplaceNpxServerEntry(var C: String; OpenCodeStyle: Boolean);';
$issLines += 'var I, S, E, Depth: Integer; Block, Fresh: String;';
$issLines += 'begin';
$issLines += '  S := Pos(''"'' + ServerKey + ''"'', C);';
$issLines += '  if S = 0 then Exit;';
$issLines += '  E := 0; Depth := 0;';
$issLines += '  for I := S to Length(C) do begin';
$issLines += '    if C[I] = ''{'' then begin';
$issLines += '      Depth := Depth + 1;';
$issLines += '    end else if C[I] = ''}'' then begin';
$issLines += '      Depth := Depth - 1;';
$issLines += '      if Depth = 0 then begin E := I; Break; end;';
$issLines += '    end;';
$issLines += '  end;';
$issLines += '  if E = 0 then Exit;';
$issLines += '  Block := Copy(C, S, E - S + 1);';
$issLines += '  // Upgrade stale entry forms: npx-based (upstream era) and direct "node"';
$issLines += '  // launches (pre-bundled-runtime installs) both become shim-based.';
$issLines += '  if (Pos(''npx'', Block) = 0) and (Pos(''run-mcp-server'', Block) > 0) then Exit;';
$issLines += '  if OpenCodeStyle then GetOpenCodeEntry(Fresh) else GetNodeEntry(Fresh);';
$issLines += '  Delete(C, S, E - S + 1);';
$issLines += '  Insert(Fresh, C, S);';
$issLines += '  Log(''Refreshed server entry to bundled-runtime shim'');';
$issLines += 'end;';
$issLines += ''
$issLines += 'procedure ConfigureClaudeDesktop;'
$issLines += 'var P, C: String; SL: TStringList;'
$issLines += 'begin'
$issLines += '  P := GetAppDataDir + ''\\Claude\\claude_desktop_config.json'';'
$issLines += '  if not FileExists(P) then begin'
$issLines += '    ForceDirectories(ExtractFilePath(P));'
$issLines += '    SL := TStringList.Create;'
$issLines += '    try'
$issLines += '      SL.Add(''{'');'
$issLines += '      SL.Add(''    "mcpServers": {'');'
$issLines += '      SL.Add(''        "mcp-server-for-revit": {'');'
$issLines += '      SL.Add(''            "command": "cmd",'');'
$issLines += '      SL.Add(''            "args": ["/c", "'' + ShimPathJson() + ''"]'');'
$issLines += '      SL.Add(''        }'');'
$issLines += '      SL.Add(''    }'');'
$issLines += '      SL.Add(''}'');'
$issLines += '      GuardedSaveConfig(SL, P, SL.Text, ''Claude Desktop'');'
$issLines += '    finally SL.Free; end;'
$issLines += '    Log(''Created Claude Desktop config'');'
$issLines += '  end else begin'
$issLines += '    SL := TStringList.Create;'
$issLines += '    try'
$issLines += '      SL.LoadFromFile(P); C := SL.Text;'
$issLines += '      if Pos(ServerKey, C) > 0 then begin'
$issLines += '        ReplaceNpxServerEntry(C, False);'
$issLines += '        GuardedSaveConfig(SL, P, C, ''Claude Desktop'');'
$issLines += '      Exit;'
$issLines += '      end;'
$issLines += '      StringChangeEx(C, ''"mcpServers": {'','
$issLines += '        ''"mcpServers": {'' + #13#10 + ''        "mcp-server-for-revit": {'' + #13#10 + ''            "command": "cmd",'' + #13#10 + ''            "args": ["/c", "'' + ShimPathJson() + ''"]'' + #13#10 + ''        },'', True);'
$issLines += '      GuardedSaveConfig(SL, P, C, ''Claude Desktop'');'
$issLines += '    finally SL.Free; end;'
$issLines += '  end;'
$issLines += 'end;'
$issLines += ''
$issLines += 'procedure RemoveFromClaudeDesktop;'
$issLines += 'var P, C, Block: String; SL: TStringList; S, E: Integer;'
$issLines += 'begin'
$issLines += '  P := GetAppDataDir + ''\\Claude\\claude_desktop_config.json'';'
$issLines += '  if not FileExists(P) then Exit;'
$issLines += '  SL := TStringList.Create;'
$issLines += '  try'
$issLines += '    SL.LoadFromFile(P); C := SL.Text;'
$issLines += '    S := Pos(''"mcp-server-for-revit"'', C);'
$issLines += '    if S = 0 then Exit;'
$issLines += '    E := Pos(''}'', Copy(C, S, Length(C)));'
$issLines += '    if E = 0 then Exit;'
$issLines += '    E := S + E - 1;'
$issLines += '    if (E + 1 <= Length(C)) and (C[E + 1] = '','') then Inc(E);'
$issLines += '    Block := Copy(C, S, E - S + 1);'
$issLines += '    if Pos(ShimPathJson(), Block) = 0 then begin'
$issLines += '      Log(''Claude Desktop entry is not owned by this installer; left untouched'');'
$issLines += '      Exit;'
$issLines += '    end;'
$issLines += '    Delete(C, S, E - S + 1);'
$issLines += '    CleanTrailingCommas(C);'
$issLines += '    GuardedRemoveConfig(SL, P, C);'
$issLines += '  finally SL.Free; end;'
$issLines += 'end;'
$issLines += ''
$issLines += 'procedure ConfigureCursor;'
$issLines += 'var P, C: String; SL: TStringList;'
$issLines += 'begin'
$issLines += '  P := GetUserProfilePath + ''\\.cursor\\mcp.json'';'
$issLines += '  if not FileExists(P) then begin'
$issLines += '    ForceDirectories(ExtractFilePath(P));'
$issLines += '    SL := TStringList.Create;'
$issLines += '    try'
$issLines += '      SL.Add(''{'');'
$issLines += '      SL.Add(''    "mcpServers": {'');'
$issLines += '      SL.Add(''        "mcp-server-for-revit": {'');'
$issLines += '      SL.Add(''            "command": "cmd",'');'
$issLines += '      SL.Add(''            "args": ["/c", "'' + ShimPathJson() + ''"]'');'
$issLines += '      SL.Add(''        }'');'
$issLines += '      SL.Add(''    }'');'
$issLines += '      SL.Add(''}'');'
$issLines += '      GuardedSaveConfig(SL, P, SL.Text, ''Cursor'');'
$issLines += '    finally SL.Free; end;'
$issLines += '    Log(''Created Cursor config'');'
$issLines += '  end else begin'
$issLines += '    SL := TStringList.Create;'
$issLines += '    try'
$issLines += '      SL.LoadFromFile(P); C := SL.Text;'
$issLines += '      if Pos(ServerKey, C) > 0 then begin'
$issLines += '        ReplaceNpxServerEntry(C, False);'
$issLines += '        GuardedSaveConfig(SL, P, C, ''Cursor'');'
$issLines += '      Exit;'
$issLines += '      end;'
$issLines += '      StringChangeEx(C, ''"mcpServers": {'','
$issLines += '        ''"mcpServers": {'' + #13#10 + ''        "mcp-server-for-revit": {'' + #13#10 + ''            "command": "cmd",'' + #13#10 + ''            "args": ["/c", "'' + ShimPathJson() + ''"]'' + #13#10 + ''        },'', True);'
$issLines += '      GuardedSaveConfig(SL, P, C, ''Cursor'');'
$issLines += '    finally SL.Free; end;'
$issLines += '  end;'
$issLines += 'end;'
$issLines += ''
$issLines += 'procedure RemoveFromCursor;'
$issLines += 'var P, C, Block: String; SL: TStringList; S, E: Integer;'
$issLines += 'begin'
$issLines += '  P := GetUserProfilePath + ''\\.cursor\\mcp.json'';'
$issLines += '  if not FileExists(P) then Exit;'
$issLines += '  SL := TStringList.Create;'
$issLines += '  try'
$issLines += '    SL.LoadFromFile(P); C := SL.Text;'
$issLines += '    S := Pos(''"mcp-server-for-revit"'', C);'
$issLines += '    if S = 0 then Exit;'
$issLines += '    E := Pos(''}'', Copy(C, S, Length(C)));'
$issLines += '    if E = 0 then Exit;'
$issLines += '    E := S + E - 1;'
$issLines += '    if (E + 1 <= Length(C)) and (C[E + 1] = '','') then Inc(E);'
$issLines += '    Block := Copy(C, S, E - S + 1);'
$issLines += '    if Pos(ShimPathJson(), Block) = 0 then begin'
$issLines += '      Log(''Cursor entry is not owned by this installer; left untouched'');'
$issLines += '      Exit;'
$issLines += '    end;'
$issLines += '    Delete(C, S, E - S + 1);'
$issLines += '    CleanTrailingCommas(C);'
$issLines += '    GuardedRemoveConfig(SL, P, C);'
$issLines += '  finally SL.Free; end;'
$issLines += 'end;'
$issLines += ''
$issLines += 'procedure ConfigureAnythingLLM;'
$issLines += 'var P, C: String; SL: TStringList;'
$issLines += 'begin'
$issLines += '  P := GetAppDataDir + ''\\anythingllm-desktop\\storage\\plugins\\anythingllm_mcp_servers.json'';'
$issLines += '  if not FileExists(P) then begin'
$issLines += '    ForceDirectories(ExtractFilePath(P));'
$issLines += '    SL := TStringList.Create;'
$issLines += '    try'
$issLines += '      SL.Add(''{'');'
$issLines += '      SL.Add(''    "mcpServers": {'');'
$issLines += '      SL.Add(''        "mcp-server-for-revit": {'');'
$issLines += '      SL.Add(''            "command": "cmd",'');'
$issLines += '      SL.Add(''            "args": ["/c", "'' + ShimPathJson() + ''"]'');'
$issLines += '      SL.Add(''        }'');'
$issLines += '      SL.Add(''    }'');'
$issLines += '      SL.Add(''}'');'
$issLines += '      GuardedSaveConfig(SL, P, SL.Text, ''AnythingLLM'');'
$issLines += '    finally SL.Free; end;'
$issLines += '    Log(''Created AnythingLLM config'');'
$issLines += '  end else begin'
$issLines += '    SL := TStringList.Create;'
$issLines += '    try'
$issLines += '      SL.LoadFromFile(P); C := SL.Text;'
$issLines += '      if Pos(ServerKey, C) > 0 then begin'
$issLines += '        ReplaceNpxServerEntry(C, False);'
$issLines += '        GuardedSaveConfig(SL, P, C, ''AnythingLLM'');'
$issLines += '      Exit;'
$issLines += '      end;'
$issLines += '      StringChangeEx(C, ''"mcpServers": {'','
$issLines += '        ''"mcpServers": {'' + #13#10 + ''        "mcp-server-for-revit": {'' + #13#10 + ''            "command": "cmd",'' + #13#10 + ''            "args": ["/c", "'' + ShimPathJson() + ''"]'' + #13#10 + ''        },'', True);'
$issLines += '      GuardedSaveConfig(SL, P, C, ''AnythingLLM'');'
$issLines += '    finally SL.Free; end;'
$issLines += '  end;'
$issLines += 'end;'
$issLines += ''
$issLines += 'procedure RemoveFromAnythingLLM;'
$issLines += 'var P, C, Block: String; SL: TStringList; S, E: Integer;'
$issLines += 'begin'
$issLines += '  P := GetAppDataDir + ''\\anythingllm-desktop\\storage\\plugins\\anythingllm_mcp_servers.json'';'
$issLines += '  if not FileExists(P) then Exit;'
$issLines += '  SL := TStringList.Create;'
$issLines += '  try'
$issLines += '    SL.LoadFromFile(P); C := SL.Text;'
$issLines += '    S := Pos(''"mcp-server-for-revit"'', C);'
$issLines += '    if S = 0 then Exit;'
$issLines += '    E := Pos(''}'', Copy(C, S, Length(C)));'
$issLines += '    if E = 0 then Exit;'
$issLines += '    E := S + E - 1;'
$issLines += '    if (E + 1 <= Length(C)) and (C[E + 1] = '','') then Inc(E);'
$issLines += '    Block := Copy(C, S, E - S + 1);'
$issLines += '    if Pos(ShimPathJson(), Block) = 0 then begin'
$issLines += '      Log(''AnythingLLM entry is not owned by this installer; left untouched'');'
$issLines += '      Exit;'
$issLines += '    end;'
$issLines += '    Delete(C, S, E - S + 1);'
$issLines += '    CleanTrailingCommas(C);'
$issLines += '    GuardedRemoveConfig(SL, P, C);'
$issLines += '  finally SL.Free; end;'
$issLines += 'end;'
$issLines += ''
$issLines += 'procedure ConfigureOpencode;'
$issLines += 'var RC: Integer; NativeMode: Boolean;'
$issLines += 'begin'
$issLines += '  if OpenCode2CB <> nil then NativeMode := OpenCode2CB.Checked'
$issLines += '  else if Pos(''2.'', OpenCodeVersion) = 1 then NativeMode := True'
$issLines += '  else if Pos(''1.'', OpenCodeVersion) = 1 then NativeMode := False'
$issLines += '  else NativeMode := OpenCodeConfigShape <> ''legacy'';'
$issLines += '  RC := RunOpenCodeConfig(''Install'', NativeMode);'
$issLines += '  if RC <> 0 then'
$issLines += '    SuppressibleMsgBox(''Unable to update the opencode config automatically. See the installer log and configure the MCP server manually in '' + GetOpenCodeConfigDir, mbError, MB_OK, IDOK);'
$issLines += 'end;'
$issLines += ''
$issLines += 'procedure RemoveFromOpencode;'
$issLines += 'var RC: Integer;'
$issLines += 'begin'
$issLines += '  RC := RunOpenCodeConfig(''Remove'', False);'
$issLines += '  if RC <> 0 then Log(''WARNING: opencode config removal failed; manual cleanup may be needed'');'
$issLines += 'end;'
$issLines += ''
$issLines += 'procedure CreateCommandSetPage;'
$issLines += 'var I: Integer;'
$issLines += '  SetLabel: TNewStaticText;'
$issLines += '  Names: array[' + '0..' + ($commandSetNames.Count - 1) + '] of String;'
$issLines += 'begin'
$issLines += '  SetPage := CreateCustomPage(wpSelectDir, ''Command Sets'', ''Select which Command Sets to install:'');'
$issLines += ''
$issLines += '  SetLabel := TNewStaticText.Create(WizardForm);'
$issLines += '  SetLabel.Parent := SetPage.Surface;'
$issLines += '  SetLabel.SetBounds(ScaleX(20), ScaleY(10), ScaleX(400), ScaleY(20));'
$issLines += "  SetLabel.Caption := 'All command sets are selected by default. Uncheck any you do not want:';"
$issLines += '  SetLabel.Font.Style := [fsBold];'
$issLines += ''
foreach ($i in 0..($commandSetNames.Count - 1)) {
    $issLines += "  Names[$i] := '$($commandSetNames[$i])';"
}
$issLines += ''
$issLines += '  for I := 0 to High(SetCBs) do begin'
$issLines += '    SetCBs[I] := TNewCheckBox.Create(WizardForm);'
$issLines += '    SetCBs[I].Parent := SetPage.Surface;'
$issLines += '    SetCBs[I].SetBounds(ScaleX(20), ScaleY(40 + I * 30), ScaleX(600), ScaleY(20));'
$issLines += '    SetCBs[I].Caption := Names[I];'
$issLines += '    SetCBs[I].Checked := True;'
$issLines += '  end;'
$issLines += 'end;'
$issLines += ''
$issLines += 'procedure CreateClientPage;'
$issLines += 'var Label1: TNewStaticText;'
$issLines += 'begin'
$issLines += '  Page := CreateCustomPage(wpSelectDir, ''AI Client Configuration'', ''Select which AI clients to configure for MCP server access:'');'
$issLines += ''
$issLines += '  Label1 := TNewStaticText.Create(WizardForm);'
$issLines += '  Label1.Parent := Page.Surface;'
$issLines += '  Label1.SetBounds(ScaleX(20), ScaleY(10), ScaleX(400), ScaleY(20));'
$issLines += '  Label1.Caption := ''Detected clients are pre-checked:'';'
$issLines += '  Label1.Font.Style := [fsBold];'
$issLines += ''
$issLines += '  ClaudeCB := TNewCheckBox.Create(WizardForm);'
$issLines += '  ClaudeCB.Parent := Page.Surface;'
$issLines += '  ClaudeCB.SetBounds(ScaleX(20), ScaleY(40), ScaleX(400), ScaleY(20));'
$issLines += '  ClaudeCB.Caption := ''Claude Desktop ('' + GetAppDataDir + ''\Claude\)'';'
$issLines += '  ClaudeCB.Checked := FileExists(GetAppDataDir + ''\\Claude\\claude_desktop_config.json'');'
$issLines += ''
$issLines += '  CursorCB := TNewCheckBox.Create(WizardForm);'
$issLines += '  CursorCB.Parent := Page.Surface;'
$issLines += '  CursorCB.SetBounds(ScaleX(20), ScaleY(65), ScaleX(400), ScaleY(20));'
$issLines += '  CursorCB.Caption := ''Cursor ('' + GetUserProfilePath + ''\.cursor\)'';'
$issLines += '  CursorCB.Checked := DirExists(GetUserProfilePath + ''\\.cursor'');'
$issLines += ''
$issLines += '  OpencodeCB := TNewCheckBox.Create(WizardForm);'
$issLines += '  OpencodeCB.Parent := Page.Surface;'
$issLines += '  OpencodeCB.SetBounds(ScaleX(20), ScaleY(90), ScaleX(600), ScaleY(20));'
$issLines += '  if OpenCodeInstalled then begin'
$issLines += '    if OpenCodeVersion <> '''' then OpencodeCB.Caption := ''opencode '' + OpenCodeVersion + '' ('' + OpenCodeSource + '')'''
$issLines += '    else OpencodeCB.Caption := ''opencode (detected, version unknown)'';'
$issLines += '  end else'
$issLines += '    OpencodeCB.Caption := ''opencode (not detected - check to force)'';'
$issLines += '  OpencodeCB.Checked := OpenCodeInstalled;'
$issLines += ''
$issLines += '  OpenCode2CB := TNewCheckBox.Create(WizardForm);'
$issLines += '  OpenCode2CB.Parent := Page.Surface;'
$issLines += '  OpenCode2CB.SetBounds(ScaleX(40), ScaleY(112), ScaleX(560), ScaleY(20));'
$issLines += '  OpenCode2CB.Caption := ''Use OpenCode 2 native config (mcp.servers, codemode: false)'';'
$issLines += '  OpenCode2CB.Checked := False;'
$issLines += '  OpenCode2CB.Visible := False;'
$issLines += '  if OpenCodeInstalled then begin'
$issLines += '    if OpenCodeVersion <> '''' then begin'
$issLines += '      if Pos(''2.'', OpenCodeVersion) = 1 then OpenCode2CB.Checked := True'
$issLines += '      else if Pos(''1.'', OpenCodeVersion) = 1 then OpenCode2CB.Checked := False'
$issLines += '      else begin'
$issLines += '        OpenCode2CB.Visible := True;'
$issLines += '        OpenCode2CB.Caption := ''OpenCode version unknown: check for OpenCode 2, uncheck for OpenCode 1'';'
$issLines += '        OpenCode2CB.Checked := OpenCodeConfigShape <> ''legacy'';'
$issLines += '      end;'
$issLines += '    end else begin'
$issLines += '      OpenCode2CB.Visible := True;'
$issLines += '      OpenCode2CB.Caption := ''OpenCode version unknown: check for OpenCode 2, uncheck for OpenCode 1'';'
$issLines += '      OpenCode2CB.Checked := OpenCodeConfigShape <> ''legacy'';'
$issLines += '    end;'
$issLines += '  end;'
$issLines += ''
$issLines += '  AnythingLLMCB := TNewCheckBox.Create(WizardForm);'
$issLines += '  AnythingLLMCB.Parent := Page.Surface;'
$issLines += '  if OpenCode2CB.Visible then'
$issLines += '    AnythingLLMCB.SetBounds(ScaleX(20), ScaleY(140), ScaleX(400), ScaleY(20))'
$issLines += '  else'
$issLines += '    AnythingLLMCB.SetBounds(ScaleX(20), ScaleY(115), ScaleX(400), ScaleY(20));'
$issLines += '  AnythingLLMCB.Caption := ''AnythingLLM ('' + GetAppDataDir + ''\anythingllm-desktop\)'';'
$issLines += '  AnythingLLMCB.Checked := DirExists(GetAppDataDir + ''\\anythingllm-desktop'');'
$issLines += 'end;'
$issLines += ''
$issLines += 'procedure InitializeWizard;'
$issLines += 'begin'
$issLines += '  DetectOpenCode;'
$issLines += '  CreateCommandSetPage;'
$issLines += '  CreateClientPage;'
$issLines += 'end;'
$issLines += ''
$issLines += 'function NextButtonClick(Page: Integer): Boolean;'
$issLines += 'begin'
$issLines += '  Result := True;'
$issLines += 'end;'
$issLines += ''
$issLines += 'procedure ConfigureDetectedClients;'
$issLines += 'begin'
$issLines += '  if FileExists(GetAppDataDir + ''\\Claude\\claude_desktop_config.json'') or DirExists(GetAppDataDir + ''\\Claude'') then ConfigureClaudeDesktop;'
$issLines += '  if DirExists(GetUserProfilePath + ''\\.cursor'') then ConfigureCursor;'
$issLines += '  if OpenCodeInstalled then ConfigureOpencode;'
$issLines += '  if DirExists(GetAppDataDir + ''\\anythingllm-desktop'') then ConfigureAnythingLLM;'
$issLines += 'end;'
$issLines += ''
$issLines += 'procedure CurStepChanged(CurStep: TSetupStep);'
$issLines += 'var AnyChecked: Boolean;'
$issLines += 'begin'
$issLines += '  if CurStep = ssPostInstall then begin'
foreach ($year in $builtVersions) {
    $issLines += "    if IsRevitInstalled('$year') then CopyToRevitAddins('$year');"
}
$issLines += '    AnyChecked := ClaudeCB.Checked or CursorCB.Checked or OpencodeCB.Checked or AnythingLLMCB.Checked;'
$issLines += '    if not AnyChecked then ConfigureDetectedClients'
$issLines += '    else begin'
$issLines += '      if ClaudeCB.Checked then ConfigureClaudeDesktop;'
$issLines += '      if CursorCB.Checked then ConfigureCursor;'
$issLines += '      if OpencodeCB.Checked then ConfigureOpencode;'
$issLines += '      if AnythingLLMCB.Checked then ConfigureAnythingLLM;'
$issLines += '    end;'
$issLines += '    SuppressibleMsgBox(''Installation complete.'' #13#10 #13#10 ''Commands are disabled by default for your control. To expose model commands to AI clients:'' #13#10 ''1. In Revit, open the plugin Settings page'' #13#10 ''2. Enable the commands you want, then click Save'' #13#10 ''3. Click the Revit MCP Switch to start the server'', mbInformation, MB_OK, IDOK);'
$issLines += '  end;'
$issLines += 'end;'
$issLines += ''
$issLines += 'procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);'
$issLines += 'var I: Integer;'
$issLines += 'begin'
$issLines += '  if CurUninstallStep = usUninstall then begin'
$issLines += '    for I := 2020 to 2026 do RemoveFromRevitAddins(IntToStr(I));'
$issLines += '    RemoveFromClaudeDesktop;'
$issLines += '    RemoveFromCursor;'
$issLines += '    RemoveFromOpencode;'
$issLines += '    RemoveFromAnythingLLM;'
$issLines += '  end;'
$issLines += 'end;'

$issPath = Join-Path $InstallerDir "mcp-servers-for-revit.iss"
[System.IO.File]::WriteAllText($issPath, ($issLines -join "`r`n"), (New-Object System.Text.UTF8Encoding $false))
Write-Host "Generated: $issPath" -ForegroundColor Green

# ---------------------------------------------------------------
# Step 4: Compile
# ---------------------------------------------------------------
Write-Host "`n=== Compiling installer ===" -ForegroundColor Cyan

$ISCC = $null
@(
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
    "C:\Program Files\Inno Setup 6\ISCC.exe"
) | ForEach-Object { if (Test-Path $_) { $ISCC = $_ } }

if (-not $ISCC) { throw "ISCC.exe not found" }

New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null

Push-Location $InstallerDir
try {
    & $ISCC "mcp-servers-for-revit.iss"
    if ($LASTEXITCODE -ne 0) { throw "ISCC failed: $LASTEXITCODE" }
} finally { Pop-Location }

$exe = Get-ChildItem $OutputDir -Filter "*.exe" -ErrorAction SilentlyContinue | Select-Object -First 1
if ($exe) {
    Write-Host "`nInstaller: $($exe.FullName)" -ForegroundColor Green
    Write-Host "Size: $([math]::Round($exe.Length / 1MB, 2)) MB" -ForegroundColor Green
} else {
    Write-Host "Warning: .exe not found" -ForegroundColor Yellow
}