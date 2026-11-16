param(
    [string]$Unity = 'C:/Program Files/Unity/Hub/Editor/2022.3.62f3/Editor/Unity.exe',
    [string]$PreviewDirectory = (Join-Path $env:TEMP ('BoscaliRenderTuning-' + [guid]::NewGuid().ToString('N'))),
    [string]$ProductionDll,
    [ValidateSet('RenderTuning', 'RenderSharpening')][string]$Check = 'RenderTuning'
)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path "$PSScriptRoot/../..").Path
$gameDirectory = 'C:/Program Files (x86)/Steam/steamapps/common/Nuclear Option'
$className = $Check + 'UnityCheck'
$sourcePath = Join-Path $PSScriptRoot ($className + '.cs')
if (-not (Test-Path -LiteralPath $sourcePath)) { throw "Missing check source: $sourcePath" }
New-Item -ItemType Directory -Force -Path "$PreviewDirectory/Assets/Harness", "$PreviewDirectory/Packages", "$PreviewDirectory/ProjectSettings" | Out-Null
Set-Content -LiteralPath "$PreviewDirectory/ProjectSettings/ProjectVersion.txt" -Value 'm_EditorVersion: 2022.3.62f3'
Set-Content -LiteralPath "$PreviewDirectory/Packages/manifest.json" -Value '{"dependencies":{"com.unity.ugui":"1.0.0","com.unity.textmeshpro":"3.0.6","com.unity.modules.audio":"1.0.0","com.unity.modules.imageconversion":"1.0.0","com.unity.modules.uielements":"1.0.0","com.unity.modules.physics":"1.0.0","com.unity.modules.animation":"1.0.0","com.unity.modules.ai":"1.0.0","com.unity.modules.particlesystem":"1.0.0","com.unity.modules.assetbundle":"1.0.0","com.unity.modules.imgui":"1.0.0"}}'
Get-ChildItem -LiteralPath "$gameDirectory/NuclearOption_Data/Managed" -Filter '*.dll' |
    Where-Object { $_.Name -notmatch '^(System|Mono|Microsoft|UnityEngine|UnityEditor|mscorlib|netstandard|Unity.TextMeshPro|Accessibility|Novell)' } |
    Copy-Item -Destination "$PreviewDirectory/Assets/"
Get-ChildItem -LiteralPath "$gameDirectory/BepInEx/core" -Filter '*.dll' |
    Where-Object { $_.Name -match '^(BepInEx.dll$|Mono|0Harmony.dll$|HarmonyXInterop)' } |
    Copy-Item -Destination "$PreviewDirectory/Assets/"
if (-not $ProductionDll) { $ProductionDll = "$repo/bin/Release/netstandard2.1/BoscaliSummer.dll" }
Copy-Item -LiteralPath $ProductionDll -Destination "$PreviewDirectory/Assets/BoscaliSummer.dll"
Copy-Item -LiteralPath $sourcePath -Destination "$PreviewDirectory/Assets/Harness/"
Set-Content -LiteralPath "$PreviewDirectory/Assets/Harness/BoscaliRenderTuningCheck.asmdef" -Value '{"name":"BoscaliRenderTuningCheck","includePlatforms":["Editor"]}'
Get-Item "$PreviewDirectory/Assets/BoscaliSummer.dll", "$PreviewDirectory/Assets/Harness/$className.cs", "$PreviewDirectory/Assets/Unity.RenderPipelines.Universal.Runtime.dll" |
    Get-FileHash | Select-Object Hash, Path | Export-Csv -LiteralPath "$PreviewDirectory/inputs.csv" -NoTypeInformation
# A hidden non-batch editor exercises the actual client gate; no game process or play session is started.
$arguments = @('--burst-disable-compilation', '-disable-assembly-updater', '-projectPath', ('"' + $PreviewDirectory + '"'),
    '-executeMethod', ($className + '.Run'), '-logFile', ('"' + "$PreviewDirectory/check.log" + '"'))
if (Test-Path -LiteralPath "$PreviewDirectory/result.txt") { Remove-Item -LiteralPath "$PreviewDirectory/result.txt" }
$process = Start-Process -FilePath $Unity -ArgumentList $arguments -WorkingDirectory $PreviewDirectory -WindowStyle Hidden -PassThru
if (-not $process.WaitForExit(240000)) { $process.Kill(); throw "Unity render tuning check timed out: $PreviewDirectory/check.log" }
Write-Output "Evidence: $PreviewDirectory"
if (Test-Path -LiteralPath "$PreviewDirectory/result.txt") { Get-Content -LiteralPath "$PreviewDirectory/result.txt" }
else { Get-Content -LiteralPath "$PreviewDirectory/check.log" -Tail 60 }
if ($process.ExitCode -ne 0 -or -not (Test-Path -LiteralPath "$PreviewDirectory/result.txt") -or
    (Get-Content -LiteralPath "$PreviewDirectory/result.txt" -Raw) -notmatch '\APASS:') {
    throw "Unity render tuning check failed ($($process.ExitCode)): $PreviewDirectory"
}
