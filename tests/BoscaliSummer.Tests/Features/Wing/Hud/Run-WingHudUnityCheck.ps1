param(
    [string]$Unity = "C:/Program Files/Unity/Hub/Editor/2022.3.62f3/Editor/Unity.exe",
    [string]$PreviewDirectory = (Join-Path $env:TEMP ("BoscaliWingHudCheck-" + [guid]::NewGuid().ToString("N"))),
    [string]$ProductionDll,
    [string]$AtlasStyles
)
$ErrorActionPreference = "Stop"
function Get-PreviewHash([string]$Path) {
    $stream = [IO.File]::OpenRead($Path)
    $sha = [Security.Cryptography.SHA256]::Create()
    try { ([BitConverter]::ToString($sha.ComputeHash($stream))).Replace('-', '') }
    finally { $stream.Dispose(); $sha.Dispose() }
}
$repo = (Resolve-Path (Join-Path $PSScriptRoot "../../../../..")).Path
$gameManaged = 'C:/Program Files (x86)/Steam/steamapps/common/Nuclear Option/NuclearOption_Data/Managed'
New-Item -ItemType Directory -Force -Path "$PreviewDirectory/Assets/Harness/Native", "$PreviewDirectory/ProjectSettings", "$PreviewDirectory/Packages", "$PreviewDirectory/NOAvionics" | Out-Null
$keepFrozenStyles = $AtlasStyles -and [IO.Path]::GetFullPath($AtlasStyles) -eq [IO.Path]::GetFullPath($PreviewDirectory)
if (-not $keepFrozenStyles) {
    Get-ChildItem -LiteralPath "$repo/AvionicsUi" -Filter 'avionics.*.avss' | Copy-Item -Destination "$PreviewDirectory/NOAvionics/"
    if ($AtlasStyles) { Get-ChildItem -LiteralPath (Join-Path $AtlasStyles 'NOAvionics') -Filter '*.avss' | Copy-Item -Destination "$PreviewDirectory/NOAvionics/" -Force }
}
Set-Content -LiteralPath "$PreviewDirectory/ProjectSettings/ProjectVersion.txt" -Value 'm_EditorVersion: 2022.3.62f3'
Set-Content -LiteralPath "$PreviewDirectory/Packages/manifest.json" -Value '{"dependencies":{"com.unity.ugui":"1.0.0","com.unity.textmeshpro":"3.0.6","com.unity.modules.audio":"1.0.0","com.unity.modules.imageconversion":"1.0.0","com.unity.modules.uielements":"1.0.0","com.unity.modules.physics":"1.0.0","com.unity.modules.animation":"1.0.0","com.unity.modules.ai":"1.0.0","com.unity.modules.particlesystem":"1.0.0","com.unity.modules.assetbundle":"1.0.0","com.unity.modules.imgui":"1.0.0"}}'
Get-ChildItem -LiteralPath $gameManaged -Filter '*.dll' | Where-Object {
    $_.Name -notmatch '^(System|Mono|Microsoft|UnityEngine|UnityEditor|mscorlib|netstandard|Unity.TextMeshPro|Accessibility|Novell)'
} | Copy-Item -Destination "$PreviewDirectory/Assets/"
Get-ChildItem -LiteralPath 'C:/Program Files (x86)/Steam/steamapps/common/Nuclear Option/BepInEx/core' -Filter '*.dll' | Where-Object { $_.Name -match '^(BepInEx.dll$|Mono|0Harmony.dll$|HarmonyXInterop)' } | Copy-Item -Destination "$PreviewDirectory/Assets/"
if (-not $ProductionDll) { $ProductionDll = "$repo/bin/Release/netstandard2.1/BoscaliSummer.dll" }
if ((Resolve-Path -LiteralPath $ProductionDll).Path -ne [IO.Path]::GetFullPath("$PreviewDirectory/Assets/BoscaliSummer.dll")) {
    Copy-Item -LiteralPath $ProductionDll -Destination "$PreviewDirectory/Assets/BoscaliSummer.dll"
}
Copy-Item -LiteralPath "$PSScriptRoot/WingHudUnityCheck.cs", "$PSScriptRoot/BoscaliWingHudPreview.asmdef" -Destination "$PreviewDirectory/Assets/Harness/"
Copy-Item -LiteralPath "$PSScriptRoot/Native/WingHudNativeAdapter.cs", "$PSScriptRoot/Native/BoscaliWingHudNativeAdapter.asmdef" -Destination "$PreviewDirectory/Assets/Harness/Native/"
foreach ($obsoleteAdapter in @("$PreviewDirectory/Assets/Harness/WingHudNativeAdapter.cs", "$PreviewDirectory/Assets/Harness/WingHudNativeAdapter.cs.meta")) {
    if (Test-Path -LiteralPath $obsoleteAdapter) { Remove-Item -LiteralPath $obsoleteAdapter }
}
Set-Content -LiteralPath "$PreviewDirectory/production-dll.sha256" -Value (Get-PreviewHash "$PreviewDirectory/Assets/BoscaliSummer.dll")
@((Get-Item -LiteralPath "$PreviewDirectory/Assets/BoscaliSummer.dll"), (Get-ChildItem -LiteralPath "$PreviewDirectory/NOAvionics" -Filter '*.avss'), (Get-ChildItem -LiteralPath "$PreviewDirectory/Assets/Harness" -File -Recurse)) |
    ForEach-Object { $_ } | ForEach-Object { [pscustomobject]@{ Hash = Get-PreviewHash $_.FullName; Path = $_.FullName } } |
    Export-Csv -LiteralPath "$PreviewDirectory/input-manifest.csv" -NoTypeInformation
$arguments = @('-batchmode', '-disable-assembly-updater', '-projectPath', ('"' + $PreviewDirectory + '"'), '-executeMethod', 'WingHudUnityCheck.Run', '-logFile', ('"' + "$PreviewDirectory/check.log" + '"'))
if (Test-Path -LiteralPath "$PreviewDirectory/result.txt") { Remove-Item -LiteralPath "$PreviewDirectory/result.txt" }
$process = Start-Process -FilePath $Unity -ArgumentList $arguments -WorkingDirectory $PreviewDirectory -WindowStyle Hidden -PassThru
$process.WaitForExit()
Write-Output "Results and renders: $PreviewDirectory"
if (Test-Path "$PreviewDirectory/result.txt") { Get-Content "$PreviewDirectory/result.txt" }
else { Get-Content "$PreviewDirectory/check.log" -Tail 80 }
if ($process.ExitCode -ne 0) { throw "Unity Wing HUD check failed: $($process.ExitCode)" }
if (-not (Test-Path -LiteralPath "$PreviewDirectory/result.txt") -or (Get-Content -LiteralPath "$PreviewDirectory/result.txt" -Raw) -notmatch '\APASS:') { throw "Unity exited without a successful result: $PreviewDirectory" }
