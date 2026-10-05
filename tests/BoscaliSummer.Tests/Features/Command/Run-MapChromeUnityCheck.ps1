param(
    [string]$Unity = 'C:/Program Files/Unity/Hub/Editor/2022.3.62f3/Editor/Unity.exe',
    [string]$PreviewDirectory = (Join-Path $env:TEMP ('BoscaliMapChromeCheck-' + [guid]::NewGuid().ToString('N'))),
    [string]$ModDll,
    [string]$AtlasStyles,
    [string]$Out,
    [switch]$PrepareOnly
)
$ErrorActionPreference = 'Stop'
function Get-MapChromeHash([string]$Path) {
    $sha = [System.Security.Cryptography.SHA256]::Create()
    $stream = [System.IO.File]::OpenRead($Path)
    try { return [BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-', '') }
    finally { $stream.Dispose(); $sha.Dispose() }
}
$repo = (Resolve-Path (Join-Path $PSScriptRoot '../../../..')).Path
$PreviewDirectory = [IO.Path]::GetFullPath($PreviewDirectory)
if (-not $ModDll) { $ModDll = Join-Path $repo 'bin/Release/netstandard2.1/BoscaliSummer.dll' }
$game = 'C:/Program Files (x86)/Steam/steamapps/common/Nuclear Option'
New-Item -ItemType Directory -Force -Path "$PreviewDirectory/Assets/Harness/Native", "$PreviewDirectory/ProjectSettings", "$PreviewDirectory/Packages", "$PreviewDirectory/NOAvionics" | Out-Null
Set-Content -LiteralPath "$PreviewDirectory/ProjectSettings/ProjectVersion.txt" -Value 'm_EditorVersion: 2022.3.62f3'
Set-Content -LiteralPath "$PreviewDirectory/Packages/manifest.json" -Value '{"dependencies":{"com.unity.ugui":"1.0.0","com.unity.textmeshpro":"3.0.6","com.unity.modules.audio":"1.0.0","com.unity.modules.imageconversion":"1.0.0","com.unity.modules.uielements":"1.0.0","com.unity.modules.physics":"1.0.0","com.unity.modules.animation":"1.0.0","com.unity.modules.ai":"1.0.0","com.unity.modules.particlesystem":"1.0.0","com.unity.modules.assetbundle":"1.0.0","com.unity.modules.imgui":"1.0.0"}}'
Get-ChildItem -LiteralPath "$game/NuclearOption_Data/Managed" -Filter '*.dll' | Where-Object {
    $_.Name -notmatch '^(System|Mono|Microsoft|UnityEngine|UnityEditor|mscorlib|netstandard|Unity.TextMeshPro|Accessibility|Novell)'
} | Copy-Item -Destination "$PreviewDirectory/Assets/"
Get-ChildItem -LiteralPath "$game/BepInEx/core" -Filter '*.dll' | Where-Object {
    $_.Name -match '^(BepInEx.dll$|Mono|0Harmony.dll$|HarmonyXInterop)'
} | Copy-Item -Destination "$PreviewDirectory/Assets/"
Copy-Item -LiteralPath $ModDll -Destination "$PreviewDirectory/Assets/BoscaliSummer.dll"
Copy-Item -LiteralPath "$PSScriptRoot/MapChromeUnityCheck.cs", "$PSScriptRoot/MapChromeUnityCheck.asmdef" -Destination "$PreviewDirectory/Assets/Harness/"
Copy-Item -LiteralPath "$PSScriptRoot/MapChromeNativeShell.cs", "$PSScriptRoot/MapChromeNative.asmdef" -Destination "$PreviewDirectory/Assets/Harness/Native/"
Get-ChildItem -LiteralPath "$repo/AvionicsUi" -Filter 'avionics.*.avss' | Copy-Item -Destination "$PreviewDirectory/NOAvionics/"
$fixtureHash = Get-MapChromeHash "$PreviewDirectory/Assets/Harness/MapChromeUnityCheck.cs"
[ordered]@{
    assembly_sha256 = Get-MapChromeHash "$PreviewDirectory/Assets/BoscaliSummer.dll"
    fixture_sha256 = $fixtureHash
    production_widgets = @('MfdMapDeck', 'MfdMapFooter', 'MfdMapFooter.RectSnapshot')
    limits = 'Real private placement/adopt/snapshot/tick/restore methods; synthetic native surfaces, no public Ensure native discovery adapter. Owned deck overlay converted to world-space only for offscreen screenshot after sorting/scaler assertions. Deferred Destroy unavailable in editor; fixture uses DestroyImmediate only after restoration gates. No native gameplay action, input, mission or scene reload acceptance.'
} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath "$PreviewDirectory/inputs.json"
if ($PrepareOnly) { Write-Output "Prepared: $PreviewDirectory"; return }
if ($AtlasStyles) { Get-ChildItem -LiteralPath (Join-Path $AtlasStyles 'NOAvionics') -Filter '*.avss' | Copy-Item -Destination "$PreviewDirectory/NOAvionics" -Force }
if (Test-Path -LiteralPath "$PreviewDirectory/result.txt") { Remove-Item -LiteralPath "$PreviewDirectory/result.txt" }
$arguments = @('-batchmode', '--burst-disable-compilation', '-disable-assembly-updater', '-projectPath', ('"' + $PreviewDirectory + '"'), '-executeMethod', 'MapChromeUnityCheck.Run', '-logFile', ('"' + "$PreviewDirectory/check.log" + '"'))
$process = Start-Process -FilePath $Unity -ArgumentList $arguments -WorkingDirectory $PreviewDirectory -WindowStyle Hidden -PassThru
$process.WaitForExit()
Write-Output "Results and renders: $PreviewDirectory"
if (Test-Path -LiteralPath "$PreviewDirectory/result.txt") { Get-Content -LiteralPath "$PreviewDirectory/result.txt" }
else { Get-Content -LiteralPath "$PreviewDirectory/check.log" -Tail 80 }
if ($Out -and (Test-Path -LiteralPath "$PreviewDirectory/renders")) {
    New-Item -ItemType Directory -Force -Path $Out | Out-Null
    Copy-Item -Path "$PreviewDirectory/renders/*.png" -Destination $Out -Force
    foreach ($name in @('result.txt', 'failures.txt', 'measurements.tsv', 'captures.tsv', 'inputs.json')) {
        if (Test-Path -LiteralPath "$PreviewDirectory/$name") { Copy-Item -LiteralPath "$PreviewDirectory/$name" -Destination $Out -Force }
    }
}
if ((Get-MapChromeHash "$PSScriptRoot/MapChromeUnityCheck.cs") -ne $fixtureHash) { throw 'MapChrome fixture changed during render; rerun settled source.' }
if ($process.ExitCode -ne 0) { throw "Unity map chrome check failed: $($process.ExitCode)" }
if (-not (Test-Path -LiteralPath "$PreviewDirectory/result.txt") -or (Get-Content -LiteralPath "$PreviewDirectory/result.txt" -Raw) -notmatch '\APASS: MAP CHROME ') { throw "Unity exited without a successful MapChrome result: $PreviewDirectory" }
