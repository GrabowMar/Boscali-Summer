param(
    [string]$AtlasStyles,
    [string]$Unity = "C:/Program Files/Unity/Hub/Editor/2022.3.62f3/Editor/Unity.exe",
    [string]$PreviewDirectory = (Join-Path $env:TEMP ("BoscaliReliefCheck-" + [guid]::NewGuid().ToString("N"))),
    [string]$AssetName = "terrain2_map",
    [string]$MapImage,
    [switch]$UseUrp
)
$ErrorActionPreference = "Stop"
. "$PSScriptRoot/../../../UnityCheck.Common.ps1"
if ($AssetName -notin @("terrain2_map", "terrain_naval_map")) { throw "Unsupported test map: $AssetName" }
if (-not $MapImage) { $MapImage = Join-Path $env:TEMP "BoscaliMapSources/$AssetName.png" }
if (-not (Test-Path -LiteralPath $MapImage)) { throw "Real game map image missing: $MapImage" }
$heightAsset = Join-Path $repo "modules/Command/Assets/$AssetName.bmap"
if (-not (Test-Path -LiteralPath $heightAsset)) { throw "Baked heightfield missing: $heightAsset" }
$styleAsset = Join-Path $repo ("modules/Command/Assets/" + ($AssetName -replace '_map$', '') + "_intel.png")
if (-not (Test-Path -LiteralPath $styleAsset)) { throw "Baked style missing: $styleAsset" }
$env:BOSCALI_MAP_PREVIEW = $MapImage
$env:BOSCALI_HEIGHT_PREVIEW = $heightAsset
$env:BOSCALI_STYLE_PREVIEW = $styleAsset
$env:BOSCALI_ASSET_NAME = $AssetName
$env:BOSCALI_MAP_WIDTH = if ($AssetName -eq "terrain_naval_map") { "163840" } else { "81920" }
$env:BOSCALI_RELIEF_URP = if ($UseUrp) { "1" } else { "0" }
New-UnityCheckProject $PreviewDirectory '{"dependencies":{"com.unity.render-pipelines.universal":"14.0.12","com.unity.ugui":"1.0.0","com.unity.textmeshpro":"3.0.6","com.unity.modules.physics":"1.0.0","com.unity.modules.imageconversion":"1.0.0","com.unity.modules.audio":"1.0.0","com.unity.modules.uielements":"1.0.0","com.unity.modules.assetbundle":"1.0.0","com.unity.modules.imgui":"1.0.0"}}' -Folders 'NOAvionics'
# Match production's native field accessor rather than stubbing Harmony reflection.
Copy-GameDlls "$PreviewDirectory/Assets/" -BepInExMatch '^(Mono|0Harmony\.dll$|HarmonyXInterop)'
# The relief view and the context menu draw on kit v2 (AvFrame, AvText, AvControl, AvStyleHost ...), so the harness compiles the real kit sources
# instead of the old v1 kit/AvTheme stubs, like Run-RailUnityCheck.ps1 does.
Get-ChildItem -LiteralPath "$repo/AvionicsUi" -Filter '*.cs' -Recurse | Where-Object { $_.Name -notlike '*Tests.cs' } | Copy-Item -Destination "$PreviewDirectory/Assets/"
Copy-Item -LiteralPath "$repo/modules/Command/Presentation/MapUi/MfdTerrainRelief.cs", "$repo/modules/Command/Presentation/MapUi/MfdMapInteractions.cs", "$repo/modules/Command/Presentation/MapUi/MfdChromeLay.cs", "$repo/modules/Command/Presentation/MapUi/MfdMapOrbitControls.cs", "$repo/modules/Command/Presentation/MapUi/MapSymbology.cs", "$repo/modules/Command/Presentation/MapUi/MapSymbolAtlas.cs", "$repo/modules/Command/Domain/ReliefRig.cs", "$repo/modules/Command/Domain/ReliefHoles.cs", "$PSScriptRoot/ReliefUnityStubs.cs", "$PSScriptRoot/ReliefUnityCheck.cs" -Destination "$PreviewDirectory/Assets/"
Copy-Item -LiteralPath "$repo/Core/Contracts/IMapProjection.cs", "$repo/Core/Contracts/IMapBoxInput.cs" -Destination "$PreviewDirectory/Assets/"
Copy-Item -LiteralPath "$repo/modules/Command/Presentation/MapUi/AircraftTrailGraphic.cs" -Destination "$PreviewDirectory/Assets/"
Copy-Item -LiteralPath "$repo/modules/Command/Presentation/MapUi/MapUiManager.cs" -Destination "$PreviewDirectory/Assets/"
Copy-Item -LiteralPath "$repo/modules/Command/Domain/ReliefLight.cs", "$repo/modules/Command/Domain/ReliefClusterPlan.cs" -Destination "$PreviewDirectory/Assets/"
Copy-Item -LiteralPath "$repo/AvionicsUi/Assets/avionics-ui.bundle" -Destination "$PreviewDirectory/avionics-ui.bundle"
Copy-AvionicsStyles $PreviewDirectory $AtlasStyles
$process = Invoke-UnityCheck $Unity $PreviewDirectory 'ReliefUnityCheck.Run' -Flags '-batchmode'
Write-Output "Results and render: $PreviewDirectory"
Show-UnityCheckResult $PreviewDirectory
Assert-UnityResult $PreviewDirectory $process -ExitMessage "Unity relief check failed: $($process.ExitCode)"
