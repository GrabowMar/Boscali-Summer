param(
    [string]$AtlasStyles,
    [string]$Unity = "C:/Program Files/Unity/Hub/Editor/2022.3.62f3/Editor/Unity.exe",
    [string]$PreviewDirectory = (Join-Path $env:TEMP ("BoscaliSettingsCheck-" + [guid]::NewGuid().ToString("N")))
)
$ErrorActionPreference = "Stop"
. "$PSScriptRoot/../../../UnityCheck.Common.ps1"
New-UnityCheckProject $PreviewDirectory '{"dependencies":{"com.unity.ugui":"1.0.0","com.unity.textmeshpro":"3.0.6","com.unity.modules.audio":"1.0.0","com.unity.modules.imageconversion":"1.0.0","com.unity.modules.uielements":"1.0.0","com.unity.modules.assetbundle":"1.0.0"}}' -Folders 'NOAvionics'
Get-ChildItem -LiteralPath "$repo/AvionicsUi" -Filter '*.cs' -Recurse | Where-Object { $_.Name -notlike '*Tests.cs' } | Copy-Item -Destination "$PreviewDirectory/Assets/"
Copy-AvionicsStyles $PreviewDirectory
Copy-Item -LiteralPath "$repo/modules/Command/Presentation/MapUi/SettingsMfdPanel.cs", "$repo/modules/Command/Presentation/MapUi/SettingsMfdPanel.Performance.cs", "$repo/modules/Command/Presentation/MapUi/SettingsMfdPanel.Hud.cs", "$repo/modules/Command/Presentation/MapUi/SettingsMfdPanel.Client.cs", "$repo/modules/Command/Presentation/MapUi/SettingsMfdPanel.Modes.cs", "$repo/modules/Command/Presentation/MapUi/SettingsServerPage.cs", "$repo/modules/Command/Presentation/MapUi/SettingsChoices.cs", "$repo/modules/Command/Presentation/MapUi/MapMfdLookup.cs", "$repo/modules/Command/Presentation/MapUi/MfdLayout.cs", "$repo/modules/Command/Presentation/MapUi/MfdSecondaryObjectives.cs", "$repo/modules/Command/Configuration/CommandSettings.cs", "$repo/Core/Contracts/IHostSettingsView.cs", "$repo/Core/Contracts/ISecondaryObjectivesView.cs", "$repo/Core/Contracts/IHudBoard.cs", "$repo/Core/Contracts/HudPrimitives.cs", "$repo/Core/Contracts/HudLayout.cs", "$repo/Core/Ui/HostSettingsBoard.cs", "$repo/Core/Ui/ClientSettingsBoard.cs", "$PSScriptRoot/SettingsUnityCheck.cs", $UnityCheckHarness, "$PSScriptRoot/SettingsUnityStubs.cs" -Destination "$PreviewDirectory/Assets/"
Copy-Item -LiteralPath "$repo/modules/Command/Presentation/MapUi/SettingsParts.cs", "$repo/modules/Command/Presentation/MapUi/SettingsPreviews.cs", "$repo/modules/Command/Presentation/MapUi/SettingsMfdPanel.Immersion.cs", "$repo/Core/Contracts/IImmersionSettings.cs" -Destination "$PreviewDirectory/Assets/"
Copy-Item -LiteralPath "$repo/modules/Command/Presentation/StrNodeBoard.cs", "$repo/modules/Command/Presentation/StrConsoleParts.cs", "$repo/modules/Command/Domain/TacticalTheaterState.cs", "$repo/modules/Command/Domain/SortieClassifier.cs" -Destination "$PreviewDirectory/Assets/"
Copy-Item -LiteralPath "$repo/modules/Command/Presentation/StrLogBoard.cs", "$repo/modules/Command/Domain/TheaterReadout.cs", "$repo/Core/Contracts/IThreatPicture.cs" -Destination "$PreviewDirectory/Assets/"
Copy-GameDlls "$PreviewDirectory/Assets/" -BepInExMatch '^(BepInEx\.dll$|Mono|0Harmony\.dll$)'
Copy-Item -LiteralPath "$repo/AvionicsUi/Assets/avionics-ui.bundle" -Destination "$PreviewDirectory/avionics-ui.bundle"
Copy-AvionicsStyles $PreviewDirectory $AtlasStyles -NoDefaults
$process = Invoke-UnityCheck $Unity $PreviewDirectory 'SettingsUnityCheck.Run' -Flags '-batchmode'
Write-Output "Results and renders: $PreviewDirectory"
Show-UnityCheckResult $PreviewDirectory
Assert-UnityResult $PreviewDirectory $process -ExitMessage "Unity settings check failed: $($process.ExitCode)"
