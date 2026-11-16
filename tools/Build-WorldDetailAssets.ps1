param(
    [string]$Unity = 'C:/Program Files/Unity/Hub/Editor/2022.3.62f3/Editor/Unity.exe',
    [string]$ProjectDirectory,
    [string]$OutputDirectory,
    [Alias('SkipRenderCheck')][switch]$NoGraphics,
    [int]$TimeoutSeconds = 300
)
$ErrorActionPreference = 'Stop'
$repoDirectory = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (-not $ProjectDirectory) { $ProjectDirectory = Join-Path $repoDirectory '.nomodkit/world-detail-assets' }
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $repoDirectory 'modules/Immersion/Assets' }
$ProjectDirectory = [IO.Path]::GetFullPath($ProjectDirectory)
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
if (-not (Test-Path -LiteralPath $Unity -PathType Leaf)) { throw "Unity Editor not found: $Unity" }
if ($TimeoutSeconds -lt 1) { throw 'TimeoutSeconds must be positive.' }

function Copy-WorldDetailInput([string]$Source, [string]$Destination) {
    if (-not (Test-Path -LiteralPath $Source -PathType Leaf)) { throw "World detail build input missing: $Source" }
    if (-not (Test-Path -LiteralPath $Destination) -or
        (Get-FileHash -LiteralPath $Source -Algorithm SHA256).Hash -ne
        (Get-FileHash -LiteralPath $Destination -Algorithm SHA256).Hash) {
        Copy-Item -LiteralPath $Source -Destination $Destination -Force
    }
}

Write-Output "World detail Unity project: $ProjectDirectory"
foreach ($folder in 'Assets/WorldDetail', 'Assets/Editor', 'Packages', 'ProjectSettings', 'BundleOutput') {
    New-Item -ItemType Directory -Force -Path (Join-Path $ProjectDirectory $folder) | Out-Null
}
$manifest = '{"dependencies":{"com.unity.render-pipelines.universal":"14.0.12","com.unity.modules.assetbundle":"1.0.0","com.unity.modules.imageconversion":"1.0.0"}}'
$manifestPath = Join-Path $ProjectDirectory 'Packages/manifest.json'
if (-not (Test-Path -LiteralPath $manifestPath) -or (Get-Content -LiteralPath $manifestPath -Raw).Trim() -ne $manifest) {
    Set-Content -LiteralPath $manifestPath -Value $manifest -Encoding UTF8
}
Set-Content -LiteralPath (Join-Path $ProjectDirectory 'ProjectSettings/ProjectVersion.txt') -Value 'm_EditorVersion: 2022.3.62f3' -Encoding UTF8
Copy-WorldDetailInput (Join-Path $repoDirectory 'modules/Immersion/Assets/Source/WorldDetail.shader') (Join-Path $ProjectDirectory 'Assets/WorldDetail/WorldDetail.shader')
Copy-WorldDetailInput (Join-Path $PSScriptRoot 'WorldDetailAssets/BuildWorldDetailAssets.cs') (Join-Path $ProjectDirectory 'Assets/Editor/BuildWorldDetailAssets.cs')
Copy-WorldDetailInput (Join-Path $PSScriptRoot 'WorldDetailAssets/WorldDetailMeshCheck.cs') (Join-Path $ProjectDirectory 'Assets/Editor/WorldDetailMeshCheck.cs')
Copy-WorldDetailInput (Join-Path $repoDirectory 'modules/Immersion/Visuals/WorldDetailMeshes.cs') (Join-Path $ProjectDirectory 'Assets/Editor/WorldDetailMeshes.cs')

$resultPath = Join-Path $ProjectDirectory 'result.txt'
if (Test-Path -LiteralPath $resultPath) { Remove-Item -LiteralPath $resultPath }
$logPath = Join-Path $ProjectDirectory 'build.log'
$arguments = @('-batchmode', '-projectPath', ('"' + $ProjectDirectory + '"'), '-executeMethod', 'BuildWorldDetailAssets.Run', '-logFile', ('"' + $logPath + '"'))
if ($NoGraphics) { $arguments += '-nographics' }
else { $arguments += '-force-d3d11' }
$process = Start-Process -FilePath $Unity -ArgumentList $arguments -WorkingDirectory $ProjectDirectory -WindowStyle Hidden -PassThru
try {
    if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
        $process.Kill()
        throw "World detail build exceeded $TimeoutSeconds seconds; inspect $logPath"
    }
    $result = if (Test-Path -LiteralPath $resultPath) { Get-Content -LiteralPath $resultPath -Raw } else { '' }
    if ($result) { Write-Output $result.Trim() }
    if ($process.ExitCode -ne 0 -or -not $result.StartsWith('PASS:')) {
        throw "World detail build failed (exit $($process.ExitCode)); inspect $logPath"
    }
}
finally { $process.Dispose() }

$bundlePath = Join-Path $ProjectDirectory 'BundleOutput/world-detail.bundle'
if (-not (Test-Path -LiteralPath $bundlePath) -or (Get-Item -LiteralPath $bundlePath).Length -gt 512KB) {
    throw 'World detail bundle missing or exceeds the 512 KiB cap.'
}
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
Copy-Item -LiteralPath $bundlePath -Destination (Join-Path $OutputDirectory 'world-detail.bundle') -Force
Write-Output "World detail bundle: $(Join-Path $OutputDirectory 'world-detail.bundle')"
