param([string]$Unity, [string]$Out, [string]$AtlasStyles, [string]$ModDll, [string]$PreviewDirectory)
if (-not $Unity) { $Unity = 'C:/Program Files/Unity/Hub/Editor/2022.3.62f3/Editor/Unity.exe' }
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/../../../UnityCheck.Common.ps1"
$proj = if ($PreviewDirectory) { [IO.Path]::GetFullPath($PreviewDirectory) } else { Join-Path $env:TEMP ('BoscaliComms-' + [guid]::NewGuid().ToString('N')) }
New-UnityCheckProject $proj $UnityCheckManifest.KitFull -Folders 'Assets/Harness', 'NOAvionics'
if (-not $ModDll) { $ModDll = "$repo/bin/Release/netstandard2.1/BoscaliSummer.dll" }
Copy-Item -LiteralPath $ModDll -Destination "$proj/Assets/BoscaliSummer.dll"
Set-Content -LiteralPath "$proj/dll-sha256.txt" -Value (Get-FileHash -LiteralPath $ModDll).Hash
Copy-Item "$PSScriptRoot/CommsUnityCheck.cs", "$PSScriptRoot/BoscaliCommsPreview.asmdef" "$proj/Assets/Harness/"
Copy-Item "$repo/AvionicsUi/Assets/avionics-ui.bundle" "$proj/"
Copy-GameDlls "$proj/Assets/" $UnityCheckFilter.Managed $UnityCheckFilter.BepInEx
Copy-AvionicsStyles $proj $AtlasStyles -NoDefaults
$p = Invoke-UnityCheck $Unity $proj 'CommsUnityCheck.Run'
Show-UnityCheckResult $proj -LogTail 80
Write-Output "Renders: $proj/com"
if ($Out -and (Test-Path "$proj/com")) {
    New-Item -ItemType Directory -Force -Path $Out | Out-Null
    Copy-Item "$proj/com/*.png", "$proj/result.txt", "$proj/dll-sha256.txt" -Destination $Out
    Write-Output "Saved renders: $Out"
}
Assert-UnityResult $proj $p -Pattern '(?m)^PASS: COM ' -ExitMessage "COM console check failed: $($p.ExitCode) ($proj)" -FailMessage "COM console check did not report a pass: $proj"
