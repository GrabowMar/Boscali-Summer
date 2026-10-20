param(
    [string]$Unity = 'C:/Program Files/Unity/Hub/Editor/2022.3.62f3/Editor/Unity.exe',
    [string]$PreviewDirectory = (Join-Path $env:TEMP ('BoscaliDroneLink-' + [guid]::NewGuid().ToString('N'))),
    [int]$TimeoutSeconds = 600,
    [string]$PluginDll = ''
)
# Renders the VANGUARD drone link surfaces (UAV terminal, UAV MFD, HUD feed card) into <PreviewDirectory>/renders and gates their text.
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/../../../UnityCheck.Common.ps1"
if (-not $PluginDll) { $PluginDll = Join-Path $repo 'bin/Release/netstandard2.1/BoscaliSummer.dll' }
$PluginDll = (Resolve-Path -LiteralPath $PluginDll).Path
New-UnityCheckProject $PreviewDirectory $UnityCheckManifest.KitFull -Folders 'Assets/Harness', 'NOAvionics', 'renders'
Copy-Item -LiteralPath $PluginDll -Destination "$PreviewDirectory/Assets/BoscaliSummer.dll"
Copy-Item -LiteralPath "$PSScriptRoot/DroneLinkUnityCheck.cs", $UnityCheckHarness, "$PSScriptRoot/../Support/BoscaliSupportPreview.asmdef" -Destination "$PreviewDirectory/Assets/Harness/"
Copy-Item -LiteralPath "$repo/AvionicsUi/Assets/avionics-ui.bundle" -Destination "$PreviewDirectory/"
Copy-Item -LiteralPath "$repo/modules/Command/Assets/terrain2_intel.png" -Destination "$PreviewDirectory/feed-fixture.png"
Copy-GameDlls "$PreviewDirectory/Assets/" $UnityCheckFilter.Managed $UnityCheckFilter.BepInEx
Copy-AvionicsStyles $PreviewDirectory ''
Write-Output "Plugin DLL: $PluginDll (SHA256 $((Get-FileHash -LiteralPath $PluginDll -Algorithm SHA256).Hash))"
$process = Invoke-UnityCheck $Unity $PreviewDirectory 'DroneLinkUnityCheck.Run' -TimeoutSeconds $TimeoutSeconds `
    -TimeoutMessage "Unity drone link check timed out after $TimeoutSeconds seconds: $PreviewDirectory/check.log"
Write-Output "Results and renders: $PreviewDirectory/renders"
Show-UnityCheckResult $PreviewDirectory -LogTail 60
if ($process.ExitCode -ne 0) { throw "Unity drone link check failed: $($process.ExitCode)" }
