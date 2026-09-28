param(
    [string]$Unity = "C:/Program Files/Unity/Hub/Editor/2022.3.62f3/Editor/Unity.exe",
    [string]$PreviewDirectory = (Join-Path $env:TEMP ("BoscaliReliefCheck-" + [guid]::NewGuid().ToString("N"))),
    [string]$AssetName = "terrain2_map",
    [string]$MapImage
)
$ErrorActionPreference = "Stop"
$repo = (Resolve-Path (Join-Path $PSScriptRoot "../../../..")).Path
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
New-Item -ItemType Directory -Force -Path "$PreviewDirectory/Assets", "$PreviewDirectory/ProjectSettings", "$PreviewDirectory/Packages" | Out-Null
Set-Content -LiteralPath "$PreviewDirectory/ProjectSettings/ProjectVersion.txt" -Value "m_EditorVersion: 2022.3.62f3"
Set-Content -LiteralPath "$PreviewDirectory/Packages/manifest.json" -Value '{"dependencies":{"com.unity.ugui":"1.0.0","com.unity.textmeshpro":"3.0.6","com.unity.modules.physics":"1.0.0","com.unity.modules.imageconversion":"1.0.0"}}'
Copy-Item -LiteralPath "$repo/modules/Command/Presentation/MapUi/MfdTerrainRelief.cs", "$repo/modules/Command/Presentation/MapUi/MfdMapInteractions.cs", "$repo/modules/Command/Domain/ReliefRig.cs", "$repo/modules/Command/Domain/ReliefHoles.cs", "$PSScriptRoot/ReliefUnityStubs.cs", "$PSScriptRoot/ReliefUnityCheck.cs" -Destination "$PreviewDirectory/Assets/"
Copy-Item -LiteralPath "$repo/Framework/Contracts/IMapProjection.cs", "$repo/Framework/Contracts/IMapBoxInput.cs" -Destination "$PreviewDirectory/Assets/"
$arguments = @('-batchmode', '-projectPath', ('"' + $PreviewDirectory + '"'), '-executeMethod', 'ReliefUnityCheck.Run', '-logFile', ('"' + "$PreviewDirectory/check.log" + '"'))
$process = Start-Process -FilePath $Unity -ArgumentList $arguments -WorkingDirectory $PreviewDirectory -WindowStyle Hidden -PassThru
$process.WaitForExit()
Write-Output "Results and render: $PreviewDirectory"
if (Test-Path "$PreviewDirectory/result.txt") { Get-Content "$PreviewDirectory/result.txt" }
else { Get-Content "$PreviewDirectory/check.log" -Tail 60 }
if ($process.ExitCode -ne 0) { throw "Unity relief check failed: $($process.ExitCode)" }
