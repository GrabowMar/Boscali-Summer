param(
    [string]$Unity = "C:/Program Files/Unity/Hub/Editor/2022.3.62f3/Editor/Unity.exe",
    [string]$PreviewDirectory = (Join-Path $env:TEMP ("BoscaliEventsCheck-" + [guid]::NewGuid().ToString("N"))),
    [string]$AtlasStyles,
    [switch]$PrepareOnly
)
# Offline Unity gate for the EVN console and its field archive window: real presentation sources, stubbed game
# types, rendered pages plus the overlap / overflow / contrast gate from the kit gallery.
$ErrorActionPreference = "Stop"
. "$PSScriptRoot/../../../UnityCheck.Common.ps1"
New-UnityCheckProject $PreviewDirectory '{"dependencies":{"com.unity.ugui":"1.0.0","com.unity.textmeshpro":"3.0.6","com.unity.modules.audio":"1.0.0","com.unity.modules.imageconversion":"1.0.0","com.unity.modules.uielements":"1.0.0","com.unity.modules.assetbundle":"1.0.0","com.unity.modules.physics":"1.0.0"}}' -Folders 'NOAvionics', 'BepInEx/plugins/BoscaliSummer/Events'
Get-ChildItem -LiteralPath "$repo/AvionicsUi", "$repo/AvionicsUi/Pure" -Filter '*.cs' | Where-Object { $_.Name -notlike '*Tests.cs' } | Copy-Item -Destination "$PreviewDirectory/Assets/"
Get-ChildItem -LiteralPath "$repo/AvionicsUi/Fui" -Filter '*.cs' | Copy-Item -Destination "$PreviewDirectory/Assets/"
Copy-Item -LiteralPath "$repo/AvionicsUi/avionics.fui.avss", "$repo/AvionicsUi/avionics.steel.avss", "$repo/AvionicsUi/avionics.ace.avss", "$repo/AvionicsUi/avionics.phosphor.avss", "$repo/AvionicsUi/avionics.fieldops.avss", "$repo/AvionicsUi/avionics.amber.avss", "$repo/AvionicsUi/avionics.glass.avss", "$repo/AvionicsUi/avionics.nightops.avss" -Destination "$PreviewDirectory/NOAvionics/"
Copy-Item -LiteralPath "$repo/AvionicsUi/Assets/avionics-ui.bundle" -Destination "$PreviewDirectory/"
Get-ChildItem -LiteralPath "$repo/modules/Events/Assets/Posters" -Filter '*.png' | Copy-Item -Destination "$PreviewDirectory/BepInEx/plugins/BoscaliSummer/Events/"
Copy-Item -LiteralPath "$repo/Core/Util/EmbeddedResources.cs", "$repo/Core/Util/PngSprites.cs", "$repo/Core/Util/PngSprites.Unity.cs" -Destination "$PreviewDirectory/Assets/"
# The module's own presentation (no alert / tone), its pure domain, settings and the two shared files it reads.
Get-ChildItem -LiteralPath "$repo/modules/Events/Presentation" -Filter '*.cs' |
    Where-Object { $_.Name -notin @('SuperEventAlert.cs', 'EventAlertTone.cs') } |
    Copy-Item -Destination "$PreviewDirectory/Assets/"
Get-ChildItem -LiteralPath "$repo/modules/Events/Domain" -Filter '*.cs' | Copy-Item -Destination "$PreviewDirectory/Assets/"
Copy-Item -LiteralPath "$repo/modules/Events/Configuration/EventsSettings.cs", "$repo/Core/Contracts/IActiveEventsView.cs", "$repo/Core/Math/Deterministic.cs", "$repo/Core/Game/MfdPanelInstaller.cs", "$PSScriptRoot/EventsUnityCheck.cs", $UnityCheckHarness, "$PSScriptRoot/EventsUnityStubs.cs" -Destination "$PreviewDirectory/Assets/"
Copy-GameDlls "$PreviewDirectory/Assets/" -BepInExMatch '^(BepInEx\.dll$|Mono|0Harmony\.dll$)'
Copy-AvionicsStyles $PreviewDirectory $AtlasStyles -IfExists -NoDefaults
if ($PrepareOnly) { Write-Output "Prepared preview project: $PreviewDirectory"; return }
$process = Invoke-UnityCheck $Unity $PreviewDirectory 'EventsUnityCheck.Run' -Flags '-batchmode'
Write-Output "Results and renders: $PreviewDirectory"
Show-UnityCheckResult $PreviewDirectory
Assert-UnityResult $PreviewDirectory $process -Pattern '(?m)^PASS:' -ExitMessage "Unity events check failed: $($process.ExitCode)" -FailMessage "Unity events check did not report a pass: $PreviewDirectory"
