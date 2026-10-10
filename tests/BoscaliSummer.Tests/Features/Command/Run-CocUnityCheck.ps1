param(
    [string]$Unity = "C:/Program Files/Unity/Hub/Editor/2022.3.62f3/Editor/Unity.exe",
    [string]$PreviewDirectory = (Join-Path $env:TEMP ("BoscaliCocCheck-" + [guid]::NewGuid().ToString("N")))
)
$ErrorActionPreference = "Stop"
. "$PSScriptRoot/../../../UnityCheck.Common.ps1"
New-UnityCheckProject $PreviewDirectory '{"dependencies":{"com.unity.ugui":"1.0.0","com.unity.textmeshpro":"3.0.6","com.unity.modules.audio":"1.0.0","com.unity.modules.imageconversion":"1.0.0","com.unity.modules.uielements":"1.0.0","com.unity.modules.assetbundle":"1.0.0"}}' -Folders 'NOAvionics'
Get-ChildItem -LiteralPath "$repo/AvionicsUi" -Filter '*.cs' -Recurse | Where-Object { $_.Name -notlike '*Tests.cs' } | Copy-Item -Destination "$PreviewDirectory/Assets/"
Copy-AvionicsStyles $PreviewDirectory
Copy-Item -LiteralPath `
    "$repo/modules/Command/Presentation/MapUi/MfdGlyph.cs", `
    "$repo/modules/Command/Presentation/MapUi/MfdLayout.cs", `
    "$repo/Core/Game/MfdPanelInstaller.cs", `
    "$repo/modules/Command/Presentation/StrMfdPanel.cs", `
    "$repo/modules/Command/Presentation/StrMfdPanel.Coc.cs", `
    "$repo/modules/Command/Presentation/StrMfdPanel.Cmd.cs", `
    "$repo/modules/Command/Presentation/StrConsoleParts.cs", `
    "$repo/modules/Command/Presentation/StrSituationParts.cs", `
    "$repo/modules/Command/Presentation/StrCommandParts.cs", `
    "$repo/modules/Command/Presentation/StrOperationsParts.cs", `
    "$repo/modules/Command/Presentation/StrNodeBoard.cs", `
    "$repo/modules/Command/Presentation/StrLogBoard.cs", `
    "$repo/modules/Command/Domain/CommandRosterOrder.cs", `
    "$repo/modules/Command/Domain/TacticalTheaterState.cs", `
    "$repo/modules/Command/Domain/TheaterReadout.cs", `
    "$repo/modules/Command/Domain/SortieClassifier.cs", `
    "$repo/Core/Contracts/IHighCommandView.cs", `
    "$repo/Core/Contracts/CommanderLogLine.cs", `
    "$repo/Core/Contracts/IBaseDefenseAlarmService.cs", `
    "$repo/Core/Contracts/IActiveEventsView.cs", `
    "$repo/Core/Contracts/ITheaterStrikePicture.cs", `
    "$repo/Core/Contracts/ITheaterWarView.cs", `
    "$repo/Core/Contracts/ITerritoryIngress.cs", `
    "$repo/Core/Contracts/IThreatPicture.cs", `
    "$PSScriptRoot/SettingsUnityStubs.cs", `
    "$PSScriptRoot/CocUnityCheck.cs", $UnityCheckHarness `
    -Destination "$PreviewDirectory/Assets/"
Copy-GameDlls "$PreviewDirectory/Assets/" -BepInExMatch '^(BepInEx|Mono|0Harmony)'
Copy-Item -LiteralPath "$repo/AvionicsUi/Assets/avionics-ui.bundle" -Destination "$PreviewDirectory/avionics-ui.bundle"
$process = Invoke-UnityCheck $Unity $PreviewDirectory 'CocUnityCheck.Run' -Flags '-batchmode'
Write-Output "Results and renders: $PreviewDirectory"
Show-UnityCheckResult $PreviewDirectory -LogTail 80
Assert-UnityResult $PreviewDirectory $process -ExitMessage "Unity COC check failed: $($process.ExitCode)" -FailMessage "Unity exited without a successful result: $PreviewDirectory"
