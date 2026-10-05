param([string]$Unity, [string]$Out, [string]$AtlasStyles, [string]$ModDll, [string]$PreviewDirectory)
if (-not $Unity) { $Unity = 'C:/Program Files/Unity/Hub/Editor/2022.3.62f3/Editor/Unity.exe' }
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '../../../..')).Path
$game = 'C:/Program Files (x86)/Steam/steamapps/common/Nuclear Option'
$proj = if ($PreviewDirectory) { [IO.Path]::GetFullPath($PreviewDirectory) } else { Join-Path $env:TEMP ('BoscaliComEnvOverlays-' + [guid]::NewGuid().ToString('N')) }
New-Item -ItemType Directory -Force -Path "$proj/Assets/Harness", "$proj/ProjectSettings", "$proj/Packages", "$proj/NOAvionics" | Out-Null
Set-Content -LiteralPath "$proj/ProjectSettings/ProjectVersion.txt" -Value 'm_EditorVersion: 2022.3.62f3'
Set-Content -LiteralPath "$proj/Packages/manifest.json" -Value '{"dependencies":{"com.unity.ugui":"1.0.0","com.unity.textmeshpro":"3.0.6","com.unity.modules.assetbundle":"1.0.0","com.unity.modules.imgui":"1.0.0","com.unity.modules.uielements":"1.0.0","com.unity.modules.imageconversion":"1.0.0","com.unity.modules.audio":"1.0.0","com.unity.modules.physics":"1.0.0","com.unity.modules.particlesystem":"1.0.0","com.unity.modules.animation":"1.0.0"}}'
if (-not $ModDll) { $ModDll = "$repo/bin/Release/netstandard2.1/BoscaliSummer.dll" }
Copy-Item -LiteralPath $ModDll -Destination "$proj/Assets/BoscaliSummer.dll"
function Get-OverlayFileHash([string]$Path) {
    $taskSha = [System.Security.Cryptography.SHA256]::Create()
    $taskStream = [System.IO.File]::OpenRead($Path)
    try { [BitConverter]::ToString($taskSha.ComputeHash($taskStream)).Replace('-', '') }
    finally { $taskStream.Dispose(); $taskSha.Dispose() }
}
Set-Content -LiteralPath "$proj/dll-sha256.txt" -Value (Get-OverlayFileHash "$proj/Assets/BoscaliSummer.dll")
Copy-Item "$PSScriptRoot/ComEnvOverlayUnityCheck.cs", "$PSScriptRoot/BoscaliCommsPreview.asmdef" "$proj/Assets/Harness/"
$taskFixtureHash = Get-OverlayFileHash "$proj/Assets/Harness/ComEnvOverlayUnityCheck.cs"
Set-Content -LiteralPath "$proj/fixture-sha256.txt" -Value $taskFixtureHash
Copy-Item "$repo/AvionicsUi/Assets/avionics-ui.bundle" "$proj/"
Get-ChildItem "$game/NuclearOption_Data/Managed" -Filter '*.dll' | Where-Object { $_.Name -notmatch '^(System|Mono\.|UnityEngine|UnityEditor|mscorlib|netstandard|Unity\.TextMeshPro|Unity\.Timeline|Unity\.VisualScripting)' } | Copy-Item -Destination "$proj/Assets/"
Get-ChildItem "$game/BepInEx/core" -Filter '*.dll' | Where-Object { $_.Name -match '^(BepInEx.dll$|Mono|0Harmony.dll$|HarmonyXInterop)' } | Copy-Item -Destination "$proj/Assets/"
if ($AtlasStyles) { Get-ChildItem -LiteralPath (Join-Path $AtlasStyles 'NOAvionics') -Filter '*.avss' | Copy-Item -Destination "$proj/NOAvionics" -Force }
if (Test-Path -LiteralPath "$proj/geometry.txt") { Remove-Item -LiteralPath "$proj/geometry.txt" }
if (Test-Path -LiteralPath "$proj/result.txt") { Remove-Item -LiteralPath "$proj/result.txt" }
$p = Start-Process -FilePath $Unity -ArgumentList @('-batchmode', '-disable-assembly-updater', '-projectPath', "`"$proj`"", '-executeMethod', 'ComEnvOverlayUnityCheck.Run', '-logFile', "`"$proj/check.log`"") -WorkingDirectory $proj -WindowStyle Hidden -PassThru
$p.WaitForExit()
if (Test-Path "$proj/result.txt") { Get-Content "$proj/result.txt" } else { Get-Content "$proj/check.log" -Tail 80 }
Write-Output "Renders: $proj/renders"
if ($Out -and (Test-Path "$proj/renders")) {
    New-Item -ItemType Directory -Force -Path $Out | Out-Null
    Copy-Item "$proj/renders/*.png", "$proj/result.txt", "$proj/dll-sha256.txt", "$proj/fixture-sha256.txt" -Destination $Out
    Write-Output "Saved renders: $Out"
}
if (Test-Path -LiteralPath "$proj/geometry.txt") { Get-Content -LiteralPath "$proj/geometry.txt" }
if ((Get-OverlayFileHash "$PSScriptRoot/ComEnvOverlayUnityCheck.cs") -ne $taskFixtureHash) { throw 'Overlay fixture changed during render; rerun the settled source.' }
if ($p.ExitCode -ne 0) { throw "COM/ENV overlay check failed: $($p.ExitCode) ($proj)" }
if (!(Test-Path -LiteralPath "$proj/result.txt") -or
    !(Select-String -LiteralPath "$proj/result.txt" -Pattern '^PASS: COMENV OVERLAYS ' -Quiet)) {
    throw "COM/ENV overlay check did not report a pass: $proj"
}
