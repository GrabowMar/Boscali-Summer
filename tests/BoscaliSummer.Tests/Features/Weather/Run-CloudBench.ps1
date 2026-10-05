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
$repo = (Resolve-Path (Join-Path $PSScriptRoot '../../../..')).Path
$fixture = if ($EvidenceDir) { [IO.Path]::GetFullPath($EvidenceDir) } else {
    Join-Path $env:TEMP ('BoscaliCloudBench-' + [guid]::NewGuid().ToString('N'))
}
if (Test-Path -LiteralPath $fixture) { throw "Evidence directory already exists: $fixture" }
if (-not (Test-Path -LiteralPath $Unity)) { throw "Unity Editor not found: $Unity" }
New-Item -ItemType Directory -Force "$fixture/Assets/Resources", "$fixture/Assets/Code", "$fixture/Packages", "$fixture/ProjectSettings" | Out-Null
Set-Content "$fixture/ProjectSettings/ProjectVersion.txt" 'm_EditorVersion: 2022.3.62f3'
Set-Content "$fixture/Packages/manifest.json" '{"dependencies":{"com.unity.modules.imageconversion":"1.0.0"}}'
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
Get-ChildItem "$fixture/Assets" -Recurse -File | Get-FileHash |
    Select-Object Hash, Path | Export-Csv "$fixture/input-manifest.csv" -NoTypeInformation
$editor = Start-Process $Unity -ArgumentList @('-batchmode','-projectPath',('"'+$fixture+'"'),'-executeMethod','CloudBench.Build','-logFile',('"'+"$fixture/build.log"+'"')) -WorkingDirectory $fixture -WindowStyle Hidden -PassThru
$editor.WaitForExit()
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
$player = Start-Process "$fixture/Player/CloudBench.exe" -ArgumentList @('-batchmode','-logFile',('"'+"$fixture/player.log"+'"')) -WorkingDirectory $fixture -WindowStyle Hidden -PassThru
if (-not $player.WaitForExit(900000)) { $player.Kill(); throw "Bench timeout: $fixture/player.log" }
$result = Get-Content "$fixture/result.txt" -Raw
Write-Output $result
if ($player.ExitCode -ne 0 -or $result -match '(?m)^FAIL ' -or $result -notmatch 'ms/frame') {
    throw "Cloud bench failed: $fixture/player.log"
}
