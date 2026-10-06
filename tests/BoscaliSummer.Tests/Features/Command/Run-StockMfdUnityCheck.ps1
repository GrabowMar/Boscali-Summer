param(
    [string]$AtlasStyles,
    [string]$Unity = "C:/Program Files/Unity/Hub/Editor/2022.3.62f3/Editor/Unity.exe",
    [string]$PreviewDirectory = (Join-Path $env:TEMP ("BoscaliStockCheck-" + [guid]::NewGuid().ToString("N")))
)
$ErrorActionPreference = "Stop"
. "$PSScriptRoot/../../../UnityCheck.Common.ps1"
New-UnityCheckProject $PreviewDirectory $UnityCheckManifest.MfdFull -Folders 'Assets/Harness', 'NOAvionics'
Copy-GameDlls "$PreviewDirectory/Assets/" $UnityCheckFilter.ManagedWide $UnityCheckFilter.BepInEx
Copy-Item -LiteralPath "$repo/bin/Release/netstandard2.1/BoscaliSummer.dll" -Destination "$PreviewDirectory/Assets/"
Copy-Item -LiteralPath "$PSScriptRoot/StockMfdUnityCheck.cs", $UnityCheckHarness, "$PSScriptRoot/BoscaliStockPreview.asmdef" -Destination "$PreviewDirectory/Assets/Harness/"
# The harness runs the built DLL, which embeds the kit v2 sources' sheets, fonts and bundle; only the v1 override sheet is copied.
Copy-AvionicsStyles $PreviewDirectory $AtlasStyles -NoDefaults
$process = Invoke-UnityCheck $Unity $PreviewDirectory 'StockMfdUnityCheck.Run'
Write-Output "Results and renders: $PreviewDirectory"
Show-UnityCheckResult $PreviewDirectory -LogTail 80
Assert-UnityResult $PreviewDirectory $process -ExitMessage "Unity stock MFD check failed: $($process.ExitCode)"
