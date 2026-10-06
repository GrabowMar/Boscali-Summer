param(
    [string]$AtlasStyles,
    [string]$Unity = "C:/Program Files/Unity/Hub/Editor/2022.3.62f3/Editor/Unity.exe",
    [string]$PreviewDirectory = (Join-Path $env:TEMP ("BoscaliSettingsCheck-" + [guid]::NewGuid().ToString("N")))
)
$ErrorActionPreference = "Stop"
$repo = (Resolve-Path (Join-Path $PSScriptRoot "../../../..")).Path
New-Item -ItemType Directory -Force -Path "$PreviewDirectory/Assets", "$PreviewDirectory/ProjectSettings", "$PreviewDirectory/Packages", "$PreviewDirectory/NOAvionics" | Out-Null
Set-Content -LiteralPath "$PreviewDirectory/ProjectSettings/ProjectVersion.txt" -Value "m_EditorVersion: 2022.3.62f3"
Set-Content -LiteralPath "$PreviewDirectory/Packages/manifest.json" -Value '{"dependencies":{"com.unity.ugui":"1.0.0","com.unity.textmeshpro":"3.0.6","com.unity.modules.audio":"1.0.0","com.unity.modules.imageconversion":"1.0.0","com.unity.modules.uielements":"1.0.0","com.unity.modules.assetbundle":"1.0.0"}}'
Get-ChildItem -LiteralPath "$repo/AvionicsUi" -Filter '*.cs' -Recurse | Where-Object { $_.Name -notlike '*Tests.cs' } | Copy-Item -Destination "$PreviewDirectory/Assets/"
Get-ChildItem -LiteralPath "$repo/AvionicsUi" -Filter 'avionics.*.avss' | Copy-Item -Destination "$PreviewDirectory/NOAvionics/"
Copy-Item -LiteralPath "$repo/modules/Command/Presentation/MapUi/SettingsMfdPanel.cs", "$repo/modules/Command/Presentation/MapUi/SettingsMfdPanel.Performance.cs", "$repo/modules/Command/Presentation/MapUi/SettingsMfdPanel.Hud.cs", "$repo/modules/Command/Presentation/MapUi/SettingsMfdPanel.Client.cs", "$repo/modules/Command/Presentation/MapUi/SettingsMfdPanel.Modes.cs", "$repo/modules/Command/Presentation/MapUi/SettingsServerPage.cs", "$repo/modules/Command/Presentation/MapUi/SettingsChoices.cs", "$repo/modules/Command/Presentation/MapUi/MapMfdLookup.cs", "$repo/modules/Command/Presentation/MapUi/MfdLayout.cs", "$repo/modules/Command/Presentation/MapUi/MfdSecondaryObjectives.cs", "$repo/modules/Command/Configuration/CommandSettings.cs", "$repo/Core/Contracts/IHostSettingsView.cs", "$repo/Core/Contracts/ISecondaryObjectivesView.cs", "$repo/Core/Contracts/IHudBoard.cs", "$repo/Core/Contracts/HudPrimitives.cs", "$repo/Core/Contracts/HudLayout.cs", "$repo/Core/Ui/HostSettingsBoard.cs", "$repo/Core/Ui/ClientSettingsBoard.cs", "$PSScriptRoot/SettingsUnityCheck.cs", "$PSScriptRoot/SettingsUnityStubs.cs" -Destination "$PreviewDirectory/Assets/"
Copy-Item -LiteralPath "$repo/modules/Command/Presentation/MapUi/SettingsParts.cs", "$repo/modules/Command/Presentation/MapUi/SettingsPreviews.cs", "$repo/modules/Command/Presentation/MapUi/SettingsMfdPanel.Immersion.cs", "$repo/Core/Contracts/IImmersionSettings.cs" -Destination "$PreviewDirectory/Assets/"
Copy-Item -LiteralPath "$repo/modules/Command/Presentation/StrNodeBoard.cs", "$repo/modules/Command/Presentation/StrConsoleParts.cs", "$repo/modules/Command/Domain/TacticalTheaterState.cs", "$repo/modules/Command/Domain/SortieClassifier.cs" -Destination "$PreviewDirectory/Assets/"
Copy-Item -LiteralPath "$repo/modules/Command/Presentation/StrLogBoard.cs", "$repo/modules/Command/Domain/TheaterReadout.cs", "$repo/Core/Contracts/IThreatPicture.cs" -Destination "$PreviewDirectory/Assets/"
Copy-Item -LiteralPath 'C:/Program Files (x86)/Steam/steamapps/common/Nuclear Option/BepInEx/core/BepInEx.dll' -Destination "$PreviewDirectory/Assets/"
Get-ChildItem -LiteralPath 'C:/Program Files (x86)/Steam/steamapps/common/Nuclear Option/BepInEx/core' -Filter '*.dll' | Where-Object { $_.Name -match '^(Mono|0Harmony\.dll$)' } | Copy-Item -Destination "$PreviewDirectory/Assets/"
if (Test-Path -LiteralPath "$PreviewDirectory/result.txt") { Remove-Item -LiteralPath "$PreviewDirectory/result.txt" }
Copy-Item -LiteralPath "$repo/AvionicsUi/Assets/avionics-ui.bundle" -Destination "$PreviewDirectory/avionics-ui.bundle"
$arguments = @('-batchmode', '-projectPath', ('"' + $PreviewDirectory + '"'), '-executeMethod', 'SettingsUnityCheck.Run', '-logFile', ('"' + "$PreviewDirectory/check.log" + '"'))
if ($AtlasStyles) { Get-ChildItem -LiteralPath (Join-Path $AtlasStyles 'NOAvionics') -Filter '*.avss' | Copy-Item -Destination (Join-Path $PreviewDirectory 'NOAvionics') -Force }
$process = Start-Process -FilePath $Unity -ArgumentList $arguments -WorkingDirectory $PreviewDirectory -WindowStyle Hidden -PassThru
$process.WaitForExit()
Write-Output "Results and renders: $PreviewDirectory"
if (Test-Path "$PreviewDirectory/result.txt") { Get-Content "$PreviewDirectory/result.txt" }
else { Get-Content "$PreviewDirectory/check.log" -Tail 60 }
if ($process.ExitCode -ne 0) { throw "Unity settings check failed: $($process.ExitCode)" }



if (-not (Test-Path -LiteralPath "$PreviewDirectory/result.txt") -or (Get-Content -LiteralPath "$PreviewDirectory/result.txt" -Raw) -notmatch '\APASS:') { throw "Unity exited without a successful result: $PreviewDirectory" }
