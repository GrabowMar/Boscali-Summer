param(
    [string]$Unity = 'C:/Program Files/Unity/Hub/Editor/2022.3.62f3/Editor/Unity.exe',
    [string]$PreviewDirectory = (Join-Path $env:TEMP ('BoscaliDebugSweep-' + [guid]::NewGuid().ToString('N'))),
    [string]$ProductionDll
)
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/../../UnityCheck.Common.ps1"
New-UnityCheckProject $PreviewDirectory $UnityCheckManifest.MfdFull -Folders 'Assets/Harness'
Copy-GameDlls "$PreviewDirectory/Assets/" $UnityCheckFilter.ManagedWide $UnityCheckFilter.BepInEx
if (-not $ProductionDll) { $ProductionDll = "$repo/bin/Release/netstandard2.1/BoscaliSummer.dll" }
Copy-Item -LiteralPath $ProductionDll -Destination "$PreviewDirectory/Assets/BoscaliSummer.dll"
Copy-Item -LiteralPath "$PSScriptRoot/DebugSweepUnityCheck.cs", $UnityCheckHarness -Destination "$PreviewDirectory/Assets/Harness/"
Set-Content -LiteralPath "$PreviewDirectory/Assets/Harness/BoscaliDebugSweep.asmdef" -Value '{"name":"BoscaliDebugSweep","references":["Unity.TextMeshPro","Unity.ugui"],"includePlatforms":["Editor"]}'
Set-Content -LiteralPath "$PreviewDirectory/production-dll.sha256" -Value (Get-FileHash -LiteralPath $ProductionDll).Hash
Remove-Item -LiteralPath "$PreviewDirectory/result.txt" -Force -ErrorAction SilentlyContinue
$process = Invoke-UnityCheck $Unity $PreviewDirectory 'DebugSweepUnityCheck.Run' -TimeoutSeconds 240
Write-Output "Results: $PreviewDirectory"
Show-UnityCheckResult $PreviewDirectory -LogTail 40
Assert-UnityResult $PreviewDirectory $process -ExitMessage "Unity debug sweep failed: $($process.ExitCode)"
