# Wall scorecard: area-symmetry (IoU-wall) metric between a target layout and a
# created-wall layout. Both inputs are JSON arrays of walls:
#   [{"id":1,"sx":0,"sy":0,"ex":10,"ey":0,"w":0.83}, ...]
# Coordinates and widths are in FEET (Revit internal units).
#
# Metric: each wall is a rectangle (centerline x width). For near-parallel pairs
# the intersection area is exact (thickness overlap x span overlap); for
# crossing pairs it is a capped approximation (T-join noise, accepted).
# Matching is greedy best-first on intersection area. Then:
#   precision = sumInt / sumAreaCreated
#   recall    = sumInt / sumAreaTargets
#   F1        = harmonic mean
# Usage: .\score-walls.ps1 -Targets golden-targets.json -Created run1-created.json

param(
    [Parameter(Mandatory = $true)][string]$Targets,
    [Parameter(Mandatory = $true)][string]$Created,
    # Shift applied to TARGETS to bring them into the created-walls coordinate
    # frame (e.g. -500 for the +500ft-moved reference layout).
    [double]$OffsetX = 0.0,
    [double]$OffsetY = 0.0
)

$ErrorActionPreference = "Stop"

function Convert-Wall($w) {
    $sx = [double]$w.sx; $sy = [double]$w.sy
    $ex = [double]$w.ex; $ey = [double]$w.ey
    $ww = [double]$w.w
    $dx = $ex - $sx; $dy = $ey - $sy
    $len = [math]::Sqrt($dx * $dx + $dy * $dy)
    if ($len -gt 0.0) { $ux = $dx / $len; $uy = $dy / $len } else { $ux = 0.0; $uy = 0.0 }
    [pscustomobject]@{
        id = $w.id; sx = $sx; sy = $sy; ex = $ex; ey = $ey; w = $ww
        len = $len; ux = $ux; uy = $uy; area = $len * $ww
    }
}

# Intersection of created wall $c against target wall $t (parallel approximation).
# Returns @{ area; perp; spanFrac }
function Get-Inter($t, $c) {
    $dx = $c.sx - $t.sx; $dy = $c.sy - $t.sy
    $along = $dx * $t.ux + $dy * $t.uy
    $px = $dx - $t.ux * $along; $py = $dy - $t.uy * $along
    $perp = [math]::Sqrt($px * $px + $py * $py)
    # Intersection band along the perpendicular axis: target spans [-wt/2,+wt/2],
    # created spans [perp-wc/2, perp+wc/2]. Overlap width is their intersection
    # (for perp=0 this is min(wt,wc) - NOT (wt+wc)/2, which is the union band).
    $loP = [math]::Max(-$t.w / 2.0, $perp - $c.w / 2.0)
    $hiP = [math]::Min($t.w / 2.0, $perp + $c.w / 2.0)
    $thickOv = $hiP - $loP
    if ($thickOv -le 0.0 -or $t.len -le 0.0) {
        return @{ area = 0.0; perp = $perp; spanFrac = 0.0 }
    }
    $s1 = $along
    $s2 = $along + (($c.ex - $c.sx) * $t.ux + ($c.ey - $c.sy) * $t.uy)
    $lo = [math]::Max([math]::Min($s1, $s2), 0.0)
    $hi = [math]::Min([math]::Max($s1, $s2), $t.len)
    $covLen = $hi - $lo
    if ($covLen -le 0.0) {
        return @{ area = 0.0; perp = $perp; spanFrac = 0.0 }
    }
    $area = [math]::Min($thickOv * $covLen, [math]::Min($t.area, $c.area))
    @{ area = $area; perp = $perp; spanFrac = ($covLen / $t.len) }
}

$rawT = Get-Content $Targets -Raw | ConvertFrom-Json
$tList = New-Object System.Collections.Generic.List[object]
foreach ($item in $rawT) { $item.sx = [double]$item.sx + $OffsetX; $item.ex = [double]$item.ex + $OffsetX; $item.sy = [double]$item.sy + $OffsetY; $item.ey = [double]$item.ey + $OffsetY; $tList.Add((Convert-Wall $item)) }
$rawC = Get-Content $Created -Raw | ConvertFrom-Json
$cList = New-Object System.Collections.Generic.List[object]
foreach ($item in $rawC) { $cList.Add((Convert-Wall $item)) }

if ($tList.Count -eq 0) { Write-Output "no targets loaded"; exit 1 }

# --- candidate pair matrix (area > 0 only) ---
$pairs = New-Object System.Collections.Generic.List[object]
for ($ti = 0; $ti -lt $tList.Count; $ti++) {
    for ($ci = 0; $ci -lt $cList.Count; $ci++) {
        $inter = Get-Inter $tList[$ti] $cList[$ci]
        if ($inter.area -gt 0.0001) {
            $pairs.Add([pscustomobject]@{
                ti = $ti; ci = $ci; area = $inter.area
                perp = $inter.perp; spanFrac = $inter.spanFrac
            })
        }
    }
}

