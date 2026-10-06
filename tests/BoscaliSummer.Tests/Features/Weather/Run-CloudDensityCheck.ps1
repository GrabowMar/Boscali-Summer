param(
    [string]$Unity = 'C:/Program Files/Unity/Hub/Editor/2022.3.62f3/Editor/Unity.exe',
    [string]$EvidenceDir = '',
    [switch]$PrepareOnly
)
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/../../../UnityCheck.Common.ps1"
$fixture = Resolve-UnityCheckDir $EvidenceDir 'BoscaliCloudDensity-'
if (Test-Path -LiteralPath $fixture) { throw "Evidence directory already exists: $fixture" }
if (-not $PrepareOnly -and -not (Test-Path -LiteralPath $Unity)) { throw "Unity Editor not found: $Unity" }
New-UnityCheckProject $fixture '{"dependencies":{"com.unity.modules.imageconversion":"1.0.0"}}' -Folders 'Assets/Resources', 'Assets/Code'
Copy-Item "$repo/modules/Weather/Domain/*.cs" "$fixture/Assets/Code/"
Copy-Item "$repo/Core/Math/Deterministic.cs", "$repo/Core/Math/Scalar.cs" "$fixture/Assets/Code/"
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
Export-UnityCheckHashes "$fixture/input-manifest.csv" (Get-ChildItem "$fixture/Assets" -Recurse -File)
Export-UnityCheckHashes "$fixture/production-shader-manifest.csv" (Get-Item -LiteralPath "$fixture/input-FlightCloud.shader")
Write-Output "Density fixture: $fixture"
if ($PrepareOnly) { Write-Output 'Prepared source only; no editor/player launched'; return }
$editor = Invoke-UnityCheck $Unity $fixture 'WeatherCloudDensityCheck.Build' -Flags '-batchmode' -LogName 'build.log'
if ($editor.ExitCode -ne 0 -or -not (Test-Path "$fixture/Player/WeatherCloudDensityCheck.exe")) {
    if (Test-Path "$fixture/build-result.txt") { Get-Content "$fixture/build-result.txt" }
    throw "Density fixture build failed: $fixture/build.log"
}
$player = Invoke-UnityPlayer "$fixture/Player/WeatherCloudDensityCheck.exe" @('-batchmode', '-screen-width', '640', '-screen-height', '360', '-logFile', ('"' + "$fixture/player.log" + '"')) $fixture `
    -TimeoutSeconds 180 -TimeoutMessage "Density fixture timeout: $fixture/player.log"
$result = Get-Content "$fixture/result.txt" -Raw
Write-Output $result
if ($player.ExitCode -ne 0 -or $result -match '(?m)^FAIL ' -or $result -notmatch 'PASS density' -or
    $result -notmatch 'PASS rain optics 8' -or $result -notmatch 'PASS ray composition 10' -or
    $result -notmatch 'PASS slab footprint 18' -or $result -notmatch 'PASS air optics 12') {
    throw "Density fixture failed: $fixture/player.log"
}
