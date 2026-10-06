param(
    [string]$AtlasStyles,
    [string]$Unity = "C:/Program Files/Unity/Hub/Editor/2022.3.62f3/Editor/Unity.exe",
    [string]$PreviewDirectory = (Join-Path $env:TEMP ("BoscaliMissionDeskCheck-" + [guid]::NewGuid().ToString("N")))
)
$ErrorActionPreference = "Stop"
. "$PSScriptRoot/../../../UnityCheck.Common.ps1"
if (-not [IO.Path]::IsPathRooted($PreviewDirectory)) { $PreviewDirectory = Join-Path $repo $PreviewDirectory }
$PreviewDirectory = [IO.Path]::GetFullPath($PreviewDirectory)
New-UnityCheckProject $PreviewDirectory '{"dependencies":{"com.unity.ugui":"1.0.0","com.unity.textmeshpro":"3.0.6","com.unity.modules.audio":"1.0.0","com.unity.modules.imageconversion":"1.0.0","com.unity.modules.uielements":"1.0.0","com.unity.modules.physics":"1.0.0","com.unity.modules.animation":"1.0.0","com.unity.modules.ai":"1.0.0","com.unity.modules.particlesystem":"1.0.0"}}' -Folders 'Assets/Harness', 'NOAvionics'
Copy-GameDlls "$PreviewDirectory/Assets/" $UnityCheckFilter.ManagedWide $UnityCheckFilter.BepInEx
Copy-Item -LiteralPath "$repo/bin/Release/netstandard2.1/BoscaliSummer.dll" -Destination "$PreviewDirectory/Assets/"
Copy-Item -LiteralPath "$PSScriptRoot/MissionDeskUnityCheck.cs", "$PSScriptRoot/BoscaliStockPreview.asmdef" -Destination "$PreviewDirectory/Assets/Harness/"
# Reuse the production assembly's existing preview friendship for a typed fake host-view fixture.
# The shared stock-preview asmdef stays unchanged; this isolated project owns its assembly name.
Set-Content -LiteralPath "$PreviewDirectory/Assets/Harness/BoscaliStockPreview.asmdef" -Value '{"name":"BoscaliSupportPreview","references":["Unity.TextMeshPro","Unity.ugui"],"includePlatforms":["Editor"]}'
Copy-AvionicsStyles $PreviewDirectory $AtlasStyles
$process = Invoke-UnityCheck $Unity $PreviewDirectory 'MissionDeskUnityCheck.Run'
Write-Output "Results and renders: $PreviewDirectory"
Show-UnityCheckResult $PreviewDirectory -LogTail 80
Assert-UnityResult $PreviewDirectory $process -ExitMessage "Unity mission desk check failed: $($process.ExitCode)"
