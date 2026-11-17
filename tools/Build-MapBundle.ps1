param(
    [Parameter(Mandatory = $true)][string]$Source,
    [string]$Unity = $(if ($env:NOMOD_UNITY) { $env:NOMOD_UNITY } else { 'C:/Program Files/Unity/Hub/Editor/2022.3.62f3/Editor/Unity.exe' }),
    [string]$ProjectDirectory,
    [int]$TimeoutSeconds = 1700
)
# Bakes the folder `nomod map gen` wrote into modules/Maps/Assets/<id>.bsmap (meshes, colliders, textures, no game types),
# and puts the relief heightfield and chart where the Command module embeds them.
$ErrorActionPreference = 'Stop'
$repoDirectory = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (-not $ProjectDirectory) { $ProjectDirectory = Join-Path $repoDirectory '.nomodkit/map-assets' }
$ProjectDirectory = [IO.Path]::GetFullPath($ProjectDirectory)
$Source = [IO.Path]::GetFullPath($Source)
if (-not (Test-Path -LiteralPath $Unity -PathType Leaf)) { throw "Unity Editor not found: $Unity" }
$mapJson = Join-Path $Source 'map.json'
if (-not (Test-Path -LiteralPath $mapJson -PathType Leaf)) { throw "No map.json in $Source; run nomod map gen first." }
$id = (Get-Content -LiteralPath $mapJson -Raw | ConvertFrom-Json).id
if ($id -notmatch '^[A-Za-z0-9_]+$') { throw "map.json has no usable id." }

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
Copy-Input (Join-Path $PSScriptRoot 'MapAssets/BuildMapBundle.cs') (Join-Path $ProjectDirectory 'Assets/Editor/BuildMapBundle.cs')
Copy-Input $mapJson (Join-Path $mapAssets 'map.json')
Copy-Input (Join-Path $Source 'trees.bin') (Join-Path $mapAssets 'trees.bytes')
foreach ($n in 0..0) { Copy-Input (Join-Path $Source "forest_$n.bin") (Join-Path $mapAssets "forest_$n.bytes") }
foreach ($name in 'ocean_depth.png', 'ocean_color.png', 'colormap.png', "bs_${id}_map.png", 'macro.png', 'splat_grass.png', 'splat_rock.png', 'splat_lush.png', 'grass_mask.png') {
    Copy-Input (Join-Path $Source $name) (Join-Path $mapAssets $name)
}

$resultPath = Join-Path $ProjectDirectory 'result.txt'
if (Test-Path -LiteralPath $resultPath) { Remove-Item -LiteralPath $resultPath }
$logPath = Join-Path $ProjectDirectory 'build.log'
$arguments = @('-batchmode', '-nographics', '-projectPath', ('"' + $ProjectDirectory + '"'), '-executeMethod', 'BuildMapBundle.Run',
    '-mapSource', ('"' + $Source + '"'), '-logFile', ('"' + $logPath + '"'))
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

$bundlePath = Join-Path $ProjectDirectory "BundleOutput/$id.bsmap"
if (-not (Test-Path -LiteralPath $bundlePath) -or (Get-Item -LiteralPath $bundlePath).Length -gt 80MB) {
    throw 'Map bundle missing or exceeds the 80 MiB cap.'
}
Copy-Item -LiteralPath $bundlePath -Destination (Join-Path $repoDirectory "modules/Maps/Assets/$id.bsmap") -Force
Copy-Input (Join-Path $Source 'relief.bmap') (Join-Path $repoDirectory "modules/Command/Assets/bs_${id}_map.bmap")
Copy-Input (Join-Path $Source 'relief_intel.png') (Join-Path $repoDirectory "modules/Command/Assets/bs_${id}_intel.png")
Write-Output "Map bundle: $(Join-Path $repoDirectory "modules/Maps/Assets/$id.bsmap")"
