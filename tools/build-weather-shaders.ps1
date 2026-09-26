# Builds the Weather module's shader AssetBundle from tools/WeatherShaders and copies it to
# modules/Weather/Assets/boscali_weather.bundle, which the plugin embeds. Run it after changing
# any shader; commit the resulting bundle so an ordinary `dotnet build` never needs Unity.
param(
    [string]$Unity = "C:/Program Files/Unity/Hub/Editor/2022.3.62f3/Editor/Unity.exe"
)
$ErrorActionPreference = "Stop"
$repo = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$project = Join-Path $repo "tools/WeatherShaders"
$out = Join-Path $project "Build"
$log = Join-Path $project "Logs/build-weather-shaders.log"
$target = Join-Path $repo "modules/Weather/Assets/boscali_weather.bundle"

if (-not (Test-Path -LiteralPath $Unity)) { throw "Unity 2022.3.62 not found at $Unity" }
New-Item -ItemType Directory -Force -Path (Split-Path $log), $out, (Split-Path $target) | Out-Null
if (Test-Path -LiteralPath (Join-Path $out "boscali_weather")) { Remove-Item -LiteralPath (Join-Path $out "boscali_weather") }

$arguments = @(
    '-batchmode', '-nographics',
    '-projectPath', ('"' + $project + '"'),
    '-buildTarget', 'StandaloneWindows64',
    '-executeMethod', 'BuildWeatherBundle.Build',
    '-bundleOut', ('"' + $out + '"'),
    '-logFile', ('"' + $log + '"')
)
$process = Start-Process -FilePath $Unity -ArgumentList $arguments -WorkingDirectory $project -WindowStyle Hidden -PassThru
$process.WaitForExit()

Get-Content -LiteralPath $log | Select-String -Pattern '\[WeatherShaders\]|error CS|Shader error|Compiling shader' | ForEach-Object { $_.Line }
if ($process.ExitCode -ne 0) { throw "Unity bundle build failed ($($process.ExitCode)); see $log" }

Copy-Item -LiteralPath (Join-Path $out "boscali_weather") -Destination $target -Force
$size = (Get-Item -LiteralPath $target).Length
Write-Output ("Bundle: {0} ({1:N0} bytes)" -f $target, $size)
