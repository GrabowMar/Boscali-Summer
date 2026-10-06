param(
    [string]$AtlasStyles,
    [string]$Unity = 'C:/Program Files/Unity/Hub/Editor/2022.3.62f3/Editor/Unity.exe',
    [string]$PreviewDirectory = (Join-Path $env:TEMP ('BoscaliMapOverlayCheck-' + [guid]::NewGuid().ToString('N'))),
    [string]$ModAssembly,
    [Alias('ExcludeWing')][switch]$NonWingOnly,
    [switch]$PrepareOnly
)
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/../../../UnityCheck.Common.ps1"
if (-not $ModAssembly) { $ModAssembly = Join-Path $repo 'bin/Release/netstandard2.1/BoscaliSummer.dll' }
New-UnityCheckProject $PreviewDirectory '{"dependencies":{"com.unity.ugui":"1.0.0","com.unity.textmeshpro":"3.0.6","com.unity.modules.audio":"1.0.0","com.unity.modules.imageconversion":"1.0.0","com.unity.modules.uielements":"1.0.0","com.unity.modules.physics":"1.0.0","com.unity.modules.animation":"1.0.0","com.unity.modules.ai":"1.0.0","com.unity.modules.particlesystem":"1.0.0","com.unity.modules.assetbundle":"1.0.0"}}' -Folders 'Assets/Harness', 'NOAvionics'
Copy-GameDlls "$PreviewDirectory/Assets/" $UnityCheckFilter.ManagedWide $UnityCheckFilter.BepInEx
Copy-Item -LiteralPath $ModAssembly -Destination "$PreviewDirectory/Assets/BoscaliSummer.dll"
Copy-Item -LiteralPath "$PSScriptRoot/MapOverlayUnityCheck.cs", "$PSScriptRoot/MapOverlayUnityCheck.asmdef" -Destination "$PreviewDirectory/Assets/Harness/"
Copy-AvionicsStyles $PreviewDirectory
$widgets = @('TrenchMapOverlay', 'TrenchMapGraphic', 'ComMapOverlay', 'FrontlineGraphic', 'ThreatMapOverlay', 'CommsMapLayer', 'CommsMapTag', 'CommsInkGraphic', 'CommsPulseGraphic', 'AircraftTrailGraphic')
if (-not $NonWingOnly) { $widgets += @('WingMarkerBadge', 'WingMapRingGraphic', 'WmcMapOverlay') }
$executeMethod = if ($NonWingOnly) { 'MapOverlayUnityCheck.RunNonWing' } else { 'MapOverlayUnityCheck.Run' }
[ordered]@{
    assembly_sha256 = (Get-FileHash -LiteralPath "$PreviewDirectory/Assets/BoscaliSummer.dll").Hash
    fixture_sha256 = (Get-FileHash -LiteralPath "$PreviewDirectory/Assets/Harness/MapOverlayUnityCheck.cs").Hash
    production_widgets = $widgets
    non_wing_only = [bool]$NonWingOnly
    execute_method = $executeMethod
    limits = 'Synthetic native DynamicMap/FactionHQ lifecycle shells and mirrored display data. No world observation, native map input, flight, replication, scene reload or oblique relief acceptance.'
} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath "$PreviewDirectory/inputs.json"
if ($PrepareOnly) { Write-Output "Prepared: $PreviewDirectory"; return }
Copy-AvionicsStyles $PreviewDirectory $AtlasStyles -NoDefaults
$process = Invoke-UnityCheck $Unity $PreviewDirectory $executeMethod
Write-Output "Results and renders: $PreviewDirectory"
Show-UnityCheckResult $PreviewDirectory -LogTail 80
Assert-UnityResult $PreviewDirectory $process -ExitMessage "Unity map overlay check failed: $($process.ExitCode)" -FailMessage "Unity exited without a successful result: $PreviewDirectory"
