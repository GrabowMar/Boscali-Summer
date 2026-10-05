param(
    [string]$Unity = 'C:/Program Files/Unity/Hub/Editor/2022.3.62f3/Editor/Unity.exe',
    [string]$EvidenceDir = '',
    [switch]$PrepareOnly
)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '../../../..')).Path
$fixture = if ($EvidenceDir) { [IO.Path]::GetFullPath($EvidenceDir) } else {
    Join-Path $env:TEMP ('BoscaliCloudDensity-' + [guid]::NewGuid().ToString('N'))
}
if (Test-Path -LiteralPath $fixture) { throw "Evidence directory already exists: $fixture" }
if (-not $PrepareOnly -and -not (Test-Path -LiteralPath $Unity)) { throw "Unity Editor not found: $Unity" }
New-Item -ItemType Directory -Force "$fixture/Assets/Resources", "$fixture/Assets/Code", "$fixture/Packages", "$fixture/ProjectSettings" | Out-Null
Set-Content "$fixture/ProjectSettings/ProjectVersion.txt" 'm_EditorVersion: 2022.3.62f3'
Set-Content "$fixture/Packages/manifest.json" '{"dependencies":{"com.unity.modules.imageconversion":"1.0.0"}}'
Copy-Item "$repo/modules/Weather/Domain/*.cs" "$fixture/Assets/Code/"
Copy-Item "$repo/Core/Math/Deterministic.cs" "$fixture/Assets/Code/"
foreach ($f in 'CloudNoise3D.cs', 'CloudMaps.cs', 'CloudBodies.cs', 'CloudVolumeUniforms.cs') {
    Copy-Item "$repo/modules/Weather/Visuals/$f" "$fixture/Assets/Code/"
}
Copy-Item "$PSScriptRoot/WeatherCloudDensityCheck.cs" "$fixture/Assets/Code/"
$sourcePath = "$repo/modules/Weather/Assets/Source/FlightCloud.shader"
$source = Get-Content -LiteralPath $sourcePath -Raw
$includes = [regex]::Matches($source, '(?s)HLSLINCLUDE\s*(.*?)\s*ENDHLSL')
if ($includes.Count -ne 1) { throw 'Expected exactly one production HLSLINCLUDE block' }
$template = Get-Content -LiteralPath "$PSScriptRoot/CloudDensityProbe.shader.txt" -Raw
$shader = $template.Replace('/*__WEATHER_DENSITY_INCLUDE__*/', $includes[0].Groups[1].Value)
Set-Content -LiteralPath "$fixture/Assets/Resources/CloudDensityProbe.shader" -Value $shader -NoNewline -Encoding UTF8
Copy-Item -LiteralPath $sourcePath -Destination "$fixture/input-FlightCloud.shader"
Get-ChildItem "$fixture/Assets" -Recurse -File | Get-FileHash |
    Select-Object Hash, Path | Export-Csv "$fixture/input-manifest.csv" -NoTypeInformation
Get-FileHash -LiteralPath "$fixture/input-FlightCloud.shader" |
    Select-Object Hash, Path | Export-Csv "$fixture/production-shader-manifest.csv" -NoTypeInformation
Write-Output "Density fixture: $fixture"
if ($PrepareOnly) { Write-Output 'Prepared source only; no editor/player launched'; return }
$editor = Start-Process $Unity -ArgumentList @('-batchmode','-projectPath',('"'+$fixture+'"'),'-executeMethod','WeatherCloudDensityCheck.Build','-logFile',('"'+"$fixture/build.log"+'"')) -WorkingDirectory $fixture -WindowStyle Hidden -PassThru
$editor.WaitForExit()
if ($editor.ExitCode -ne 0 -or -not (Test-Path "$fixture/Player/WeatherCloudDensityCheck.exe")) {
    if (Test-Path "$fixture/build-result.txt") { Get-Content "$fixture/build-result.txt" }
    throw "Density fixture build failed: $fixture/build.log"
}
$player = Start-Process "$fixture/Player/WeatherCloudDensityCheck.exe" -ArgumentList @('-batchmode','-screen-width','640','-screen-height','360','-logFile',('"'+"$fixture/player.log"+'"')) -WorkingDirectory $fixture -WindowStyle Hidden -PassThru
if (-not $player.WaitForExit(180000)) { $player.Kill(); throw "Density fixture timeout: $fixture/player.log" }
$result = Get-Content "$fixture/result.txt" -Raw
Write-Output $result
if ($player.ExitCode -ne 0 -or $result -match '(?m)^FAIL ' -or $result -notmatch 'PASS density' -or
    $result -notmatch 'PASS rain optics 8' -or $result -notmatch 'PASS ray composition 10' -or
    $result -notmatch 'PASS slab footprint 18' -or $result -notmatch 'PASS air optics 12') {
    throw "Density fixture failed: $fixture/player.log"
}
