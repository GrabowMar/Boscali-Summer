param(
    [Parameter(Mandatory)][string]$ProjectDirectory,
    [string]$Unity = $(if ($env:NOMOD_UNITY) { $env:NOMOD_UNITY } else { 'C:/Program Files/Unity/Hub/Editor/2022.3.62f3/Editor/Unity.exe' })
)

$ErrorActionPreference = 'Stop'
$project = [IO.Path]::GetFullPath($ProjectDirectory)
$sourceAssets = Split-Path -Path $PSScriptRoot -Parent
foreach ($directory in 'Assets/Map/SceneryInstancing', 'Assets/Editor', 'Packages', 'ProjectSettings') {
    New-Item -ItemType Directory -Path (Join-Path $project $directory) -Force | Out-Null
}
Set-Content -LiteralPath (Join-Path $project 'ProjectSettings/ProjectVersion.txt') -Value 'm_EditorVersion: 2022.3.62f3' -Encoding utf8
$manifest = '{"dependencies":{"com.unity.render-pipelines.universal":"14.0.12","com.unity.modules.assetbundle":"1.0.0"}}'
Set-Content -LiteralPath (Join-Path $project 'Packages/manifest.json') -Value $manifest -Encoding utf8
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'MapSceneryInstanced.shader') -Destination (Join-Path $project 'Assets/Map/SceneryInstancing/MapSceneryInstanced.shader') -Force
Copy-Item -LiteralPath (Join-Path $sourceAssets 'Editor/BuildSceneryInstancing.cs') -Destination (Join-Path $project 'Assets/Editor/BuildSceneryInstancing.cs') -Force
foreach ($foliage in 'PalmFoliage.png', 'ScrubFoliage.png') {
    $foliageSource = Join-Path $PSScriptRoot "Scenery/$foliage"
    Copy-Item -LiteralPath $foliageSource -Destination (Join-Path $project "Assets/Map/SceneryInstancing/$foliage") -Force
}
$log = Join-Path $project 'build-scenery-instancing.log'
$result = Join-Path $project 'result.txt'
Set-Content -LiteralPath $result -Value ''
$arguments = @('-batchmode', '-nographics', '-projectPath', ('"' + $project + '"'), '-executeMethod', 'BuildSceneryInstancing.Run', '-logFile', ('"' + $log + '"'))
$process = Start-Process -FilePath $Unity -ArgumentList $arguments -WorkingDirectory $project -WindowStyle Hidden -PassThru
try {
    if (-not $process.WaitForExit(12 * 60 * 1000)) {
        $process.Kill()
        throw "Scenery shader bake timed out. Log: $log"
    }
    $status = (Get-Content -LiteralPath $result -Raw).Trim()
    if ($process.ExitCode -ne 0 -or -not $status.StartsWith('PASS:')) {
        throw "Scenery shader bake failed (exit $($process.ExitCode)). Log: $log"
    }
    Copy-Item -LiteralPath (Join-Path $project 'BundleOutput/scenery-instancing.bundle') -Destination (Join-Path $sourceAssets 'scenery-instancing.bundle') -Force
    $status
}
finally { $process.Dispose() }
