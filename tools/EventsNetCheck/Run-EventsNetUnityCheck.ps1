param(
    [string]$Unity = 'C:/Program Files/Unity/Hub/Editor/2022.3.62f3/Editor/Unity.exe',
    [string]$PreviewDirectory = (Join-Path $env:TEMP ('BoscaliEventsNet-' + [guid]::NewGuid().ToString('N'))),
    [string]$ProductionDll = (Join-Path $PSScriptRoot '../../bin/Release/netstandard2.1/BoscaliSummer.dll')
)
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/../UnityCheck.Common.ps1"
New-UnityCheckProject $PreviewDirectory $UnityCheckManifest.MfdFull -Folders 'Assets/Harness', 'Assets/Fixtures'
Copy-GameDlls "$PreviewDirectory/Assets" -ManagedExclude $UnityCheckFilter.ManagedWide -BepInExMatch $UnityCheckFilter.BepInEx
Copy-Item -LiteralPath $ProductionDll -Destination "$PreviewDirectory/Assets/BoscaliSummer.dll"
Copy-Item -LiteralPath "$PSScriptRoot/EventsNetUnityCheck.cs" -Destination "$PreviewDirectory/Assets/Harness/"
Copy-Item -LiteralPath "$PSScriptRoot/EventsNativeManagerFixture.cs" -Destination "$PreviewDirectory/Assets/Fixtures/"
Set-Content -LiteralPath "$PreviewDirectory/Assets/Fixtures/BoscaliEventsNetFixtures.asmdef" -Value '{"name":"BoscaliEventsNetFixtures","references":[]}'
Set-Content -LiteralPath "$PreviewDirectory/Assets/Harness/BoscaliEventsNetCheck.asmdef" -Value '{"name":"BoscaliEventsNetCheck","references":["BoscaliEventsNetFixtures"],"includePlatforms":["Editor"]}'
Export-UnityCheckHashes "$PreviewDirectory/inputs.csv" (Get-Item "$PreviewDirectory/Assets/BoscaliSummer.dll", "$PreviewDirectory/Assets/Harness/EventsNetUnityCheck.cs", "$PreviewDirectory/Assets/Fixtures/EventsNativeManagerFixture.cs")
$process = Invoke-UnityCheck $Unity $PreviewDirectory 'EventsNetUnityCheck.Run' -Flags @('-batchmode', '--burst-disable-compilation', '-disable-assembly-updater') -TimeoutSeconds 240
Write-Output "Evidence: $PreviewDirectory"
Show-UnityCheckResult $PreviewDirectory
Assert-UnityResult $PreviewDirectory $process -ExitMessage "Events network check failed: $PreviewDirectory"
