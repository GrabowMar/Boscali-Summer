param([string]$Unity = 'C:/Program Files/Unity/Hub/Editor/2022.3.62f3/Editor/Unity.exe', [switch]$RainBench,
    [string]$EvidenceDir = '')
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/../../../UnityCheck.Common.ps1"
$fixture = Resolve-UnityCheckDir $EvidenceDir 'BoscaliWeatherCheck-'
if (Test-Path -LiteralPath $fixture) { throw "Evidence directory already exists: $fixture" }
if (-not (Test-Path -LiteralPath $Unity)) { throw "Unity Editor not found: $Unity" }
New-UnityCheckProject $fixture '{"dependencies":{"com.unity.modules.audio":"1.0.0","com.unity.modules.assetbundle":"1.0.0","com.unity.modules.particlesystem":"1.0.0","com.unity.modules.imageconversion":"1.0.0","com.unity.modules.physics":"1.0.0"}}' -Folders 'Assets/Resources', 'Assets/Code'
Get-ChildItem "$repo/modules/Weather/Visuals/*.cs" |
    Where-Object { $_.Name -notin @('WeatherVolumeDressing.cs', 'WeatherCloudShadows.cs', 'WeatherCloudPass.cs') } |
    Copy-Item -Destination "$fixture/Assets/Code/"
Copy-Item "$repo/modules/Weather/Audio/*.cs" "$fixture/Assets/Code/"
Copy-Item "$PSScriptRoot/CloudDressingStubs.cs" "$fixture/Assets/Code/"
Copy-Item "$repo/modules/Weather/Domain/*.cs" "$fixture/Assets/Code/"
Copy-Item "$repo/Core/Math/Deterministic.cs", "$repo/Core/Math/Scalar.cs" "$fixture/Assets/Code/"
Copy-Item "$repo/Core/Contracts/FxBudget.cs" "$fixture/Assets/Code/"
Copy-Item "$repo/Core/Contracts/IClientEffect.cs" "$fixture/Assets/Code/"
Copy-Item "$repo/Core/Fx/*.cs" "$fixture/Assets/Code/"
Copy-Item "$PSScriptRoot/WeatherUnityCheck.cs" "$fixture/Assets/Code/"
Copy-Item "$PSScriptRoot/RainBench.cs" "$fixture/Assets/Code/"
Copy-Item "$repo/modules/Weather/Assets/Source/*" "$fixture/Assets/Resources/" -Recurse -Force
Set-Content "$fixture/Assets/Resources/Fixture.shader" @'
Shader "Hidden/WeatherFixture" {
Properties { _Color("Color",Color)=(0.3,0.3,0.3,1) }
SubShader { Tags {"RenderType"="Opaque"} Pass { Color [_Color] } }
}
'@
Set-Content "$fixture/Assets/Resources/TerrainFixture.shader" @'
Shader "Shader Graphs/TerrainShader" {
SubShader { Tags {"RenderType"="Opaque"} Pass { Color (0.5,0.5,0.5,1) } }
}
'@
Write-Output "Weather fixture: $fixture"
Export-UnityCheckHashes "$fixture/input-manifest.csv" (Get-ChildItem "$fixture/Assets" -Recurse -File)
$entry = if ($RainBench) { 'RainBench.Build' } else { 'WeatherUnityCheck.Build' }
$editor = Invoke-UnityCheck $Unity $fixture $entry -Flags '-batchmode' -LogName 'build.log'
if ($editor.ExitCode -ne 0 -or -not (Test-Path "$fixture/Player/WeatherCheck.exe")) { throw "Player build failed: $fixture/build.log" }
$player = Invoke-UnityPlayer "$fixture/Player/WeatherCheck.exe" @('-batchmode', '-screen-width', '320', '-screen-height', '240', '-logFile', ('"' + "$fixture/player.log" + '"')) $fixture `
    -TimeoutSeconds 120 -TimeoutMessage "Fixture timeout: $fixture/player.log"
Get-Content "$fixture/result.txt"
if ($player.ExitCode -ne 0) { throw "Weather fixture failed: $fixture/player.log" }
