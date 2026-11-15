param(
    [string]$Unity = 'C:/Program Files/Unity/Hub/Editor/2022.3.62f3/Editor/Unity.exe',
    [string]$SourceDir = (Join-Path $PSScriptRoot '../modules/QoL/Assets/Source'),
    [string]$OutputDir = (Join-Path $PSScriptRoot '../modules/QoL/Assets'),
    [string]$EvidenceDir
)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (-not (Test-Path -LiteralPath $Unity)) { throw "Unity Editor not found: $Unity" }
if (-not $EvidenceDir) { $EvidenceDir = Join-Path $repo '.nomodkit/nightvision-assets' }
$fixture = [IO.Path]::GetFullPath($EvidenceDir)
foreach ($folder in 'Assets/NightVision', 'Assets/Editor', 'BundleOutput', 'ProjectSettings', 'Packages') {
    New-Item -ItemType Directory -Force -Path (Join-Path $fixture $folder) | Out-Null
}
Set-Content -LiteralPath "$fixture/ProjectSettings/ProjectVersion.txt" -Value 'm_EditorVersion: 2022.3.62f3'
Set-Content -LiteralPath "$fixture/Packages/manifest.json" -Value '{"dependencies":{"com.unity.modules.assetbundle":"1.0.0","com.unity.modules.imageconversion":"1.0.0"}}'
$sources = @('NvgPhosphor.shader', 'NvgHeat.shader') | ForEach-Object { Join-Path $SourceDir $_ }
foreach ($source in $sources) {
    if (-not (Test-Path -LiteralPath $source)) { throw "Required shader source missing: $source" }
    Copy-Item -LiteralPath $source -Destination "$fixture/Assets/NightVision/" -Force
}
$check = Join-Path $PSScriptRoot 'NightVisionAssets/NightVisionShaderCheck.cs'
Copy-Item -LiteralPath $check -Destination "$fixture/Assets/Editor/" -Force
($sources + @($check)) | Get-FileHash | Select-Object Hash, Path | Export-Csv -LiteralPath "$fixture/source-hashes.csv" -NoTypeInformation
Write-Output "Night vision shader compiler: $fixture"
$result = Join-Path $fixture 'result.txt'
if (Test-Path -LiteralPath $result) { Remove-Item -LiteralPath $result }
$arguments = @('-batchmode', '-disable-assembly-updater', '-force-d3d11', '-projectPath', ('"' + $fixture + '"'),
    '-executeMethod', 'NightVisionShaderCheck.Run', '-logFile', ('"' + "$fixture/check.log" + '"'))
$process = Start-Process -FilePath $Unity -ArgumentList $arguments -WorkingDirectory $fixture -WindowStyle Hidden -PassThru
if (-not $process.WaitForExit(300000)) { $process.Kill(); throw "Night vision compiler timed out: $fixture/check.log" }
if (Test-Path -LiteralPath $result) { Get-Content -LiteralPath $result }
if ($process.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $result) -or
    (Get-Content -LiteralPath $result -Raw) -notmatch '\APASS:') { throw "Night vision shader build failed: $fixture/check.log" }
$bundle = Join-Path $fixture 'BundleOutput/nightvision.bundle'
if ((Get-Item -LiteralPath $bundle).Length -gt 512KB) { throw 'nightvision.bundle exceeds its 512 KiB loader limit' }
New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null
Copy-Item -LiteralPath $bundle -Destination (Join-Path $OutputDir 'nightvision.bundle') -Force
Write-Output "Night vision bundle: $(Join-Path $OutputDir 'nightvision.bundle')"
Write-Output "Evidence: $fixture"
