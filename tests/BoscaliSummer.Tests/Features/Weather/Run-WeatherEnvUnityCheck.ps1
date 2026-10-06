param([string]$Unity, [string]$Out, [string]$AtlasStyles, [string]$ModDll, [string]$PreviewDirectory,
    [switch]$ShadowOwnershipOnly, [switch]$DisplayOwnershipOnly)
if ($ShadowOwnershipOnly -and $DisplayOwnershipOnly) { throw 'Choose one focused ownership fixture.' }
if (-not $Unity) { $Unity = 'C:/Program Files/Unity/Hub/Editor/2022.3.62f3/Editor/Unity.exe' }
if ($DisplayOwnershipOnly) {
    & "$PSScriptRoot/Run-ImmersionMaterialCheck.ps1" -Unity $Unity -ModDll $ModDll -EvidenceDirectory $PreviewDirectory
    return
}
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/../../../UnityCheck.Common.ps1"
$proj = Resolve-UnityCheckDir $PreviewDirectory 'BoscaliWeatherEnv-'
New-UnityCheckProject $proj $UnityCheckManifest.KitFull -Folders 'Assets/Harness', 'NOAvionics'
if (-not $ModDll) { $ModDll = "$repo/bin/Release/netstandard2.1/BoscaliSummer.dll" }
Copy-Item -LiteralPath $ModDll -Destination "$proj/Assets/BoscaliSummer.dll"
Set-Content -LiteralPath "$proj/dll-sha256.txt" -Value (Get-FileHash -LiteralPath $ModDll).Hash
Copy-Item "$PSScriptRoot/WeatherEnvUnityCheck.cs", "$PSScriptRoot/BoscaliWeatherEnv.asmdef" "$proj/Assets/Harness/"
Copy-Item "$PSScriptRoot/WeatherShadowOwnershipCheck.cs" "$proj/Assets/Harness/"
Copy-Item "$repo/AvionicsUi/Assets/avionics-ui.bundle" "$proj/"
Copy-GameDlls "$proj/Assets/" $UnityCheckFilter.Managed $UnityCheckFilter.BepInEx
Copy-AvionicsStyles $proj $AtlasStyles -NoDefaults
$resultFile = if ($ShadowOwnershipOnly) { 'shadow-result.txt' } else { 'result.txt' }
$entry = if ($ShadowOwnershipOnly) { 'WeatherShadowOwnershipCheck.Run' } else { 'WeatherEnvUnityCheck.Run' }
$flags = @('-batchmode', '-disable-assembly-updater')
if ($ShadowOwnershipOnly) { $flags += '-nographics' }
$p = Invoke-UnityCheck $Unity $proj $entry -Flags $flags -Result $resultFile
Show-UnityCheckResult $proj -Result $resultFile -LogTail 80
Write-Output "Renders: $proj/env"
if ($Out -and !$ShadowOwnershipOnly -and (Test-Path "$proj/env")) {
    New-Item -ItemType Directory -Force -Path $Out | Out-Null
    Copy-Item "$proj/env/*.png", "$proj/result.txt", "$proj/dll-sha256.txt" -Destination $Out
    Write-Output "Saved renders: $Out"
}
$expectedResult = if ($ShadowOwnershipOnly) { '(?m)^PASS: ' } else { '(?m)^PASS: ENV ' }
Assert-UnityResult $proj $p -Pattern $expectedResult -Result $resultFile -ExitMessage "Weather ENV console check failed: $($p.ExitCode) ($proj)" -FailMessage "Weather ENV console check did not report a pass: $proj"
