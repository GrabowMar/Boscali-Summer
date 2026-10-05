param(
    [string]$AtlasStyles,
    [string]$Unity = 'C:/Program Files/Unity/Hub/Editor/2022.3.62f3/Editor/Unity.exe',
    [string]$PreviewDirectory = (Join-Path $env:TEMP ('BoscaliMapOverlayCheck-' + [guid]::NewGuid().ToString('N'))),
    [string]$ModAssembly,
    [Alias('ExcludeWing')][switch]$NonWingOnly,
    [switch]$PrepareOnly
)
$ErrorActionPreference = 'Stop'
function Get-MapPreviewHash([string]$Path) {
    $sha = [System.Security.Cryptography.SHA256]::Create()
    $stream = [System.IO.File]::OpenRead($Path)
    try { return [BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-', '') }
    finally { $stream.Dispose(); $sha.Dispose() }
}
$repo = (Resolve-Path (Join-Path $PSScriptRoot '../../../..')).Path
if (-not $ModAssembly) { $ModAssembly = Join-Path $repo 'bin/Release/netstandard2.1/BoscaliSummer.dll' }
$gameManaged = 'C:/Program Files (x86)/Steam/steamapps/common/Nuclear Option/NuclearOption_Data/Managed'
New-Item -ItemType Directory -Force -Path "$PreviewDirectory/Assets/Harness", "$PreviewDirectory/ProjectSettings", "$PreviewDirectory/Packages", "$PreviewDirectory/NOAvionics" | Out-Null
Set-Content -LiteralPath "$PreviewDirectory/ProjectSettings/ProjectVersion.txt" -Value 'm_EditorVersion: 2022.3.62f3'
Set-Content -LiteralPath "$PreviewDirectory/Packages/manifest.json" -Value '{"dependencies":{"com.unity.ugui":"1.0.0","com.unity.textmeshpro":"3.0.6","com.unity.modules.audio":"1.0.0","com.unity.modules.imageconversion":"1.0.0","com.unity.modules.uielements":"1.0.0","com.unity.modules.physics":"1.0.0","com.unity.modules.animation":"1.0.0","com.unity.modules.ai":"1.0.0","com.unity.modules.particlesystem":"1.0.0","com.unity.modules.assetbundle":"1.0.0"}}'
Get-ChildItem -LiteralPath $gameManaged -Filter '*.dll' | Where-Object {
    $_.Name -notmatch '^(System|Mono|Microsoft|UnityEngine|UnityEditor|mscorlib|netstandard|Unity.TextMeshPro|Accessibility|Novell)'
} | Copy-Item -Destination "$PreviewDirectory/Assets/"
Get-ChildItem -LiteralPath 'C:/Program Files (x86)/Steam/steamapps/common/Nuclear Option/BepInEx/core' -Filter '*.dll' | Where-Object {
    $_.Name -match '^(BepInEx.dll$|Mono|0Harmony.dll$|HarmonyXInterop)'
} | Copy-Item -Destination "$PreviewDirectory/Assets/"
Copy-Item -LiteralPath $ModAssembly -Destination "$PreviewDirectory/Assets/BoscaliSummer.dll"
Copy-Item -LiteralPath "$PSScriptRoot/MapOverlayUnityCheck.cs", "$PSScriptRoot/MapOverlayUnityCheck.asmdef" -Destination "$PreviewDirectory/Assets/Harness/"
Get-ChildItem -LiteralPath "$repo/AvionicsUi" -Filter 'avionics.*.avss' | Copy-Item -Destination "$PreviewDirectory/NOAvionics/"
$widgets = @('TrenchMapOverlay', 'TrenchMapGraphic', 'ComMapOverlay', 'FrontlineGraphic', 'ThreatMapOverlay', 'CommsMapLayer', 'CommsMapTag', 'CommsInkGraphic', 'CommsPulseGraphic', 'AircraftTrailGraphic')
if (-not $NonWingOnly) { $widgets += @('WingMarkerBadge', 'WingMapRingGraphic', 'WmcMapOverlay') }
$executeMethod = if ($NonWingOnly) { 'MapOverlayUnityCheck.RunNonWing' } else { 'MapOverlayUnityCheck.Run' }
[ordered]@{
    assembly_sha256 = Get-MapPreviewHash "$PreviewDirectory/Assets/BoscaliSummer.dll"
    fixture_sha256 = Get-MapPreviewHash "$PreviewDirectory/Assets/Harness/MapOverlayUnityCheck.cs"
    production_widgets = $widgets
    non_wing_only = [bool]$NonWingOnly
    execute_method = $executeMethod
    limits = 'Synthetic native DynamicMap/FactionHQ lifecycle shells and mirrored display data. No world observation, native map input, flight, replication, scene reload or oblique relief acceptance.'
} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath "$PreviewDirectory/inputs.json"
if ($PrepareOnly) { Write-Output "Prepared: $PreviewDirectory"; return }
if ($AtlasStyles) { Get-ChildItem -LiteralPath (Join-Path $AtlasStyles 'NOAvionics') -Filter '*.avss' | Copy-Item -Destination "$PreviewDirectory/NOAvionics" -Force }
if (Test-Path -LiteralPath "$PreviewDirectory/result.txt") { Remove-Item -LiteralPath "$PreviewDirectory/result.txt" }
$arguments = @('-batchmode', '-disable-assembly-updater', '-projectPath', ('"' + $PreviewDirectory + '"'), '-executeMethod', $executeMethod, '-logFile', ('"' + "$PreviewDirectory/check.log" + '"'))
$process = Start-Process -FilePath $Unity -ArgumentList $arguments -WorkingDirectory $PreviewDirectory -WindowStyle Hidden -PassThru
$process.WaitForExit()
Write-Output "Results and renders: $PreviewDirectory"
if (Test-Path -LiteralPath "$PreviewDirectory/result.txt") { Get-Content -LiteralPath "$PreviewDirectory/result.txt" }
else { Get-Content -LiteralPath "$PreviewDirectory/check.log" -Tail 80 }
if ($process.ExitCode -ne 0) { throw "Unity map overlay check failed: $($process.ExitCode)" }
if (-not (Test-Path -LiteralPath "$PreviewDirectory/result.txt") -or (Get-Content -LiteralPath "$PreviewDirectory/result.txt" -Raw) -notmatch '\APASS:') { throw "Unity exited without a successful result: $PreviewDirectory" }
