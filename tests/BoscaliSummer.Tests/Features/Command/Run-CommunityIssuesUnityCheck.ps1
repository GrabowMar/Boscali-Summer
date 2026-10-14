param(
    [string]$Unity = 'C:/Program Files/Unity/Hub/Editor/2022.3.62f3/Editor/Unity.exe',
    [string]$PreviewDirectory = (Join-Path $env:TEMP ('BoscaliCommunityCheck-' + [guid]::NewGuid().ToString('N')))
)
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/../../../UnityCheck.Common.ps1"
New-UnityCheckProject $PreviewDirectory $UnityCheckManifest.MfdFull -Folders 'Assets/Harness/Native', 'NOAvionics'
Copy-GameDlls "$PreviewDirectory/Assets/" $UnityCheckFilter.ManagedWide $UnityCheckFilter.BepInEx
Copy-Item -LiteralPath "$repo/bin/Release/netstandard2.1/BoscaliSummer.dll" -Destination "$PreviewDirectory/Assets/"
Copy-Item -LiteralPath "$PSScriptRoot/CommunityIssuesUnityCheck.cs", $UnityCheckHarness, "$PSScriptRoot/MapChromeUnityCheck.asmdef" -Destination "$PreviewDirectory/Assets/Harness/"
Copy-Item -LiteralPath "$PSScriptRoot/CommunityIssuesNativeShell.cs", "$PSScriptRoot/MapChromeNativeShell.cs", "$PSScriptRoot/MapChromeNative.asmdef" -Destination "$PreviewDirectory/Assets/Harness/Native/"
$process = Invoke-UnityCheck $Unity $PreviewDirectory 'CommunityIssuesUnityCheck.Run' -TimeoutSeconds 240
Show-UnityCheckResult $PreviewDirectory -LogTail 45
Assert-UnityResult $PreviewDirectory $process -Pattern '\APASS: COMMUNITY ISSUES ' -ExitMessage "Community regression check failed: $PreviewDirectory"
