param(
    [string]$Unity = 'C:/Program Files/Unity/Hub/Editor/2022.3.62f3/Editor/Unity.exe',
    [string]$PreviewDirectory = (Join-Path $env:TEMP ('BoscaliFrontWindow-' + [guid]::NewGuid().ToString('N'))),
    [int]$TimeoutSeconds = 300,
    [string]$PluginDll = ''
)
# Renders the OPS front window (ORBIT room, COLD and LIVE) at 1920x1080 into <PreviewDirectory>/renders and gates its text.
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/../../../UnityCheck.Common.ps1"
if (-not $PluginDll) { $PluginDll = Join-Path $repo 'bin/Release/netstandard2.1/BoscaliSummer.dll' }
$PluginDll = (Resolve-Path -LiteralPath $PluginDll).Path
New-UnityCheckProject $PreviewDirectory $UnityCheckManifest.KitFull -Folders 'Assets/Harness', 'NOAvionics', 'renders'
Copy-Item -LiteralPath $PluginDll -Destination "$PreviewDirectory/Assets/BoscaliSummer.dll"
Copy-Item -LiteralPath "$PSScriptRoot/FrontWindowUnityCheck.cs", $UnityCheckHarness, "$PSScriptRoot/BoscaliSupportPreview.asmdef" -Destination "$PreviewDirectory/Assets/Harness/"
Copy-Item -LiteralPath "$repo/AvionicsUi/Assets/avionics-ui.bundle" -Destination "$PreviewDirectory/"
Copy-Item -LiteralPath "$repo/modules/Command/Assets/terrain2_intel.png" -Destination "$PreviewDirectory/map-fixture.png"
Copy-GameDlls "$PreviewDirectory/Assets/" $UnityCheckFilter.Managed $UnityCheckFilter.BepInEx
Copy-AvionicsStyles $PreviewDirectory ''
Write-Output "Plugin DLL: $PluginDll (SHA256 $((Get-FileHash -LiteralPath $PluginDll -Algorithm SHA256).Hash))"
$process = Invoke-UnityCheck $Unity $PreviewDirectory 'FrontWindowUnityCheck.Run' -TimeoutSeconds $TimeoutSeconds `
    -TimeoutMessage "Unity front window check timed out after $TimeoutSeconds seconds: $PreviewDirectory/check.log"
Write-Output "Results and renders: $PreviewDirectory/renders"
Show-UnityCheckResult $PreviewDirectory -LogTail 90
if ($process.ExitCode -ne 0) { throw "Unity front window check failed: $($process.ExitCode)" }
