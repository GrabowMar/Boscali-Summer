param(
    [string]$Unity = 'C:/Program Files/Unity/Hub/Editor/2022.3.62f3/Editor/Unity.exe',
    [string]$PreviewDirectory = (Join-Path $env:TEMP ('BoscaliBlueprinterMaterial-' + [guid]::NewGuid().ToString('N'))),
    [string]$ProductionDll = (Join-Path $PSScriptRoot '../../bin/Release/netstandard2.1/BoscaliSummer.dll'),
    [string]$BlueprinterDll = 'C:/Program Files (x86)/Steam/steamapps/common/Nuclear Option/BepInEx/plugins/com.nikkorap.blueprinter/Blueprinter_2.0.1.dll'
)
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/../UnityCheck.Common.ps1"
New-UnityCheckProject $PreviewDirectory $UnityCheckManifest.MfdFull -Folders 'Assets/Harness'
Copy-GameDlls "$PreviewDirectory/Assets" -ManagedExclude $UnityCheckFilter.ManagedWide -BepInExMatch $UnityCheckFilter.BepInEx
Copy-Item -LiteralPath $ProductionDll -Destination "$PreviewDirectory/Assets/BoscaliSummer.dll"
Copy-Item -LiteralPath $BlueprinterDll -Destination "$PreviewDirectory/Assets/Blueprinter_2.0.1.dll"
Copy-Item -LiteralPath "$PSScriptRoot/BlueprinterMaterialBindingsCheck.cs", "$PSScriptRoot/BlueprinterMaterialCheck.shader" -Destination "$PreviewDirectory/Assets/Harness/"
Set-Content -LiteralPath "$PreviewDirectory/Assets/Harness/BoscaliBlueprinterMaterialCheck.asmdef" -Value '{"name":"BoscaliBlueprinterMaterialCheck","references":[],"includePlatforms":["Editor"]}'
Export-UnityCheckHashes "$PreviewDirectory/inputs.csv" (Get-Item "$PreviewDirectory/Assets/BoscaliSummer.dll", "$PreviewDirectory/Assets/Blueprinter_2.0.1.dll", "$PreviewDirectory/Assets/Harness/BlueprinterMaterialBindingsCheck.cs", "$PreviewDirectory/Assets/Harness/BlueprinterMaterialCheck.shader")
$process = Invoke-UnityCheck $Unity $PreviewDirectory 'BlueprinterMaterialBindingsCheck.Run' -Flags @('-batchmode', '--burst-disable-compilation', '-disable-assembly-updater') -TimeoutSeconds 240
Write-Output "Evidence: $PreviewDirectory"
Show-UnityCheckResult $PreviewDirectory
Assert-UnityResult $PreviewDirectory $process -ExitMessage "Blueprinter material check failed: $PreviewDirectory"
