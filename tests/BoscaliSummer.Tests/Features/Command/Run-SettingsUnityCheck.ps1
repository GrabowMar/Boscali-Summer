param(
    [string]$Unity = "C:/Program Files/Unity/Hub/Editor/2022.3.62f3/Editor/Unity.exe",
    [string]$PreviewDirectory = (Join-Path $env:TEMP ("BoscaliSettingsCheck-" + [guid]::NewGuid().ToString("N")))
)
$ErrorActionPreference = "Stop"
$repo = (Resolve-Path (Join-Path $PSScriptRoot "../../../..")).Path
New-Item -ItemType Directory -Force -Path "$PreviewDirectory/Assets", "$PreviewDirectory/ProjectSettings", "$PreviewDirectory/Packages", "$PreviewDirectory/NOAvionics" | Out-Null
Set-Content -LiteralPath "$PreviewDirectory/ProjectSettings/ProjectVersion.txt" -Value "m_EditorVersion: 2022.3.62f3"
Set-Content -LiteralPath "$PreviewDirectory/Packages/manifest.json" -Value '{"dependencies":{"com.unity.ugui":"1.0.0","com.unity.textmeshpro":"3.0.6","com.unity.modules.audio":"1.0.0","com.unity.modules.imageconversion":"1.0.0","com.unity.modules.uielements":"1.0.0"}}'
Get-ChildItem -LiteralPath "$repo/Avionics", "$repo/AvionicsUi" -Filter '*.cs' | Where-Object { $_.Name -notlike '*Tests.cs' } | Copy-Item -Destination "$PreviewDirectory/Assets/"
Copy-Item -LiteralPath "$repo/AvionicsUi/avionics.avss" -Destination "$PreviewDirectory/NOAvionics/"
Copy-Item -LiteralPath "$repo/tests/BoscaliSummer.Tests/Avionics/AvionicsUnityCheck.cs" -Destination "$PreviewDirectory/Assets/"
Copy-Item -LiteralPath "$repo/modules/Command/Presentation/MapUi/SettingsMfdPanel.cs", "$repo/modules/Command/Presentation/MapUi/SettingsMfdPanel.Hud.cs", "$repo/modules/Command/Presentation/MapUi/SettingsMfdPanel.Visuals.cs", "$repo/Framework/Contracts/IThirdPersonHud.cs", "$repo/Framework/Contracts/IVisualEnhancements.cs", "$repo/Framework/Contracts/IImmersionSettings.cs", "$repo/modules/Command/Presentation/MapUi/SettingsServerPage.cs", "$repo/modules/Command/Presentation/MapUi/SettingsChoices.cs", "$repo/modules/Command/Presentation/MapUi/MapMfdLookup.cs", "$repo/modules/Command/Presentation/MapUi/MfdLayout.cs", "$repo/modules/Command/Presentation/MapUi/MfdSecondaryObjectives.cs", "$repo/modules/Command/Configuration/CommandSettings.cs", "$repo/Framework/Contracts/IHostSettingsView.cs", "$repo/Framework/Contracts/ISecondaryObjectivesView.cs", "$repo/Framework/Contracts/IHudBoard.cs", "$repo/Framework/Contracts/HudPrimitives.cs", "$repo/Framework/Contracts/HudLayout.cs", "$repo/Framework/Features/HostSettingsBoard.cs", "$PSScriptRoot/SettingsUnityCheck.cs", "$PSScriptRoot/SettingsUnityStubs.cs" -Destination "$PreviewDirectory/Assets/"
Copy-Item -LiteralPath 'C:/Program Files (x86)/Steam/steamapps/common/Nuclear Option/BepInEx/core/BepInEx.dll' -Destination "$PreviewDirectory/Assets/"
Get-ChildItem -LiteralPath 'C:/Program Files (x86)/Steam/steamapps/common/Nuclear Option/BepInEx/core' -Filter '*.dll' | Where-Object { $_.Name -match '^(Mono|0Harmony\.dll$)' } | Copy-Item -Destination "$PreviewDirectory/Assets/"
if (Test-Path -LiteralPath "$PreviewDirectory/result.txt") { Remove-Item -LiteralPath "$PreviewDirectory/result.txt" }
$arguments = @('-batchmode', '-projectPath', ('"' + $PreviewDirectory + '"'), '-executeMethod', 'SettingsUnityCheck.Run', '-logFile', ('"' + "$PreviewDirectory/check.log" + '"'))
$process = Start-Process -FilePath $Unity -ArgumentList $arguments -WorkingDirectory $PreviewDirectory -WindowStyle Hidden -PassThru
$process.WaitForExit()
Write-Output "Results and renders: $PreviewDirectory"
if (Test-Path "$PreviewDirectory/result.txt") { Get-Content "$PreviewDirectory/result.txt" }
else { Get-Content "$PreviewDirectory/check.log" -Tail 60 }
if ($process.ExitCode -ne 0) { throw "Unity settings check failed: $($process.ExitCode)" }


