param([string]$Unity, [string]$Out, [string]$AtlasStyles, [string]$ModDll, [string]$PreviewDirectory,
    [switch]$ShadowOwnershipOnly, [switch]$DisplayOwnershipOnly)
if ($ShadowOwnershipOnly -and $DisplayOwnershipOnly) { throw 'Choose one focused ownership fixture.' }
if (-not $Unity) { $Unity = 'C:/Program Files/Unity/Hub/Editor/2022.3.62f3/Editor/Unity.exe' }
if ($DisplayOwnershipOnly) {
    & "$PSScriptRoot/Run-ImmersionMaterialCheck.ps1" -Unity $Unity -ModDll $ModDll -EvidenceDirectory $PreviewDirectory
    return
}
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '../../../..')).Path
$game = 'C:/Program Files (x86)/Steam/steamapps/common/Nuclear Option'
$proj = if ($PreviewDirectory) { [IO.Path]::GetFullPath($PreviewDirectory) } else { Join-Path $env:TEMP ('BoscaliWeatherEnv-' + [guid]::NewGuid().ToString('N')) }
New-Item -ItemType Directory -Force -Path "$proj/Assets/Harness", "$proj/ProjectSettings", "$proj/Packages", "$proj/NOAvionics" | Out-Null
Set-Content -LiteralPath "$proj/ProjectSettings/ProjectVersion.txt" -Value 'm_EditorVersion: 2022.3.62f3'
Set-Content -LiteralPath "$proj/Packages/manifest.json" -Value '{"dependencies":{"com.unity.ugui":"1.0.0","com.unity.textmeshpro":"3.0.6","com.unity.modules.assetbundle":"1.0.0","com.unity.modules.imgui":"1.0.0","com.unity.modules.uielements":"1.0.0","com.unity.modules.imageconversion":"1.0.0","com.unity.modules.audio":"1.0.0","com.unity.modules.physics":"1.0.0","com.unity.modules.particlesystem":"1.0.0","com.unity.modules.animation":"1.0.0"}}'
if (-not $ModDll) { $ModDll = "$repo/bin/Release/netstandard2.1/BoscaliSummer.dll" }
Copy-Item -LiteralPath $ModDll -Destination "$proj/Assets/BoscaliSummer.dll"
$taskSha = [System.Security.Cryptography.SHA256]::Create()
$taskStream = [System.IO.File]::OpenRead($ModDll)
try { $taskDllHash = [BitConverter]::ToString($taskSha.ComputeHash($taskStream)).Replace('-', '') }
finally { $taskStream.Dispose(); $taskSha.Dispose() }
Set-Content -LiteralPath "$proj/dll-sha256.txt" -Value $taskDllHash
Copy-Item "$PSScriptRoot/WeatherEnvUnityCheck.cs", "$PSScriptRoot/BoscaliWeatherEnv.asmdef" "$proj/Assets/Harness/"
Copy-Item "$PSScriptRoot/WeatherShadowOwnershipCheck.cs" "$proj/Assets/Harness/"
Copy-Item "$repo/AvionicsUi/Assets/avionics-ui.bundle" "$proj/"
Get-ChildItem "$game/NuclearOption_Data/Managed" -Filter '*.dll' | Where-Object { $_.Name -notmatch '^(System|Mono\.|UnityEngine|UnityEditor|mscorlib|netstandard|Unity\.TextMeshPro|Unity\.Timeline|Unity\.VisualScripting)' } | Copy-Item -Destination "$proj/Assets/"
Get-ChildItem "$game/BepInEx/core" -Filter '*.dll' | Where-Object { $_.Name -match '^(BepInEx.dll$|Mono|0Harmony.dll$|HarmonyXInterop)' } | Copy-Item -Destination "$proj/Assets/"
if ($AtlasStyles) { Get-ChildItem -LiteralPath (Join-Path $AtlasStyles 'NOAvionics') -Filter '*.avss' | Copy-Item -Destination "$proj/NOAvionics" -Force }
$resultPath = if ($ShadowOwnershipOnly) { "$proj/shadow-result.txt" } else { "$proj/result.txt" }
if (Test-Path -LiteralPath $resultPath) { Remove-Item -LiteralPath $resultPath }
$entry = if ($ShadowOwnershipOnly) { 'WeatherShadowOwnershipCheck.Run' } else { 'WeatherEnvUnityCheck.Run' }
$editorArgs = @('-batchmode', '-disable-assembly-updater', '-projectPath', "`"$proj`"", '-executeMethod', $entry, '-logFile', "`"$proj/check.log`"")
if ($ShadowOwnershipOnly) { $editorArgs += '-nographics' }
$p = Start-Process -FilePath $Unity -ArgumentList $editorArgs -WorkingDirectory $proj -WindowStyle Hidden -PassThru
$p.WaitForExit()
if (Test-Path -LiteralPath $resultPath) { Get-Content -LiteralPath $resultPath } else { Get-Content "$proj/check.log" -Tail 80 }
Write-Output "Renders: $proj/env"
if ($Out -and !$ShadowOwnershipOnly -and (Test-Path "$proj/env")) {
    New-Item -ItemType Directory -Force -Path $Out | Out-Null
    Copy-Item "$proj/env/*.png", "$proj/result.txt", "$proj/dll-sha256.txt" -Destination $Out
    Write-Output "Saved renders: $Out"
}
if ($p.ExitCode -ne 0) { throw "Weather ENV console check failed: $($p.ExitCode) ($proj)" }
$expectedResult = if ($ShadowOwnershipOnly) { '^PASS: ' } else { '^PASS: ENV ' }
if (!(Test-Path -LiteralPath $resultPath) -or
    !(Select-String -LiteralPath $resultPath -Pattern $expectedResult -Quiet)) {
    throw "Weather ENV console check did not report a pass: $proj"
}