# --- greedy best-first assignment ---
$assigned = @{}
$sorted = $pairs | Sort-Object -Property area -Descending
foreach ($p in $sorted) {
    if ($assigned.ContainsKey("t$($p.ti)") -or $assigned.ContainsKey("c$($p.ci)")) { continue }
    $assigned["t$($p.ti)"] = $p
    $assigned["c$($p.ci)"] = $p
}

# --- aggregate metric ---
$sumAreaT = ($tList | Measure-Object -Property area -Sum).Sum
$sumAreaC = 0.0
if ($cList.Count -gt 0) { $sumAreaC = ($cList | Measure-Object -Property area -Sum).Sum }
$sumInt = 0.0
foreach ($k in $assigned.Keys) {
    if ($k.StartsWith("t")) { $sumInt += $assigned[$k].area }
}
$precision = 0.0; $recall = 0.0; $f1 = 0.0
if ($sumAreaC -gt 0) { $precision = $sumInt / $sumAreaC }
if ($sumAreaT -gt 0) { $recall = $sumInt / $sumAreaT }
if (($precision + $recall) -gt 0) { $f1 = 2 * $precision * $recall / ($precision + $recall) }

Write-Output "================ WALL SCORECARD ================"
Write-Output ("targets : {0} walls, {1:N1} sq ft" -f $tList.Count, $sumAreaT)
Write-Output ("created : {0} walls, {1:N1} sq ft" -f $cList.Count, $sumAreaC)
Write-Output ("matched : {0} pairs, {1:N1} sq ft intersect" -f (($assigned.Keys | Where-Object { $_.StartsWith("t") }).Count), $sumInt)
Write-Output ("precision = {0:N4}   (false-positive area {1:N1} sq ft)" -f $precision, ($sumAreaC - $sumInt))
Write-Output ("recall    = {0:N4}   (false-negative area {1:N1} sq ft)" -f $recall, ($sumAreaT - $sumInt))
Write-Output ("F1        = {0:N4}" -f $f1)
Write-Output ("SUMMARY: F1={0:N4} P={1:N4} R={2:N4}" -f $f1, $precision, $recall)

# --- matched-pair quality ---
$matchedPerp = New-Object System.Collections.Generic.List[double]
$matchedSpan = New-Object System.Collections.Generic.List[double]
$matchedThick = New-Object System.Collections.Generic.List[double]
foreach ($k in $assigned.Keys) {
    if (-not $k.StartsWith("t")) { continue }
    $p = $assigned[$k]
    $matchedPerp.Add($p.perp)
    $matchedSpan.Add($p.spanFrac)
    $tt = $tList[$p.ti].w; $cc = $cList[$p.ci].w
    $matchedThick.Add([math]::Abs($tt - $cc))
}
if ($matchedPerp.Count -gt 0) {
    $mp = ($matchedPerp | Measure-Object -Average -Maximum)
    $ms = ($matchedSpan | Measure-Object -Average)
    $mt = ($matchedThick | Measure-Object -Average -Maximum)
    Write-Output ""
    Write-Output ("matched-pair quality: railOffset mean={0:N3} max={1:N3} ft | coverage mean={2:N2} | thickDelta mean={3:N2} max={4:N2} ft" -f `
        $mp.Average, $mp.Maximum, $ms.Average, $mt.Average, $mt.Maximum)
}

# --- unmatched targets: taxonomy ---
$unmatched = @()
for ($ti = 0; $ti -lt $tList.Count; $ti++) {
    if (-not $assigned.ContainsKey("t$ti")) {
        $t = $tList[$ti]
        # best candidate diagnostics
        $bestSpan = 0.0; $bestPerp = 9.9
        foreach ($p in $pairs) {
            if ($p.ti -eq $ti) {
                if ($p.spanFrac -gt $bestSpan) { $bestSpan = $p.spanFrac }
            }
        }
        $reason = if ($bestSpan -ge 0.6) { "off-rail/overlap" } else { "absent/short-coverage" }
        $unmatched += [pscustomobject]@{
            id = $t.id; len = $t.len; area = $t.area; reason = $reason
            bestSpan = [math]::Round($bestSpan, 2)
            geo = ("({0:N1},{1:N1})-({2:N1},{3:N1}) L={4:N1} w={5:N2}" -f $t.sx, $t.sy, $t.ex, $t.ey, $t.len, $t.w)
        }
    }
}
$unArea = ($unmatched | Measure-Object -Property area -Sum).Sum
if ($unmatched.Count -gt 0) {
    Write-Output ""
    Write-Output ("unmatched targets: {0} (area {1:N1} sq ft)  by reason: absent/short={2} off-rail={3}" -f `
        $unmatched.Count, $unArea,
        ($unmatched | Where-Object { $_.reason -eq "absent/short-coverage" }).Count,
        ($unmatched | Where-Object { $_.reason -eq "off-rail/overlap" }).Count)
    $unmatched | Sort-Object -Property area -Descending | Select-Object -First 12 |
        ForEach-Object { "  [{0}] {1} (bestSpan={2})" -f $_.reason, $_.geo, $_.bestSpan }
}

