param(
    [string]$AtlasStyles,
    [string]$Unity = "C:/Program Files/Unity/Hub/Editor/2022.3.62f3/Editor/Unity.exe",
    [string]$PreviewDirectory = (Join-Path $env:TEMP ("BoscaliMissionDeskCheck-" + [guid]::NewGuid().ToString("N")))
)
$ErrorActionPreference = "Stop"
$repo = (Resolve-Path (Join-Path $PSScriptRoot "../../../..")).Path
if (-not [IO.Path]::IsPathRooted($PreviewDirectory)) { $PreviewDirectory = Join-Path $repo $PreviewDirectory }
$PreviewDirectory = [IO.Path]::GetFullPath($PreviewDirectory)
$gameManaged = 'C:/Program Files (x86)/Steam/steamapps/common/Nuclear Option/NuclearOption_Data/Managed'
New-Item -ItemType Directory -Force -Path "$PreviewDirectory/Assets/Harness", "$PreviewDirectory/ProjectSettings", "$PreviewDirectory/Packages", "$PreviewDirectory/NOAvionics" | Out-Null
Set-Content -LiteralPath "$PreviewDirectory/ProjectSettings/ProjectVersion.txt" -Value 'm_EditorVersion: 2022.3.62f3'
Set-Content -LiteralPath "$PreviewDirectory/Packages/manifest.json" -Value '{"dependencies":{"com.unity.ugui":"1.0.0","com.unity.textmeshpro":"3.0.6","com.unity.modules.audio":"1.0.0","com.unity.modules.imageconversion":"1.0.0","com.unity.modules.uielements":"1.0.0","com.unity.modules.physics":"1.0.0","com.unity.modules.animation":"1.0.0","com.unity.modules.ai":"1.0.0","com.unity.modules.particlesystem":"1.0.0"}}'
Get-ChildItem -LiteralPath $gameManaged -Filter '*.dll' | Where-Object {
    $_.Name -notmatch '^(System|Mono|Microsoft|UnityEngine|UnityEditor|mscorlib|netstandard|Unity.TextMeshPro|Accessibility|Novell)'
} | Copy-Item -Destination "$PreviewDirectory/Assets/"
Get-ChildItem -LiteralPath 'C:/Program Files (x86)/Steam/steamapps/common/Nuclear Option/BepInEx/core' -Filter '*.dll' | Where-Object { $_.Name -match '^(BepInEx.dll$|Mono|0Harmony.dll$|HarmonyXInterop)' } | Copy-Item -Destination "$PreviewDirectory/Assets/"
Copy-Item -LiteralPath "$repo/bin/Release/netstandard2.1/BoscaliSummer.dll" -Destination "$PreviewDirectory/Assets/"
Copy-Item -LiteralPath "$PSScriptRoot/MissionDeskUnityCheck.cs", "$PSScriptRoot/BoscaliStockPreview.asmdef" -Destination "$PreviewDirectory/Assets/Harness/"
# Reuse the production assembly's existing preview friendship for a typed fake host-view fixture.
# The shared stock-preview asmdef stays unchanged; this isolated project owns its assembly name.
Set-Content -LiteralPath "$PreviewDirectory/Assets/Harness/BoscaliStockPreview.asmdef" -Value '{"name":"BoscaliSupportPreview","references":["Unity.TextMeshPro","Unity.ugui"],"includePlatforms":["Editor"]}'
Get-ChildItem -LiteralPath "$repo/AvionicsUi" -Filter 'avionics.*.avss' | Copy-Item -Destination "$PreviewDirectory/NOAvionics/"
$arguments = @('-batchmode', '-disable-assembly-updater', '-projectPath', ('"' + $PreviewDirectory + '"'), '-executeMethod', 'MissionDeskUnityCheck.Run', '-logFile', ('"' + "$PreviewDirectory/check.log" + '"'))
if (Test-Path -LiteralPath "$PreviewDirectory/result.txt") { Remove-Item -LiteralPath "$PreviewDirectory/result.txt" }
if ($AtlasStyles) { Get-ChildItem -LiteralPath (Join-Path $AtlasStyles 'NOAvionics') -Filter '*.avss' | Copy-Item -Destination (Join-Path $PreviewDirectory 'NOAvionics') -Force }
$process = Start-Process -FilePath $Unity -ArgumentList $arguments -WorkingDirectory $PreviewDirectory -WindowStyle Hidden -PassThru
$process.WaitForExit()
Write-Output "Results and renders: $PreviewDirectory"
if (Test-Path "$PreviewDirectory/result.txt") { Get-Content "$PreviewDirectory/result.txt" }
else { Get-Content "$PreviewDirectory/check.log" -Tail 80 }
if ($process.ExitCode -ne 0) { throw "Unity mission desk check failed: $($process.ExitCode)" }

if (-not (Test-Path -LiteralPath "$PreviewDirectory/result.txt") -or (Get-Content -LiteralPath "$PreviewDirectory/result.txt" -Raw) -notmatch '\APASS:') { throw "Unity exited without a successful result: $PreviewDirectory" }
