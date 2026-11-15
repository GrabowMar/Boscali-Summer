param(
    [string]$Unity = 'C:/Program Files/Unity/Hub/Editor/2022.3.62f3/Editor/Unity.exe',
    [string]$ProjectDirectory,
    [string]$OutputDirectory,
    [string]$OceanBaselineShader,
    [switch]$SkipRenderCheck,
    [int]$TimeoutSeconds = 300
)
$ErrorActionPreference = 'Stop'
$repoDirectory = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (-not $ProjectDirectory) { $ProjectDirectory = Join-Path $repoDirectory '.nomodkit/surface-assets' }
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $repoDirectory 'modules/Weather/Assets' }
$ProjectDirectory = [IO.Path]::GetFullPath($ProjectDirectory)
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
if (-not (Test-Path -LiteralPath $Unity -PathType Leaf)) { throw "Unity Editor not found: $Unity" }
if ($TimeoutSeconds -lt 1) { throw 'TimeoutSeconds must be positive.' }

function Copy-SurfaceInput([string]$Source, [string]$Destination) {
    if (-not (Test-Path -LiteralPath $Source -PathType Leaf)) { throw "Surface build input missing: $Source" }
    if (-not (Test-Path -LiteralPath $Destination) -or
        (Get-SurfaceHash $Source) -ne (Get-SurfaceHash $Destination)) {
        Copy-Item -LiteralPath $Source -Destination $Destination -Force
    }
}

function Get-SurfaceHash([string]$Path) {
    $hasher = [Security.Cryptography.SHA256]::Create()
    try { [Convert]::ToBase64String($hasher.ComputeHash([IO.File]::ReadAllBytes($Path))) }
    finally { $hasher.Dispose() }
}

Write-Output "Surface Unity project: $ProjectDirectory"
foreach ($folder in 'Assets/Surfaces', 'Assets/Editor', 'Packages', 'ProjectSettings', 'BundleOutput') {
    New-Item -ItemType Directory -Force -Path (Join-Path $ProjectDirectory $folder) | Out-Null
}
$manifest = '{"dependencies":{"com.unity.modules.assetbundle":"1.0.0","com.unity.modules.imageconversion":"1.0.0"}}'
$manifestPath = Join-Path $ProjectDirectory 'Packages/manifest.json'
if (-not (Test-Path -LiteralPath $manifestPath) -or (Get-Content -LiteralPath $manifestPath -Raw).Trim() -ne $manifest) {
    Set-Content -LiteralPath $manifestPath -Value $manifest -Encoding UTF8
}
Set-Content -LiteralPath (Join-Path $ProjectDirectory 'ProjectSettings/ProjectVersion.txt') -Value 'm_EditorVersion: 2022.3.62f3' -Encoding UTF8
foreach ($name in 'Ocean.shader', 'TerrainDetail.shader', 'terrain-detail.png') {
    Copy-SurfaceInput (Join-Path $repoDirectory "modules/Weather/Assets/Source/$name") (Join-Path $ProjectDirectory "Assets/Surfaces/$name")
}
Copy-SurfaceInput (Join-Path $PSScriptRoot 'SurfaceAssets/BuildSurfaceAssets.cs') (Join-Path $ProjectDirectory 'Assets/Editor/BuildSurfaceAssets.cs')
Copy-SurfaceInput (Join-Path $PSScriptRoot 'SurfaceAssets/OceanReflectionCheck.cs') (Join-Path $ProjectDirectory 'Assets/Editor/OceanReflectionCheck.cs')
if ($OceanBaselineShader) {
    $OceanBaselineShader = [IO.Path]::GetFullPath($OceanBaselineShader)
    $baselineDestination = Join-Path $ProjectDirectory 'Assets/Surfaces/OceanReflectionBaseline.shader'
    if ($OceanBaselineShader -ne $baselineDestination) { Copy-SurfaceInput $OceanBaselineShader $baselineDestination }
    elseif (-not (Test-Path -LiteralPath $OceanBaselineShader -PathType Leaf)) { throw "Ocean baseline missing: $OceanBaselineShader" }
}

$resultPath = Join-Path $ProjectDirectory 'result.txt'
if (Test-Path -LiteralPath $resultPath) { Remove-Item -LiteralPath $resultPath }
$logPath = Join-Path $ProjectDirectory 'build.log'
$arguments = @('-batchmode', '-projectPath', ('"' + $ProjectDirectory + '"'), '-executeMethod', 'BuildSurfaceAssets.Run', '-logFile', ('"' + $logPath + '"'))
if ($OceanBaselineShader) { $arguments += '-oceanBaselineShader', 'Assets/Surfaces/OceanReflectionBaseline.shader' }
if ($SkipRenderCheck) { $arguments += '-nographics', '-skipSurfaceRenderCheck' }
else { $arguments += '-force-d3d11' }
$process = Start-Process -FilePath $Unity -ArgumentList $arguments -WorkingDirectory $ProjectDirectory -WindowStyle Hidden -PassThru
try {
    if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
        $process.Kill()
        throw "Surface build exceeded $TimeoutSeconds seconds; inspect $logPath"
    }
    $result = if (Test-Path -LiteralPath $resultPath) { Get-Content -LiteralPath $resultPath -Raw } else { '' }
    if ($result) { Write-Output $result.Trim() }
    if ($process.ExitCode -ne 0 -or -not $result.StartsWith('PASS:')) {
        throw "Surface build failed (exit $($process.ExitCode)); inspect $logPath"
    }
}
finally { $process.Dispose() }

$bundlePath = Join-Path $ProjectDirectory 'BundleOutput/surfaces.bundle'
if (-not (Test-Path -LiteralPath $bundlePath) -or (Get-Item -LiteralPath $bundlePath).Length -gt 2MB) {
    throw 'Surface bundle missing or exceeds the 2 MiB cap.'
}
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
Copy-Item -LiteralPath $bundlePath -Destination (Join-Path $OutputDirectory 'surfaces.bundle') -Force
Write-Output "Surface bundle: $(Join-Path $OutputDirectory 'surfaces.bundle')"
