param(
    [string]$Unity = 'C:/Program Files/Unity/Hub/Editor/2022.3.62f3/Editor/Unity.exe',
    [string]$PreviewDirectory = (Join-Path $env:TEMP ('BoscaliPresentationCheck-' + [guid]::NewGuid().ToString('N'))),
    [switch]$EventAlertOnly,
    [switch]$SqdOnly,
    [switch]$AceHuntOnly,
    [string]$AtlasStyles,
    [switch]$PrepareOnly
)
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/../../../UnityCheck.Common.ps1"
New-UnityCheckProject $PreviewDirectory '{"dependencies":{"com.unity.ugui":"1.0.0","com.unity.textmeshpro":"3.0.6","com.unity.modules.audio":"1.0.0","com.unity.modules.imageconversion":"1.0.0","com.unity.modules.uielements":"1.0.0","com.unity.modules.physics":"1.0.0","com.unity.modules.animation":"1.0.0","com.unity.modules.ai":"1.0.0","com.unity.modules.particlesystem":"1.0.0"}}' -Folders 'Assets/Harness', 'NOAvionics'
Copy-GameDlls "$PreviewDirectory/Assets/" $UnityCheckFilter.ManagedWide $UnityCheckFilter.BepInEx
Copy-Item -LiteralPath "$repo/bin/Release/netstandard2.1/BoscaliSummer.dll" -Destination "$PreviewDirectory/Assets/"
Copy-Item -LiteralPath "$PSScriptRoot/PresentationUnityCheck.cs", "$PSScriptRoot/BoscaliPresentationPreview.asmdef" -Destination "$PreviewDirectory/Assets/Harness/"
Copy-Item -LiteralPath "$repo/AvionicsUi/Assets/avionics-ui.bundle" -Destination "$PreviewDirectory/"
# SuperEventAlert deliberately refuses Application.isBatchMode. Keep the Editor window hidden,
# but run a normal Editor process so the production overlay builder and camera path are exercised.
$method = if ($AceHuntOnly) { 'PresentationUnityCheck.RunAceHuntOnly' } elseif ($SqdOnly) { 'PresentationUnityCheck.RunSqdOnly' } elseif ($EventAlertOnly) { 'PresentationUnityCheck.RunEventAlertOnly' } else { 'PresentationUnityCheck.Run' }
# The SQD-only check never builds the event alert, so it can and must run headless (a windowed Editor that
# fails to Exit would hang forever); the full run keeps the windowed Editor for SuperEventAlert.
$headless = @(if ($SqdOnly -or $AceHuntOnly) { '-batchmode' })
Copy-AvionicsStyles $PreviewDirectory $AtlasStyles -IfExists -NoDefaults
if ($PrepareOnly) { Write-Output "Prepared preview project: $PreviewDirectory"; return }
$process = Invoke-UnityCheck $Unity $PreviewDirectory $method -Flags ($headless + '-disable-assembly-updater')
Write-Output "Results and renders: $PreviewDirectory"
Show-UnityCheckResult $PreviewDirectory -LogTail 100
Assert-UnityResult $PreviewDirectory $process -ExitMessage "Unity presentation check failed: $($process.ExitCode)"
