param(
    [string]$AtlasStyles,
    [string]$Unity = "C:/Program Files/Unity/Hub/Editor/2022.3.62f3/Editor/Unity.exe",
    [string]$PreviewDirectory = (Join-Path $env:TEMP ("BoscaliRailCheck-" + [guid]::NewGuid().ToString("N")))
)
$ErrorActionPreference = "Stop"
. "$PSScriptRoot/../../../UnityCheck.Common.ps1"
# com.unity.modules.assetbundle/imgui: AvBundle.cs (kit v2's font/asset lookup, pulled in by
# AvIcons) needs UnityEngine.AssetBundle, which this harness never had to load before the rail
# moved off MfdGlyph onto AvIcons. Matches the manifest already used by Run-KitGallery.ps1 /
# Run-AvBundleUnityCheck.ps1 for the same reason.
New-UnityCheckProject $PreviewDirectory '{"dependencies":{"com.unity.ugui":"1.0.0","com.unity.textmeshpro":"3.0.6","com.unity.modules.audio":"1.0.0","com.unity.modules.imageconversion":"1.0.0","com.unity.modules.uielements":"1.0.0","com.unity.modules.assetbundle":"1.0.0","com.unity.modules.imgui":"1.0.0"}}' -Folders 'NOAvionics'
# -Recurse also picks up AvionicsUi/Fui/*.cs (kit v2: AvLay, AvText, AvFrame's siblings, ...) that
# MfdRail.cs and MfdChromeLay.cs now depend on since the rail moved onto kit v2 primitives.
Get-ChildItem -LiteralPath "$repo/AvionicsUi" -Filter '*.cs' -Recurse | Where-Object { $_.Name -notlike '*Tests.cs' } | Copy-Item -Destination "$PreviewDirectory/Assets/"
Copy-Item -LiteralPath "$repo/modules/Command/Presentation/MapUi/MfdLayout.cs", "$repo/modules/Command/Presentation/MapUi/MfdRail.cs", "$repo/modules/Command/Presentation/MapUi/MfdRailCatalog.cs", "$repo/modules/Command/Presentation/MapUi/MfdChromeLay.cs", "$repo/modules/Command/Presentation/MapUi/MfdGlyph.cs", "$repo/modules/Command/Presentation/MapUi/MfdScreenFinish.cs", "$PSScriptRoot/SettingsUnityStubs.cs", "$PSScriptRoot/RailUnityCheck.cs" -Destination "$PreviewDirectory/Assets/"
Copy-GameDlls "$PreviewDirectory/Assets/" -BepInExMatch '^(BepInEx|Mono|0Harmony)'
Copy-Item -LiteralPath "$repo/AvionicsUi/Assets/avionics-ui.bundle" -Destination "$PreviewDirectory/avionics-ui.bundle"
Copy-AvionicsStyles $PreviewDirectory $AtlasStyles -NoDefaults
$process = Invoke-UnityCheck $Unity $PreviewDirectory 'RailUnityCheck.Run' -Flags '-batchmode'
Write-Output "Results and renders: $PreviewDirectory"
Show-UnityCheckResult $PreviewDirectory
Assert-UnityResult $PreviewDirectory $process -ExitMessage "Unity rail check failed: $($process.ExitCode)"
