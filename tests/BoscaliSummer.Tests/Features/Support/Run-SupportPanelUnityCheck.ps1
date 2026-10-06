param(
    [string]$Unity = 'C:/Program Files/Unity/Hub/Editor/2022.3.62f3/Editor/Unity.exe',
    [string]$PreviewDirectory = (Join-Path $env:TEMP ('BoscaliSupportPanels-' + [guid]::NewGuid().ToString('N'))),
    [int]$TimeoutSeconds = 240,
    [string]$PluginDll = ''
)
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/../../../UnityCheck.Common.ps1"
if (-not $PluginDll) { $PluginDll = Join-Path $repo 'bin/Release/netstandard2.1/BoscaliSummer.dll' }
$PluginDll = (Resolve-Path -LiteralPath $PluginDll).Path
New-UnityCheckProject $PreviewDirectory '{"dependencies":{"com.unity.ugui":"1.0.0","com.unity.textmeshpro":"3.0.6","com.unity.modules.audio":"1.0.0","com.unity.modules.imageconversion":"1.0.0","com.unity.modules.physics":"1.0.0","com.unity.modules.particlesystem":"1.0.0","com.unity.modules.uielements":"1.0.0","com.unity.modules.animation":"1.0.0","com.unity.modules.assetbundle":"1.0.0","com.unity.modules.imgui":"1.0.0"}}' -Folders 'Assets/Harness', 'NOAvionics'
Copy-Item -LiteralPath $PluginDll -Destination "$PreviewDirectory/Assets/BoscaliSummer.dll"
Copy-Item -LiteralPath "$PSScriptRoot/SupportPanelUnityCheck.cs", $UnityCheckHarness, "$PSScriptRoot/SupportPanelUnityCheck.Gate.cs", "$PSScriptRoot/BoscaliSupportPreview.asmdef" -Destination "$PreviewDirectory/Assets/Harness/"
Copy-Item -LiteralPath "$repo/AvionicsUi/Assets/avionics-ui.bundle" -Destination "$PreviewDirectory/"
Copy-GameDlls "$PreviewDirectory/Assets/" $UnityCheckFilter.Managed $UnityCheckFilter.BepInEx
Write-Output "Plugin DLL: $PluginDll (SHA256 $((Get-FileHash -LiteralPath $PluginDll -Algorithm SHA256).Hash))"
$process = Invoke-UnityCheck $Unity $PreviewDirectory 'SupportPanelUnityCheck.Run' -TimeoutSeconds $TimeoutSeconds `
    -TimeoutMessage "Unity CALLS panel check timed out after $TimeoutSeconds seconds: $PreviewDirectory/check.log"
Write-Output "Results and renders: $PreviewDirectory"
Show-UnityCheckResult $PreviewDirectory -LogTail 90
if ($process.ExitCode -ne 0) { throw "Unity Support panel check failed: $($process.ExitCode)" }
