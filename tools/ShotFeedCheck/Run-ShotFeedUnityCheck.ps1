param(
    [string]$Unity = 'C:/Program Files/Unity/Hub/Editor/2022.3.62f3/Editor/Unity.exe',
    [string]$PreviewDirectory = (Join-Path $env:TEMP ('BoscaliShotFeed-' + [guid]::NewGuid().ToString('N'))),
    [string]$ProductionDll = (Join-Path $PSScriptRoot '../../bin/Release/netstandard2.1/BoscaliSummer.dll')
)
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/../UnityCheck.Common.ps1"
New-UnityCheckProject $PreviewDirectory $UnityCheckManifest.MfdFull -Folders 'Assets/Harness'
Copy-GameDlls "$PreviewDirectory/Assets" -ManagedExclude $UnityCheckFilter.ManagedWide -BepInExMatch $UnityCheckFilter.BepInEx
Copy-Item -LiteralPath $ProductionDll -Destination "$PreviewDirectory/Assets/BoscaliSummer.dll"
Copy-Item -LiteralPath "$PSScriptRoot/ShotFeedUnityCheck.cs" -Destination "$PreviewDirectory/Assets/Harness/"
Set-Content -LiteralPath "$PreviewDirectory/Assets/Harness/BoscaliShotFeedCheck.asmdef" -Value '{"name":"BoscaliShotFeedCheck","references":[],"includePlatforms":["Editor"]}'
Export-UnityCheckHashes "$PreviewDirectory/inputs.csv" (Get-Item "$PreviewDirectory/Assets/BoscaliSummer.dll", "$PreviewDirectory/Assets/Harness/ShotFeedUnityCheck.cs")
$process = Invoke-UnityCheck $Unity $PreviewDirectory 'ShotFeedUnityCheck.Run' -Flags @('-batchmode', '--burst-disable-compilation', '-disable-assembly-updater') -TimeoutSeconds 240
Write-Output "Evidence: $PreviewDirectory"
Show-UnityCheckResult $PreviewDirectory
Assert-UnityResult $PreviewDirectory $process -ExitMessage "SHOT feed check failed: $PreviewDirectory"
