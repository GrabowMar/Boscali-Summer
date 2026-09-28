# Offline cloud renderer bench: builds a standalone player with the weather domain, the map
# builder, the uniforms and the shipped cloud shaders (render-pipeline tag stripped so the
# built-in pipeline runs them), renders fixed scenes, and prints GPU ms per mode.
# -OldShader: a previous FlightCloud.shader to time as "old" beside the current one.
param(
    [string]$Unity = 'C:/Program Files/Unity/Hub/Editor/2022.3.62f3/Editor/Unity.exe',
    [string]$OldShader = '',
    [string]$Only = '',
    [switch]$Variant
)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '../../../..')).Path
$fixture = Join-Path $env:TEMP ('BoscaliCloudBench-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force "$fixture/Assets/Resources", "$fixture/Assets/Code", "$fixture/Packages", "$fixture/ProjectSettings" | Out-Null
Set-Content "$fixture/ProjectSettings/ProjectVersion.txt" 'm_EditorVersion: 2022.3.62f3'
Set-Content "$fixture/Packages/manifest.json" '{"dependencies":{"com.unity.modules.imageconversion":"1.0.0"}}'
Copy-Item "$repo/modules/Weather/Domain/*.cs" "$fixture/Assets/Code/"
Copy-Item "$repo/Core/Deterministic.cs" "$fixture/Assets/Code/"
foreach ($f in 'CloudNoise3D.cs', 'CloudMaps.cs', 'CloudVolumeUniforms.cs') { Copy-Item "$repo/modules/Weather/Visuals/$f" "$fixture/Assets/Code/" }
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
$editor = Start-Process $Unity -ArgumentList @('-batchmode','-projectPath',('"'+$fixture+'"'),'-executeMethod','CloudBench.Build','-logFile',('"'+"$fixture/build.log"+'"')) -WorkingDirectory $fixture -WindowStyle Hidden -PassThru
$editor.WaitForExit()
if ($editor.ExitCode -ne 0 -or -not (Test-Path "$fixture/Player/CloudBench.exe")) {
    if (Test-Path "$fixture/build-result.txt") { Get-Content "$fixture/build-result.txt" }
    throw "Bench build failed: $fixture/build.log"
}
$env:CLOUD_BENCH_ONLY = $Only
if ($Variant) { $env:CLOUD_BENCH_VARIANT = "1" } else { $env:CLOUD_BENCH_VARIANT = "" }
$player = Start-Process "$fixture/Player/CloudBench.exe" -ArgumentList @('-batchmode','-logFile',('"'+"$fixture/player.log"+'"')) -WorkingDirectory $fixture -WindowStyle Hidden -PassThru
if (-not $player.WaitForExit(900000)) { $player.Kill(); throw "Bench timeout: $fixture/player.log" }
Get-Content "$fixture/result.txt"
