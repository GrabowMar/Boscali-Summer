# Offline cloud renderer bench: builds a standalone player with the weather domain, the map
# builder, the uniforms and the shipped cloud shaders (render-pipeline tag stripped so the
# built-in pipeline runs them), renders fixed scenes, and prints GPU-synchronized wall ms per mode.
# -OldShader: a previous FlightCloud.shader to time as "old" beside the current one.
param(
    [string]$Unity = 'C:/Program Files/Unity/Hub/Editor/2022.3.62f3/Editor/Unity.exe',
    [string]$OldShader = '',
    [string]$Only = '',
    [switch]$Variant,
    [switch]$Static,
    [int]$SettleFrames = 48,
    [int]$MeasuredFrames = 48,
    [string]$EvidenceDir = '',
    [string]$PoseFile = ''
)
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/../../../UnityCheck.Common.ps1"
$fixture = Resolve-UnityCheckDir $EvidenceDir 'BoscaliCloudBench-'
if (Test-Path -LiteralPath $fixture) { throw "Evidence directory already exists: $fixture" }
if (-not (Test-Path -LiteralPath $Unity)) { throw "Unity Editor not found: $Unity" }
New-UnityCheckProject $fixture '{"dependencies":{"com.unity.modules.imageconversion":"1.0.0"}}' -Folders 'Assets/Resources', 'Assets/Code'
Copy-Item "$repo/modules/Weather/Domain/*.cs" "$fixture/Assets/Code/"
Copy-Item "$repo/Core/Math/Deterministic.cs" "$fixture/Assets/Code/"
Copy-Item "$repo/Core/Contracts/FxBudget.cs", "$repo/Core/Contracts/IClientEffect.cs" "$fixture/Assets/Code/"
Copy-Item "$repo/Core/Fx/*.cs" "$fixture/Assets/Code/"
foreach ($f in 'CloudNoise3D.cs', 'CloudMaps.cs', 'CloudBodies.cs', 'CloudVolumeUniforms.cs', 'CloudLowRes.cs') { Copy-Item "$repo/modules/Weather/Visuals/$f" "$fixture/Assets/Code/" }
Copy-Item "$PSScriptRoot/CloudBench.cs" "$fixture/Assets/Code/"
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
if ($Variant) { $env:CLOUD_BENCH_VARIANT = "1" } else { $env:CLOUD_BENCH_VARIANT = "" }
$player = Invoke-UnityPlayer "$fixture/Player/CloudBench.exe" @('-batchmode', '-logFile', ('"' + "$fixture/player.log" + '"')) $fixture `
    -TimeoutSeconds 900 -TimeoutMessage "Bench timeout: $fixture/player.log"
$result = Get-Content "$fixture/result.txt" -Raw
Write-Output $result
if ($player.ExitCode -ne 0 -or $result -match '(?m)^FAIL ' -or $result -notmatch 'ms/frame') {
    throw "Cloud bench failed: $fixture/player.log"
}
