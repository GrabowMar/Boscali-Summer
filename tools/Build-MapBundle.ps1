param(
    [string]$Source,
    [switch]$Kit,
    [string]$Unity = $(if ($env:NOMOD_UNITY) { $env:NOMOD_UNITY } else { 'C:/Program Files/Unity/Hub/Editor/2022.3.62f3/Editor/Unity.exe' }),
    [string]$ProjectDirectory,
    [int]$TimeoutSeconds = 1700
)
# Bakes the folder `nomod map gen` wrote into modules/Maps/Assets/<id>.bsmap (meshes, colliders, textures, no game types)
# with its sidecar <id>.map.json and card, and puts the relief heightfield and chart where the Command module embeds them.
# -Kit bakes the shared scenery kit modules/Maps/Assets/mapkit.bskit instead (tools/MapAssets/MapKitShapes.cs).
$ErrorActionPreference = 'Stop'
$repoDirectory = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (-not $ProjectDirectory) { $ProjectDirectory = Join-Path $repoDirectory '.nomodkit/map-assets' }
$ProjectDirectory = [IO.Path]::GetFullPath($ProjectDirectory)
if (-not (Test-Path -LiteralPath $Unity -PathType Leaf)) { throw "Unity Editor not found: $Unity" }
$assetsOut = Join-Path $repoDirectory 'modules/Maps/Assets'

function Copy-Input([string]$From, [string]$To) {
    if (-not (Test-Path -LiteralPath $From -PathType Leaf)) { throw "Map build input missing: $From" }
    Copy-Item -LiteralPath $From -Destination $To -Force
}

Write-Output "Map Unity project: $ProjectDirectory"
$mapAssets = Join-Path $ProjectDirectory 'Assets/Map'
if (Test-Path -LiteralPath $mapAssets) { Remove-Item -LiteralPath $mapAssets -Recurse -Force }
foreach ($folder in 'Assets/Map', 'Assets/Editor', 'Packages', 'ProjectSettings', 'BundleOutput') {
    New-Item -ItemType Directory -Force -Path (Join-Path $ProjectDirectory $folder) | Out-Null
}
$manifest = '{"dependencies":{"com.unity.render-pipelines.universal":"14.0.12","com.unity.modules.assetbundle":"1.0.0","com.unity.modules.imageconversion":"1.0.0","com.unity.modules.physics":"1.0.0"}}'
$manifestPath = Join-Path $ProjectDirectory 'Packages/manifest.json'
if (-not (Test-Path -LiteralPath $manifestPath) -or (Get-Content -LiteralPath $manifestPath -Raw).Trim() -ne $manifest) {
    Set-Content -LiteralPath $manifestPath -Value $manifest -Encoding UTF8
}
Set-Content -LiteralPath (Join-Path $ProjectDirectory 'ProjectSettings/ProjectVersion.txt') -Value 'm_EditorVersion: 2022.3.62f3' -Encoding UTF8
Copy-Input (Join-Path $repoDirectory 'modules/Maps/Assets/Source/MapSurface.shader') (Join-Path $mapAssets 'MapSurface.shader')
foreach ($script in 'BuildMapBundle.cs', 'MapKitShapes.cs') { Copy-Input (Join-Path $PSScriptRoot "MapAssets/$script") (Join-Path $ProjectDirectory "Assets/Editor/$script") }

