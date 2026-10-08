param([string]$Unity = 'C:/Program Files/Unity/Hub/Editor/2022.3.62f3/Editor/Unity.exe',
    [string]$EvidenceDir = '', [switch]$Backbuffer)
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/../../../UnityCheck.Common.ps1"
$fixture = Resolve-UnityCheckDir $EvidenceDir 'BoscaliWeatherUrp-'
if (Test-Path -LiteralPath $fixture) { throw "Evidence directory already exists: $fixture" }
if (-not (Test-Path -LiteralPath $Unity)) { throw "Unity Editor not found: $Unity" }
New-UnityCheckProject $fixture '{"dependencies":{"com.unity.render-pipelines.universal":"14.0.12","com.unity.modules.audio":"1.0.0","com.unity.modules.imageconversion":"1.0.0","com.unity.modules.physics":"1.0.0"}}' -Folders 'Assets/Resources', 'Assets/Code'
Copy-Item "$repo/modules/Weather/Domain/*.cs" "$fixture/Assets/Code/"
Copy-Item "$repo/Core/Math/Deterministic.cs", "$repo/Core/Math/Scalar.cs" "$fixture/Assets/Code/"
Copy-Item "$repo/Core/Contracts/FxBudget.cs", "$repo/Core/Contracts/IClientEffect.cs" "$fixture/Assets/Code/"
Copy-Item "$repo/Core/Fx/*.cs" "$fixture/Assets/Code/"
foreach ($f in 'CloudNoise3D.cs', 'CloudMaps.cs', 'CloudBodies.cs', 'CloudVolumeUniforms.cs', 'CloudLowRes.cs', 'WeatherCloudPass.cs') {
    Copy-Item "$repo/modules/Weather/Visuals/$f" "$fixture/Assets/Code/"
}
Copy-Item "$PSScriptRoot/WeatherUrpCheck.cs" "$fixture/Assets/Code/"
Copy-Item "$PSScriptRoot/CloudEdgeFixture.shader" "$fixture/Assets/Resources/"
Copy-Item "$repo/modules/Immersion/Visuals/MaterialSlotClone.cs", "$repo/modules/Immersion/Audio/CockpitAudioFilter.cs" "$fixture/Assets/Code/"
Copy-Item "$repo/modules/Immersion/Runtime/CockpitCameraOffset.cs" "$fixture/Assets/Code/"
Copy-Item "$repo/modules/Immersion/Domain/ImmersionMath.cs" "$fixture/Assets/Code/"
# Keep production pipeline tags and shaders unchanged for the URP integration check.
Copy-Item "$repo/modules/Weather/Assets/Source/FlightCloud.shader", "$repo/modules/Weather/Assets/Source/FlightCloudComposite.shader" "$fixture/Assets/Resources/"
Export-UnityCheckHashes "$fixture/input-manifest.csv" (Get-ChildItem "$fixture/Assets" -Recurse -File)
Write-Output "URP fixture: $fixture"
$editor = Invoke-UnityCheck $Unity $fixture 'WeatherUrpCheck.Build' -Flags '-batchmode' -LogName 'build.log'
if ($editor.ExitCode -ne 0 -or -not (Test-Path "$fixture/Player/WeatherUrpCheck.exe")) {
    if (Test-Path "$fixture/build-result.txt") { Get-Content "$fixture/build-result.txt" }
    throw "URP fixture build failed: $fixture/build.log"
}
$player = Invoke-UnityPlayer "$fixture/Player/WeatherUrpCheck.exe" @('-batchmode', '-screen-width', '1920', '-screen-height', '1080', '-logFile', ('"' + "$fixture/player.log" + '"')) $fixture `
    -TimeoutSeconds 180 -TimeoutMessage "URP fixture timeout: $fixture/player.log"
$result = Get-Content "$fixture/result.txt" -Raw
Write-Output $result
if ($player.ExitCode -ne 0 -or $result -match '(?m)^FAIL ') { throw "URP fixture failed: $fixture/player.log" }
Copy-Item "$fixture/result.txt" "$fixture/batch-result.txt"
if (-not $Backbuffer) { return }
# A normal display loop also covers the backbuffer orientation and two URP overlays.
$display = Invoke-UnityPlayer "$fixture/Player/WeatherUrpCheck.exe" @('-motion-backbuffer', '-screen-fullscreen', '0', '-screen-width', '1920', '-screen-height', '1080', '-logFile', ('"' + "$fixture/backbuffer.log" + '"')) $fixture `
    -TimeoutSeconds 180 -TimeoutMessage "Backbuffer fixture timeout: $fixture/backbuffer.log"
$displayResult = Get-Content "$fixture/result.txt" -Raw
Copy-Item "$fixture/result.txt" "$fixture/backbuffer-result.txt"
Write-Output $displayResult
if ($display.ExitCode -ne 0 -or $displayResult -match '(?m)^FAIL ') { throw "Backbuffer fixture failed: $fixture/backbuffer.log" }
# The real display loop also covers post-follow camera motion against a parented cube.
$loop = Invoke-UnityPlayer "$fixture/Player/WeatherUrpCheck.exe" @('-loopinterp-only', '-screen-fullscreen', '0', '-screen-width', '1920', '-screen-height', '1080', '-logFile', ('"' + "$fixture/loopinterp.log" + '"')) $fixture `
    -TimeoutSeconds 180 -TimeoutMessage "Loop-interp fixture timeout: $fixture/loopinterp.log"
$loopResult = Get-Content "$fixture/result.txt" -Raw
Copy-Item "$fixture/result.txt" "$fixture/loopinterp-result.txt"
Write-Output $loopResult
if ($loop.ExitCode -ne 0 -or $loopResult -match '(?m)^FAIL ') { throw "Loop-interp fixture failed: $fixture/loopinterp.log" }
Set-Content "$fixture/result.txt" ($result + $displayResult + $loopResult)
