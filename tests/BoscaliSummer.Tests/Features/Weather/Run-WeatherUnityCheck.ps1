param([string]$Unity = 'C:/Program Files/Unity/Hub/Editor/2022.3.62f3/Editor/Unity.exe')
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '../../../..')).Path
$fixture = Join-Path $env:TEMP ('BoscaliWeatherCheck-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force "$fixture/Assets/Resources", "$fixture/Assets/Code", "$fixture/Packages", "$fixture/ProjectSettings" | Out-Null
Set-Content "$fixture/ProjectSettings/ProjectVersion.txt" 'm_EditorVersion: 2022.3.62f3'
Set-Content "$fixture/Packages/manifest.json" '{"dependencies":{"com.unity.modules.audio":"1.0.0","com.unity.modules.assetbundle":"1.0.0","com.unity.modules.particlesystem":"1.0.0","com.unity.modules.imageconversion":"1.0.0","com.unity.modules.physics":"1.0.0"}}'
Copy-Item "$repo/modules/Weather/Visuals/*.cs" "$fixture/Assets/Code/"
Copy-Item "$repo/modules/Weather/Audio/ProceduralRainAudio.cs" "$fixture/Assets/Code/"
Copy-Item "$repo/modules/Weather/Domain/*.cs" "$fixture/Assets/Code/"
Copy-Item "$PSScriptRoot/WeatherUnityCheck.cs" "$fixture/Assets/Code/"
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
$editor = Start-Process $Unity -ArgumentList @('-batchmode','-projectPath',('"'+$fixture+'"'),'-executeMethod','WeatherUnityCheck.Build','-logFile',('"'+"$fixture/build.log"+'"')) -WorkingDirectory $fixture -WindowStyle Hidden -PassThru
$editor.WaitForExit()
if ($editor.ExitCode -ne 0 -or -not (Test-Path "$fixture/Player/WeatherCheck.exe")) { throw "Player build failed: $fixture/build.log" }
$player = Start-Process "$fixture/Player/WeatherCheck.exe" -ArgumentList @('-batchmode','-screen-width','320','-screen-height','240','-logFile',('"'+"$fixture/player.log"+'"')) -WorkingDirectory $fixture -WindowStyle Hidden -PassThru
if (-not $player.WaitForExit(120000)) { $player.Kill(); throw "Fixture timeout: $fixture/player.log" }
Get-Content "$fixture/result.txt"
if ($player.ExitCode -ne 0) { throw "Weather fixture failed: $fixture/player.log" }
