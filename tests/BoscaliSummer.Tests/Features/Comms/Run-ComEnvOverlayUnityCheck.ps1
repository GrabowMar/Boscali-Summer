param([string]$Unity, [string]$Out, [string]$AtlasStyles, [string]$ModDll, [string]$PreviewDirectory)
if (-not $Unity) { $Unity = 'C:/Program Files/Unity/Hub/Editor/2022.3.62f3/Editor/Unity.exe' }
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/../../../UnityCheck.Common.ps1"
$proj = if ($PreviewDirectory) { [IO.Path]::GetFullPath($PreviewDirectory) } else { Join-Path $env:TEMP ('BoscaliComEnvOverlays-' + [guid]::NewGuid().ToString('N')) }
New-UnityCheckProject $proj $UnityCheckManifest.KitFull -Folders 'Assets/Harness', 'NOAvionics'
if (-not $ModDll) { $ModDll = "$repo/bin/Release/netstandard2.1/BoscaliSummer.dll" }
Copy-Item -LiteralPath $ModDll -Destination "$proj/Assets/BoscaliSummer.dll"
Set-Content -LiteralPath "$proj/dll-sha256.txt" -Value (Get-FileHash -LiteralPath "$proj/Assets/BoscaliSummer.dll").Hash
Copy-Item "$PSScriptRoot/ComEnvOverlayUnityCheck.cs", $UnityCheckHarness, "$PSScriptRoot/BoscaliCommsPreview.asmdef" "$proj/Assets/Harness/"
$taskFixtureHash = (Get-FileHash -LiteralPath "$proj/Assets/Harness/ComEnvOverlayUnityCheck.cs").Hash
Set-Content -LiteralPath "$proj/fixture-sha256.txt" -Value $taskFixtureHash
Copy-Item "$repo/AvionicsUi/Assets/avionics-ui.bundle" "$proj/"
Copy-GameDlls "$proj/Assets/" $UnityCheckFilter.Managed $UnityCheckFilter.BepInEx
Copy-AvionicsStyles $proj $AtlasStyles -NoDefaults
if (Test-Path -LiteralPath "$proj/geometry.txt") { Remove-Item -LiteralPath "$proj/geometry.txt" }
$p = Invoke-UnityCheck $Unity $proj 'ComEnvOverlayUnityCheck.Run'
Show-UnityCheckResult $proj -LogTail 80
Write-Output "Renders: $proj/renders"
if ($Out -and (Test-Path "$proj/renders")) {
    New-Item -ItemType Directory -Force -Path $Out | Out-Null
    Copy-Item "$proj/renders/*.png", "$proj/result.txt", "$proj/dll-sha256.txt", "$proj/fixture-sha256.txt" -Destination $Out
    Write-Output "Saved renders: $Out"
}
if (Test-Path -LiteralPath "$proj/geometry.txt") { Get-Content -LiteralPath "$proj/geometry.txt" }
if ((Get-FileHash -LiteralPath "$PSScriptRoot/ComEnvOverlayUnityCheck.cs").Hash -ne $taskFixtureHash) { throw 'Overlay fixture changed during render; rerun the settled source.' }
Assert-UnityResult $proj $p -Pattern '(?m)^PASS: COMENV OVERLAYS ' -ExitMessage "COM/ENV overlay check failed: $($p.ExitCode) ($proj)" -FailMessage "COM/ENV overlay check did not report a pass: $proj"
