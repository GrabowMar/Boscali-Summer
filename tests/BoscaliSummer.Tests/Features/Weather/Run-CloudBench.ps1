# Offline cloud renderer bench: builds a standalone player with the weather domain, the map
# builder, the uniforms and the shipped cloud shaders (render-pipeline tag stripped so the
# built-in pipeline runs them), renders fixed scenes, and prints GPU-synchronized wall ms per mode.
# -OldShader: a previous FlightCloud.shader to time as "old" beside the current one.
# -Tape: "" the field's own one-keyframe tape (the perf baseline B, live path), "texture" the same sky as a 4096-texel tape (B', the texture path),
#   "ladder" every rung half-way to the next (the baseline C),
#   "ramp" a tape whose genus and layers change across a 12 km ramp,
#   "mix" both thin layers with all four morphologies at weight 0.25 (the worst case of the slab).
# -Skies: the sky contact-sheet mode ("all", a class name such as Fair, or sky numbers such as 8,17,29): a metrics row per sky in
#   sky-metrics.csv and one sheet-<class>.png per class, in the evidence folder. It runs alone with its own timeout.
# -ClassBaseline: with -Skies, render the same poses under the class state instead of the pin (the perf baseline of the pinned run).
param(
    [string]$Unity = 'C:/Program Files/Unity/Hub/Editor/2022.3.62f3/Editor/Unity.exe',
    [string]$OldShader = '',
    [string]$Only = '',
    [switch]$Variant,
    [switch]$Static,
    [ValidateSet('', 'texture', 'ladder', 'ramp', 'mix')][string]$Tape = '',
    [string[]]$Skies = @(),
    [int]$SkyTimeoutSeconds = 3000,
    [switch]$Strict,
    [switch]$ClassBaseline,
    [int]$SettleFrames = 48,
    [int]$MeasuredFrames = 48,
    [string]$EvidenceDir = '',
    [string]$PoseFile = '',
    [int]$TimeoutSeconds = 900
)
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/../../../UnityCheck.Common.ps1"
$fixture = Resolve-UnityCheckDir $EvidenceDir 'BoscaliCloudBench-'
if (Test-Path -LiteralPath $fixture) { throw "Evidence directory already exists: $fixture" }
if (-not (Test-Path -LiteralPath $Unity)) { throw "Unity Editor not found: $Unity" }
New-UnityCheckProject $fixture '{"dependencies":{"com.unity.modules.imageconversion":"1.0.0"}}' -Folders 'Assets/Resources', 'Assets/Code'
Copy-Item (Get-ChildItem "$repo/modules/Weather/Domain/*.cs" | Where-Object { $_.Name -notlike 'MetOffice*' }).FullName "$fixture/Assets/Code/"
Copy-Item "$repo/Core/Math/Deterministic.cs", "$repo/Core/Math/Scalar.cs" "$fixture/Assets/Code/"
Copy-Item "$repo/Core/Contracts/FxBudget.cs", "$repo/Core/Contracts/IClientEffect.cs" "$fixture/Assets/Code/"
Copy-Item "$repo/Core/Fx/*.cs" "$fixture/Assets/Code/" -Exclude SfxClips.cs
foreach ($f in 'CloudNoise3D.cs', 'CloudMaps.cs', 'CloudBodies.cs', 'CloudVolumeUniforms.cs', 'CloudLowRes.cs', 'TapeTexture.cs') { Copy-Item "$repo/modules/Weather/Visuals/$f" "$fixture/Assets/Code/" }
Copy-Item "$PSScriptRoot/CloudBench.cs", "$PSScriptRoot/CloudBenchSkies.cs", "$PSScriptRoot/CloudBenchSeam.cs", "$PSScriptRoot/SkyMetrics.cs" "$fixture/Assets/Code/"
function Copy-Shader([string]$from, [string]$to, [string]$rename = '') {
    $text = Get-Content -Raw $from
    $text = $text -replace '"RenderPipeline"="UniversalPipeline"\s*', ''
    if ($rename) { $text = $text -replace 'Shader "Boscali/FlightCloud"', ('Shader "' + $rename + '"') }
    Set-Content -NoNewline -Encoding UTF8 "$fixture/Assets/Resources/$to" $text
}
Copy-Shader "$repo/modules/Weather/Assets/Source/FlightCloud.shader" 'FlightCloud.shader'
Copy-Shader "$repo/modules/Weather/Assets/Source/FlightCloudComposite.shader" 'FlightCloudComposite.shader'
if ($OldShader) { Copy-Shader $OldShader 'FlightCloudOld.shader' 'Boscali/FlightCloudOld' }
Set-Content "$fixture/Assets/Resources/BenchGround.shader" @'
Shader "Hidden/BenchGround" {
SubShader { Tags {"RenderType"="Opaque"} Pass { Color (0.28,0.33,0.24,1) } }
}
'@
Write-Output "Cloud bench fixture: $fixture"
Export-UnityCheckHashes "$fixture/input-manifest.csv" (Get-ChildItem "$fixture/Assets" -Recurse -File)
$editor = Invoke-UnityCheck $Unity $fixture 'CloudBench.Build' -Flags '-batchmode' -LogName 'build.log'
if ($editor.ExitCode -ne 0 -or -not (Test-Path "$fixture/Player/CloudBench.exe")) {
    if (Test-Path "$fixture/build-result.txt") { Get-Content "$fixture/build-result.txt" }
    throw "Bench build failed: $fixture/build.log"
}
$env:CLOUD_BENCH_ONLY = $Only
$env:CLOUD_BENCH_STATIC = if ($Static) { '1' } else { '' }
$env:CLOUD_BENCH_SETTLE = [string]$SettleFrames
$env:CLOUD_BENCH_FRAMES = [string]$MeasuredFrames
$env:CLOUD_BENCH_POSES = $PoseFile
$env:CLOUD_BENCH_TAPE = $Tape
$env:CLOUD_BENCH_SKIES = $Skies -join ','
$env:CLOUD_BENCH_SKYCLASS = if ($ClassBaseline) { '1' } else { '' }
$timeout = if ($Skies) { $SkyTimeoutSeconds } else { $TimeoutSeconds }
if ($Variant) { $env:CLOUD_BENCH_VARIANT = "1" } else { $env:CLOUD_BENCH_VARIANT = "" }
$player = Invoke-UnityPlayer "$fixture/Player/CloudBench.exe" @('-batchmode', '-logFile', ('"' + "$fixture/player.log" + '"')) $fixture `
    -TimeoutSeconds $timeout -TimeoutMessage "Bench timeout: $fixture/player.log"
$result = Get-Content "$fixture/result.txt" -Raw
Write-Output $result
if ($Skies -and (Test-Path "$fixture/sky-metrics.csv")) {
    $rows = @(Import-Csv "$fixture/sky-metrics.csv")
    $failed = @($rows | Where-Object { $_.verdict -like 'FAIL*' })
    Write-Output ("SKY METRICS: {0} pass, {1} fail. Sheets and metrics: {2}" -f ($rows.Count - $failed.Count), $failed.Count, $fixture)
    $pairs = if (Test-Path "$fixture/sky-distinct.csv") { @(Import-Csv "$fixture/sky-distinct.csv") } else { @() }
    $alike = @($pairs | Where-Object { $_.verdict -eq 'FAIL' })
    Write-Output ("SKY DISTINCT: {0} of {1} pairs of one class look alike (floor CloudBenchSkies.DistinctFloor); see sky-distinct.csv" -f $alike.Count, $pairs.Count)
    if ($Strict -and ($failed.Count -gt 0 -or $alike.Count -gt 0)) { throw "Sky metrics failed for $($failed.Count) skies and $($alike.Count) look-alike pairs: $fixture" }
}
if ($player.ExitCode -ne 0 -or $result -match '(?m)^FAIL ' -or $result -notmatch 'ms/frame') {
    throw "Cloud bench failed: $fixture/player.log"
}
