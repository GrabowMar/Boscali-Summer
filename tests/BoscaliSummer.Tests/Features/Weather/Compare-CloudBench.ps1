# Compares two Run-CloudBench.ps1 results (the text it prints): per scene and mode, the candidate's GPU ms/frame against the baseline's.
# Spec 10.3: at most max(15 %, +0.35 ms) in Clear and Fair scenes, 15 % elsewhere. Exits 1 when any scene is over its limit.
#   Compare-CloudBench.ps1 -Baseline perf/s2-A-today.txt -Candidate perf/s2-B-constant.txt
# -Allow: sky numbers (comma separated) whose OVER is a recorded exception that the user ratified (Task 13 Step 3). They print "OVER (allowed)"
#   and do not fail the run, so the gate stays mechanical: any other OVER exits 1.
param(
    [Parameter(Mandatory)][string]$Baseline,
    [Parameter(Mandatory)][string]$Candidate,
    [string]$Mode = 'temporal',
    [double]$Percent = 15,
    [double]$FloorMs = 0.35,
    [string]$RelaxedPattern = '^(fair|clear|seafog|lenticulars|sky-0[1-9]|sky-1[0-4])',
    [string]$Allow = ''
)
$ErrorActionPreference = 'Stop'
function Read-Bench([string]$path) {
    $times = @{}
    foreach ($line in Get-Content -LiteralPath $path) {
        # "<scene padded to 16><mode padded to 5><ms padded to 8> ms/frame": a long scene name runs into the mode, and the
        # player prints in the machine's culture (a decimal comma on a Polish system).
        if ($line -match '^(\S+?)\s*(old|full|half|temporal)\s+([0-9]+[.,][0-9]+) ms/frame') { $times["$($Matches[1])/$($Matches[2])"] = [double]::Parse($Matches[3].Replace(',', '.'), [Globalization.CultureInfo]::InvariantCulture) }
    }
    $times
}
$baseTimes = Read-Bench $Baseline
$candTimes = Read-Bench $Candidate
$failed = 0
$allowedOver = 0
$allowedSkies = @($Allow -split '[,\s]+' | Where-Object { $_ })
foreach ($key in ($baseTimes.Keys | Sort-Object)) {
    $scene, $m = $key -split '/'
    if ($m -ne $Mode -or -not $candTimes.ContainsKey($key)) { continue }
    $limit = $baseTimes[$key] * (1 + $Percent / 100)
    if ($scene -match $RelaxedPattern) { $limit = [Math]::Max($limit, $baseTimes[$key] + $FloorMs) }
    $delta = $candTimes[$key] - $baseTimes[$key]
    $isAllowed = $false
    foreach ($n in $allowedSkies) { if ($scene -match ('^sky-0*' + [int]$n + '-')) { $isAllowed = $true } }
    $verdict = if ($candTimes[$key] -le $limit) { 'ok' } elseif ($isAllowed) { $allowedOver++; 'OVER (allowed)' } else { $failed++; 'OVER' }
    '{0,-28} base {1,7:N2}  candidate {2,7:N2}  ({3,6:N2} ms, {4,6:N1} %)  limit {5,7:N2}  {6}' -f $scene, $baseTimes[$key], $candTimes[$key], $delta, (100 * $delta / [Math]::Max(0.01, $baseTimes[$key])), $limit, $verdict
}
if ($failed -gt 0) { Write-Output "$failed scene(s) over the limit"; exit 1 }
if ($allowedOver -gt 0) { Write-Output "All compared scenes within the limit except $allowedOver allowed exception(s)"; exit 0 }
Write-Output 'All compared scenes within the limit'
