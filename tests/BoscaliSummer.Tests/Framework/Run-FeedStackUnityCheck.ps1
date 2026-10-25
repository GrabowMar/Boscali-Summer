param(
    [string]$Unity = 'C:/Program Files/Unity/Hub/Editor/2022.3.62f3/Editor/Unity.exe',
    [string]$PreviewDirectory = (Join-Path $env:TEMP ('BoscaliFeedStack-' + [guid]::NewGuid().ToString('N'))),
    [string]$ProductionDll
)
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/../../UnityCheck.Common.ps1"
New-UnityCheckProject $PreviewDirectory $UnityCheckManifest.MfdFull -Folders 'Assets/Harness'
Copy-GameDlls "$PreviewDirectory/Assets/" $UnityCheckFilter.ManagedWide $UnityCheckFilter.BepInEx
if (-not $ProductionDll) { $ProductionDll = "$repo/bin/Release/netstandard2.1/BoscaliSummer.dll" }
Copy-Item -LiteralPath $ProductionDll -Destination "$PreviewDirectory/Assets/BoscaliSummer.dll"
Copy-Item -LiteralPath "$PSScriptRoot/FeedStackUnityCheck.cs" -Destination "$PreviewDirectory/Assets/Harness/"
Set-Content -LiteralPath "$PreviewDirectory/Assets/Harness/BoscaliFeedStackCheck.asmdef" -Value '{"name":"BoscaliFeedStackCheck","includePlatforms":["Editor"]}'
Export-UnityCheckHashes "$PreviewDirectory/inputs.csv" @(Get-Item "$PreviewDirectory/Assets/BoscaliSummer.dll", "$PreviewDirectory/Assets/Harness/FeedStackUnityCheck.cs", "$PreviewDirectory/Assets/Unity.RenderPipelines.Universal.Runtime.dll", "$PreviewDirectory/Assets/Unity.RenderPipelines.Core.Runtime.dll")
$process = Invoke-UnityCheck $Unity $PreviewDirectory 'FeedStackUnityCheck.Run' -Flags '-batchmode', '--burst-disable-compilation', '-disable-assembly-updater' -TimeoutSeconds 240
Write-Output "Evidence: $PreviewDirectory"
Show-UnityCheckResult $PreviewDirectory
Assert-UnityResult $PreviewDirectory $process -ExitMessage "Unity feed stack check failed: $($process.ExitCode)"