# --- extras: unmatched created ---
$extras = @()
for ($ci = 0; $ci -lt $cList.Count; $ci++) {
    if (-not $assigned.ContainsKey("c$ci")) { $extras += $cList[$ci] }
}
$exArea = ($extras | Measure-Object -Property area -Sum).Sum
if ($extras.Count -gt 0) {
    Write-Output ""
    Write-Output ("extras (unmatched created): {0} walls, {1:N1} sq ft ({2:N0}% of created area)" -f `
        $extras.Count, $exArea, (100 * $exArea / $sumAreaC))
    $extras | Sort-Object -Property area -Descending | Select-Object -First 10 |
        ForEach-Object { "  ({0:N1},{1:N1})-({2:N1},{3:N1}) L={4:N1} w={5:N2}" -f $_.sx, $_.sy, $_.ex, $_.ey, $_.len, $_.w }
}

# --- union coverage diagnostic: how much of each target's rail is covered by
# --- ANY created wall (ignores fragmentation: two pieces covering one target
# --- count fully). If unionRecall >> recall, fragmentation is the main gap.
$unionLen = New-Object System.Collections.Generic.List[double]
for ($ti = 0; $ti -lt $tList.Count; $ti++) {
    $t = $tList[$ti]
    $ivals = New-Object System.Collections.Generic.List[object]
    foreach ($p in $pairs) {
        if ($p.ti -ne $ti) { continue }
        $c = $cList[$p.ci]
        $dx = $c.sx - $t.sx; $dy = $c.sy - $t.sy
        $along = $dx * $t.ux + $dy * $t.uy
        $px2 = $dx - $t.ux * $along; $py2 = $dy - $t.uy * $along
        if ([math]::Sqrt($px2 * $px2 + $py2 * $py2) -gt (($t.w + $c.w) / 2.0)) { continue }
        $s1 = $along
        $s2 = $along + (($c.ex - $c.sx) * $t.ux + ($c.ey - $c.sy) * $t.uy)
        $lo2 = [math]::Max([math]::Min($s1, $s2), 0.0)
        $hi2 = [math]::Min([math]::Max($s1, $s2), $t.len)
        if ($hi2 -gt $lo2) { $ivals.Add([pscustomobject]@{ lo = $lo2; hi = $hi2 }) }
    }
    if ($ivals.Count -eq 0) { $unionLen.Add(0.0); continue }
    $sortedIv = @($ivals | Sort-Object -Property lo)
    $cov = 0.0; $curLo = 0.0; $curHi = 0.0; $started = $false
    foreach ($iv in $sortedIv) {
        if (-not $started) { $started = $true; $curLo = $iv.lo; $curHi = $iv.hi }
        elseif ($iv.lo -le $curHi) { if ($iv.hi -gt $curHi) { $curHi = $iv.hi } }
        else { $cov += $curHi - $curLo; $curLo = $iv.lo; $curHi = $iv.hi }
    }
    if ($started) { $cov += $curHi - $curLo }
    $unionLen.Add($cov)
}
$unionArea = 0.0
for ($i = 0; $i -lt $tList.Count; $i++) { $unionArea += $unionLen[$i] * $tList[$i].w }
Write-Output ""
Write-Output ("union coverage (fragmentation-blind): {0:N4} of target area  [1x1 recall was {1:N4}]" -f ($unionArea / $sumAreaT), $recall)
$fullCov = 0; $noneCov = 0
for ($i = 0; $i -lt $tList.Count; $i++) {
    if ($tList[$i].len -le 0) { continue }
    $frac = $unionLen[$i] / $tList[$i].len
    if ($frac -ge 0.98) { $fullCov++ } elseif ($frac -le 0.02) { $noneCov++ }
}
Write-Output ("targets fully covered (>=98% of rail): {0} / {1}   with zero coverage: {2}" -f $fullCov, $tList.Count, $noneCov)

# --- per-bucket recall (short walls are targets too) ---
Write-Output ""
foreach ($bucket in @(
        @{ name = "nub   <2ft"; min = 0.0; max = 2.0 },
        @{ name = "short 2-10"; min = 2.0; max = 10.0 },
        @{ name = "long  >10ft"; min = 10.0; max = 1e9 }
    )) {
    $bt = @($tList | Where-Object { $_.len -ge $bucket.min -and $_.len -lt $bucket.max })
    if ($bt.Count -eq 0) { continue }
    $btArea = ($bt | Measure-Object -Property area -Sum).Sum
    $btInt = 0.0
    for ($i = 0; $i -lt $tList.Count; $i++) {
        if ($tList[$i].len -lt $bucket.min -or $tList[$i].len -ge $bucket.max) { continue }
        if ($assigned.ContainsKey("t$i")) { $btInt += $assigned["t$i"].area }
    }
    $br = 0.0; if ($btArea -gt 0) { $br = $btInt / $btArea }
    Write-Output ("bucket {0}: {1} targets, {2:N0} sq ft, recall {3:N3}" -f $bucket.name, $bt.Count, $btArea, $br)
}
