param(
    [string]$AtlasStyles,
    [string]$Unity = "C:/Program Files/Unity/Hub/Editor/2022.3.62f3/Editor/Unity.exe",
    [string]$PreviewDirectory = (Join-Path $env:TEMP ("BoscaliStrCheck-" + [guid]::NewGuid().ToString("N"))),
    [switch]$SelfTest
)
# The offline Station Board harness (annex 05 P12). -SelfTest runs the negative control: an oversized string and a missing id are injected and the gate
# must FAIL; the runner then reports the self-test as passed (exit 0) only when the gate really failed on both faults.
$ErrorActionPreference = "Stop"
. "$PSScriptRoot/../../../UnityCheck.Common.ps1"
New-UnityCheckProject $PreviewDirectory '{"dependencies":{"com.unity.ugui":"1.0.0","com.unity.textmeshpro":"3.0.6","com.unity.modules.audio":"1.0.0","com.unity.modules.imageconversion":"1.0.0","com.unity.modules.uielements":"1.0.0","com.unity.modules.assetbundle":"1.0.0","com.unity.modules.imgui":"1.0.0"}}' -Folders 'NOAvionics'
Get-ChildItem -LiteralPath "$repo/AvionicsUi" -Filter '*.cs' -Recurse | Where-Object { $_.Name -notlike '*Tests.cs' } | Copy-Item -Destination "$PreviewDirectory/Assets/"
Copy-AvionicsStyles $PreviewDirectory $AtlasStyles
# The panel half: StrMfdPanel, the parts under Presentation/Str (by wildcard, the folder holds nothing but panel sources), the kit-v2 console parts the board
# shares with the settings pages, and the installer. Nothing from MapUi but the two layout helpers; no overlay, hub, reader or map file is compiled.
$panel = @(
    "$repo/modules/Command/Presentation/StrMfdPanel.cs",
    "$repo/modules/Command/Presentation/StrConsoleParts.cs",
    "$repo/modules/Command/Presentation/MapUi/MfdGlyph.cs",
    "$repo/modules/Command/Presentation/MapUi/MfdLayout.cs",
    "$repo/Core/Game/MfdPanelInstaller.cs"
)
$panel += (Get-ChildItem -LiteralPath "$repo/modules/Command/Presentation/Str" -Filter '*.cs').FullName
# What StrViewBuilder and the panel read: the domain rules (pure), and the contracts they name. TheaterWords is System-only; the builder calls
# TheaterWords.Empty, Command, Wave, Ground and Posture.
$domain = @(
    'StrContracts', 'StrViewModel', 'StrViewBuilder', 'StrViewBuilder.Rows', 'StrViewBuilder.Sitrep', 'StrWords', 'StrOrdinals', 'StrGate', 'StrStrokes',
    'StrAnchors', 'StrPagePlan', 'StrPanelText', 'TheaterReadout'
) | ForEach-Object { "$repo/modules/Command/Domain/$_.cs" }
$contracts = @(
    'ITheaterWarView', 'TheaterWords', 'ISecondaryObjectivesView', 'OperationFocus', 'IWingSortieOrders', 'IThreatPicture'
) | ForEach-Object { "$repo/Core/Contracts/$_.cs" }
Copy-Item -LiteralPath ($panel + $domain + $contracts + @(
    "$PSScriptRoot/SettingsUnityStubs.cs", "$PSScriptRoot/StrFixtures.cs", "$PSScriptRoot/PanelGate.cs", "$PSScriptRoot/StrUnityCheck.cs", $UnityCheckHarness
)) -Destination "$PreviewDirectory/Assets/"
Copy-Item -LiteralPath "$PSScriptRoot/StrMockupSpec.json" -Destination "$PreviewDirectory/StrMockupSpec.json"
Copy-GameDlls "$PreviewDirectory/Assets/" -BepInExMatch '^(BepInEx|Mono|0Harmony)'
Copy-Item -LiteralPath "$repo/AvionicsUi/Assets/avionics-ui.bundle" -Destination "$PreviewDirectory/avionics-ui.bundle"

$method = if ($SelfTest) { 'StrUnityCheck.RunSelfTest' } else { 'StrUnityCheck.Run' }
$process = Invoke-UnityCheck $Unity $PreviewDirectory $method -Flags '-batchmode'
Write-Output "Results and renders: $PreviewDirectory"
Show-UnityCheckResult $PreviewDirectory -LogTail 80
if ($SelfTest) {
    $result = if (Test-Path -LiteralPath "$PreviewDirectory/result.txt") { Get-Content -LiteralPath "$PreviewDirectory/result.txt" -Raw } else { '' }
    if ($result -notmatch '\AFAIL: self-test' -or $result -notmatch 'overflow detected=True' -or $result -notmatch 'missing id detected=True') {
        throw "Self-test: the gate did not fail on both injected faults, so it cannot be trusted: $PreviewDirectory"
    }
    Write-Output 'SELFTEST OK: the gate failed on the oversized string and on the missing id.'
    return
}
Assert-UnityResult $PreviewDirectory $process -ExitMessage "Unity STR panel check failed: $($process.ExitCode)" -FailMessage "Unity exited without a successful result: $PreviewDirectory"