$method = 'BuildMapBundle.RunKit'
$arguments = @()
if (-not $Kit) {
    if (-not $Source) { throw 'Give -Source <folder nomod map gen wrote>, or -Kit.' }
    $Source = [IO.Path]::GetFullPath($Source)
    $mapJson = Join-Path $Source 'map.json'
    if (-not (Test-Path -LiteralPath $mapJson -PathType Leaf)) { throw "No map.json in $Source; run nomod map gen first." }
    $map = Get-Content -LiteralPath $mapJson -Raw | ConvertFrom-Json
    $id = $map.id
    if ($id -notmatch '^[A-Za-z0-9_]+$') { throw "map.json has no usable id." }
    Copy-Input $mapJson (Join-Path $mapAssets 'map.json')
    for ($n = 0; $n -lt @($map.forests).Count; $n++) { Copy-Input (Join-Path $Source "forest_$n.bin") (Join-Path $mapAssets "forest_$n.bytes") }
    if (Test-Path -LiteralPath (Join-Path $Source 'scenery.bin')) { Copy-Input (Join-Path $Source 'scenery.bin') (Join-Path $mapAssets 'scenery.bytes') }
    foreach ($name in 'splat_extra.png', 'macro_ao.png', 'macro_normal.png') {
        if (Test-Path -LiteralPath (Join-Path $Source $name)) { Copy-Input (Join-Path $Source $name) (Join-Path $mapAssets $name) }
    }
    foreach ($name in 'ocean_depth.png', 'ocean_color.png', 'colormap.png', "bs_${id}_map.png", 'macro.png', 'splat_grass.png', 'splat_rock.png', 'splat_lush.png', 'grass_mask.png') {
        Copy-Input (Join-Path $Source $name) (Join-Path $mapAssets $name)
    }
    $method = 'BuildMapBundle.Run'
    $arguments = @('-mapSource', ('"' + $Source + '"'))
}

$resultPath = Join-Path $ProjectDirectory 'result.txt'
if (Test-Path -LiteralPath $resultPath) { Remove-Item -LiteralPath $resultPath }
$logPath = Join-Path $ProjectDirectory 'build.log'
$arguments = @('-batchmode', '-nographics', '-projectPath', ('"' + $ProjectDirectory + '"'), '-executeMethod', $method, '-logFile', ('"' + $logPath + '"')) + $arguments
$process = Start-Process -FilePath $Unity -ArgumentList $arguments -WorkingDirectory $ProjectDirectory -WindowStyle Hidden -PassThru
try {
    if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
        $process.Kill()
        throw "Map bake exceeded $TimeoutSeconds seconds; inspect $logPath"
    }
    $result = if (Test-Path -LiteralPath $resultPath) { Get-Content -LiteralPath $resultPath -Raw } else { '' }
    if ($result) { Write-Output $result.Trim() }
    if ($process.ExitCode -ne 0 -or -not $result.StartsWith('PASS:')) {
        throw "Map bake failed (exit $($process.ExitCode)); inspect $logPath"
    }
}
finally { $process.Dispose() }

if ($Kit) {
    Copy-Input (Join-Path $ProjectDirectory 'BundleOutput/mapkit.bskit') (Join-Path $assetsOut 'mapkit.bskit')
    Write-Output "Scenery kit: $(Join-Path $assetsOut 'mapkit.bskit')"
    return
}
$bundlePath = Join-Path $ProjectDirectory "BundleOutput/$id.bsmap"
if (-not (Test-Path -LiteralPath $bundlePath) -or (Get-Item -LiteralPath $bundlePath).Length -gt 80MB) {
    throw 'Map bundle missing or exceeds the 80 MiB cap.'
}
Copy-Item -LiteralPath $bundlePath -Destination (Join-Path $assetsOut "$id.bsmap") -Force
# The sidecar is map.json plus the bundle's build id: the mod lists the map from it and checks the bundle against it.
$hash = (Get-FileHash -LiteralPath $bundlePath -Algorithm SHA256).Hash.Substring(0, 8).ToLowerInvariant()
$text = [IO.File]::ReadAllText($mapJson).TrimStart()
$text = '{"bundleHash": "' + $hash + '", "bundleBytes": ' + (Get-Item -LiteralPath $bundlePath).Length + ',' + $text.Substring(1)
[IO.File]::WriteAllText((Join-Path $assetsOut "$id.map.json"), $text, (New-Object Text.UTF8Encoding($false)))
Copy-Input (Join-Path $Source 'card.png') (Join-Path $assetsOut "$id.card.png")
Copy-Input (Join-Path $Source 'relief.bmap') (Join-Path $repoDirectory "modules/Command/Assets/bs_${id}_map.bmap")
Copy-Input (Join-Path $Source 'relief_intel.png') (Join-Path $repoDirectory "modules/Command/Assets/bs_${id}_intel.png")
Write-Output "Map bundle: $(Join-Path $assetsOut "$id.bsmap") (build $hash)"
