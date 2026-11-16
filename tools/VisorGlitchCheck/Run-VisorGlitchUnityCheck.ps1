param(
    [string]$Unity = 'C:/Program Files/Unity/Hub/Editor/2022.3.62f3/Editor/Unity.exe',
    [string]$PreviewDirectory = (Join-Path $env:TEMP ('BoscaliVisorGlitch-' + [guid]::NewGuid().ToString('N'))),
    [string]$ProductionDll
)
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/../../tests/UnityCheck.Common.ps1"
New-UnityCheckProject $PreviewDirectory $UnityCheckManifest.MfdFull -Folders 'Assets/Harness'
Copy-GameDlls "$PreviewDirectory/Assets" -ManagedExclude $UnityCheckFilter.ManagedWide -BepInExMatch $UnityCheckFilter.BepInEx
if (-not $ProductionDll) { $ProductionDll = "$repo/bin/Release/netstandard2.1/BoscaliSummer.dll" }
Copy-Item -LiteralPath $ProductionDll -Destination "$PreviewDirectory/Assets/BoscaliSummer.dll"
Copy-Item -LiteralPath "$PSScriptRoot/VisorGlitchUnityCheck.cs" -Destination "$PreviewDirectory/Assets/Harness/"
Set-Content -LiteralPath "$PreviewDirectory/Assets/Harness/BoscaliVisorGlitchCheck.asmdef" -Value '{"name":"BoscaliVisorGlitchCheck","references":["Unity.TextMeshPro"],"includePlatforms":["Editor"]}'
Export-UnityCheckHashes "$PreviewDirectory/inputs.csv" (Get-Item "$PreviewDirectory/Assets/BoscaliSummer.dll", "$PreviewDirectory/Assets/Harness/VisorGlitchUnityCheck.cs")
$process = Invoke-UnityCheck $Unity $PreviewDirectory 'VisorGlitchUnityCheck.Run' -Flags @('-batchmode', '--burst-disable-compilation', '-disable-assembly-updater') -TimeoutSeconds 240
Write-Output "Evidence: $PreviewDirectory"
Show-UnityCheckResult $PreviewDirectory
Assert-UnityResult $PreviewDirectory $process -ExitMessage "Visor glitch check failed: $PreviewDirectory"
