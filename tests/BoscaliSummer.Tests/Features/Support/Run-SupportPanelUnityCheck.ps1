param(
    [string]$Unity = 'C:/Program Files/Unity/Hub/Editor/2022.3.62f3/Editor/Unity.exe',
    [string]$PreviewDirectory = (Join-Path $env:TEMP ('BoscaliSupportPanels-' + [guid]::NewGuid().ToString('N'))),
    [int]$TimeoutSeconds = 240,
    [string]$PluginDll = ''
)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '../../../..')).Path
$game = 'C:/Program Files (x86)/Steam/steamapps/common/Nuclear Option'
if (-not $PluginDll) { $PluginDll = Join-Path $repo 'bin/Release/netstandard2.1/BoscaliSummer.dll' }
$PluginDll = (Resolve-Path -LiteralPath $PluginDll).Path
New-Item -ItemType Directory -Force -Path "$PreviewDirectory/Assets/Harness", "$PreviewDirectory/ProjectSettings", "$PreviewDirectory/Packages", "$PreviewDirectory/NOAvionics" | Out-Null
Set-Content -LiteralPath "$PreviewDirectory/ProjectSettings/ProjectVersion.txt" -Value 'm_EditorVersion: 2022.3.62f3'
Set-Content -LiteralPath "$PreviewDirectory/Packages/manifest.json" -Value '{"dependencies":{"com.unity.ugui":"1.0.0","com.unity.textmeshpro":"3.0.6","com.unity.modules.audio":"1.0.0","com.unity.modules.imageconversion":"1.0.0","com.unity.modules.physics":"1.0.0","com.unity.modules.particlesystem":"1.0.0","com.unity.modules.uielements":"1.0.0","com.unity.modules.animation":"1.0.0","com.unity.modules.assetbundle":"1.0.0","com.unity.modules.imgui":"1.0.0"}}'
Copy-Item -LiteralPath $PluginDll -Destination "$PreviewDirectory/Assets/BoscaliSummer.dll"
Copy-Item -LiteralPath "$PSScriptRoot/SupportPanelUnityCheck.cs", "$PSScriptRoot/SupportPanelUnityCheck.Gate.cs", "$PSScriptRoot/BoscaliSupportPreview.asmdef" -Destination "$PreviewDirectory/Assets/Harness/"
Copy-Item -LiteralPath "$repo/AvionicsUi/Assets/avionics-ui.bundle" -Destination "$PreviewDirectory/"
Get-ChildItem -LiteralPath "$game/NuclearOption_Data/Managed" -Filter '*.dll' | Where-Object {
    $_.Name -notmatch '^(System|Mono\.|UnityEngine|UnityEditor|mscorlib|netstandard|Unity\.TextMeshPro|Unity\.Timeline|Unity\.VisualScripting)'
} | Copy-Item -Destination "$PreviewDirectory/Assets/"
Get-ChildItem -LiteralPath "$game/BepInEx/core" -Filter '*.dll' | Where-Object { $_.Name -match '^(BepInEx.dll$|Mono|0Harmony.dll$|HarmonyXInterop)' } | Copy-Item -Destination "$PreviewDirectory/Assets/"
if (Test-Path -LiteralPath "$PreviewDirectory/result.txt") { Remove-Item -LiteralPath "$PreviewDirectory/result.txt" }
Write-Output "Plugin DLL: $PluginDll (SHA256 $((Get-FileHash -LiteralPath $PluginDll -Algorithm SHA256).Hash))"
$process = Start-Process -FilePath $Unity -ArgumentList @('-batchmode', '-disable-assembly-updater', '-projectPath', ('"' + $PreviewDirectory + '"'), '-executeMethod', 'SupportPanelUnityCheck.Run', '-logFile', ('"' + "$PreviewDirectory/check.log" + '"')) -WorkingDirectory $PreviewDirectory -WindowStyle Hidden -PassThru
if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
    Stop-Process -Id $process.Id
    throw "Unity CALLS panel check timed out after $TimeoutSeconds seconds: $PreviewDirectory/check.log"
}
Write-Output "Results and renders: $PreviewDirectory"
if (Test-Path -LiteralPath "$PreviewDirectory/result.txt") { Get-Content -LiteralPath "$PreviewDirectory/result.txt" }
else { Get-Content -LiteralPath "$PreviewDirectory/check.log" -Tail 90 }
if ($process.ExitCode -ne 0) { throw "Unity Support panel check failed: $($process.ExitCode)" }
