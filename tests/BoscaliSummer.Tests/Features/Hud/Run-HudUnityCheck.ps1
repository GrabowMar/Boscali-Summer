param(
    [string]$Unity = "C:/Program Files/Unity/Hub/Editor/2022.3.62f3/Editor/Unity.exe",
    [string]$PreviewDirectory = (Join-Path $env:TEMP ("BoscaliHudCheck-" + [guid]::NewGuid().ToString("N")))
)
$ErrorActionPreference = "Stop"
if (Test-Path -LiteralPath "$PreviewDirectory/result.txt") { Remove-Item -LiteralPath "$PreviewDirectory/result.txt" }
$repo = (Resolve-Path (Join-Path $PSScriptRoot "../../../..")).Path
New-Item -ItemType Directory -Force -Path "$PreviewDirectory/Assets", "$PreviewDirectory/ProjectSettings", "$PreviewDirectory/Packages", "$PreviewDirectory/NOAvionics" | Out-Null
Set-Content -LiteralPath "$PreviewDirectory/ProjectSettings/ProjectVersion.txt" -Value "m_EditorVersion: 2022.3.62f3"
Set-Content -LiteralPath "$PreviewDirectory/Packages/manifest.json" -Value '{"dependencies":{"com.unity.ugui":"1.0.0","com.unity.textmeshpro":"3.0.6","com.unity.modules.audio":"1.0.0","com.unity.modules.physics":"1.0.0","com.unity.modules.imageconversion":"1.0.0","com.unity.modules.uielements":"1.0.0"}}'
Get-ChildItem -LiteralPath "$repo/Avionics", "$repo/AvionicsUi" -Filter '*.cs' | Where-Object { $_.Name -notlike '*Tests.cs' } | Copy-Item -Destination "$PreviewDirectory/Assets/"
Copy-Item -LiteralPath "$repo/AvionicsUi/avionics.avss" -Destination "$PreviewDirectory/NOAvionics/"
Get-ChildItem -LiteralPath "$repo/modules/Hud/Presentation", "$repo/modules/Hud/Domain", "$repo/modules/Hud/Configuration" -Filter '*.cs' | Copy-Item -Destination "$PreviewDirectory/Assets/"
Copy-Item -LiteralPath "$repo/modules/Hud/Runtime/FlightTelemetry.cs", "$repo/modules/Hud/Runtime/MissileTelemetry.cs", "$repo/modules/Hud/Runtime/NativeHudPresentation.cs", "$repo/modules/Hud/Runtime/ThirdPersonHudController.cs", "$repo/modules/Hud/Runtime/ThirdPersonCameraPolicy.cs", "$repo/Framework/Contracts/IObservationSource.cs", "$repo/Framework/Contracts/IHudBoard.cs", "$repo/Framework/Contracts/IThirdPersonHud.cs", "$repo/Framework/Contracts/HudLayout.cs", "$repo/Framework/Contracts/HudPrimitives.cs", "$PSScriptRoot/HudUnityStubs.cs", "$PSScriptRoot/HudUnityCheck.cs" -Destination "$PreviewDirectory/Assets/"
Get-ChildItem -LiteralPath 'C:/Program Files (x86)/Steam/steamapps/common/Nuclear Option/BepInEx/core' -Filter '*.dll' | Where-Object { $_.Name -eq 'BepInEx.dll' -or $_.Name -eq '0Harmony.dll' -or $_.Name -like 'Mono.*' -or $_.Name -like 'MonoMod.*' } | Copy-Item -Destination "$PreviewDirectory/Assets/"
$arguments = @('-batchmode', '-projectPath', ('"' + $PreviewDirectory + '"'), '-executeMethod', 'HudUnityCheck.Run', '-logFile', ('"' + "$PreviewDirectory/check.log" + '"'))
$process = Start-Process -FilePath $Unity -ArgumentList $arguments -WorkingDirectory $PreviewDirectory -WindowStyle Hidden -PassThru
$process.WaitForExit()
Write-Output "Results and renders: $PreviewDirectory"
if (Test-Path -LiteralPath "$PreviewDirectory/result.txt") { Get-Content -LiteralPath "$PreviewDirectory/result.txt" }
else { Get-Content -LiteralPath "$PreviewDirectory/check.log" -Tail 60 }
if ($process.ExitCode -ne 0) { throw "Unity HUD check failed: $($process.ExitCode)" }
if (!(Test-Path -LiteralPath "$PreviewDirectory/result.txt")) { throw "Unity HUD check did not produce a result." }
