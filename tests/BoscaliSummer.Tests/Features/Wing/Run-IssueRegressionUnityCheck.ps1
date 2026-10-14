param(
    [string]$Unity = 'C:/Program Files/Unity/Hub/Editor/2022.3.62f3/Editor/Unity.exe',
    [string]$PreviewDirectory = (Join-Path $env:TEMP ('BoscaliIssues17-19-' + [guid]::NewGuid().ToString('N'))),
    [string]$ProductionDll
)
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/../../../UnityCheck.Common.ps1"
New-UnityCheckProject $PreviewDirectory $UnityCheckManifest.MfdFull -Folders 'Assets/Harness', 'NOAvionics'
Copy-GameDlls "$PreviewDirectory/Assets/" $UnityCheckFilter.ManagedWide $UnityCheckFilter.BepInEx
if (-not $ProductionDll) { $ProductionDll = "$repo/bin/Release/netstandard2.1/BoscaliSummer.dll" }
Copy-Item -LiteralPath $ProductionDll -Destination "$PreviewDirectory/Assets/BoscaliSummer.dll"
Copy-Item -LiteralPath "$PSScriptRoot/IssueRegressionUnityCheck.cs", $UnityCheckHarness, "$PSScriptRoot/BoscaliWingRadioPreview.asmdef" -Destination "$PreviewDirectory/Assets/Harness/"
Copy-AvionicsStyles $PreviewDirectory
Export-UnityCheckHashes "$PreviewDirectory/inputs.csv" @(Get-ChildItem "$PreviewDirectory/Assets/Harness" -File; Get-Item "$PreviewDirectory/Assets/BoscaliSummer.dll")
$process = Invoke-UnityCheck $Unity $PreviewDirectory 'IssueRegressionUnityCheck.Run' -TimeoutSeconds 240
Write-Output "Evidence: $PreviewDirectory"
Show-UnityCheckResult $PreviewDirectory
Assert-UnityResult $PreviewDirectory $process -ExitMessage "Unity issue regression failed: $($process.ExitCode)"
