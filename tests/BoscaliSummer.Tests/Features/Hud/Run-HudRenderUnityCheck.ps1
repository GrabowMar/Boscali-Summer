param(
    [string]$Unity = "C:/Program Files/Unity/Hub/Editor/2022.3.62f3/Editor/Unity.exe",
    [string]$PreviewDirectory = (Join-Path $env:TEMP ("BoscaliHudRenderCheck-" + [guid]::NewGuid().ToString("N")))
)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '../../../..')).Path
New-Item -ItemType Directory -Force -Path "$PreviewDirectory/Assets", "$PreviewDirectory/ProjectSettings", "$PreviewDirectory/Packages" | Out-Null
Set-Content -LiteralPath "$PreviewDirectory/ProjectSettings/ProjectVersion.txt" -Value 'm_EditorVersion: 2022.3.62f3'
Set-Content -LiteralPath "$PreviewDirectory/Packages/manifest.json" -Value '{"dependencies":{"com.unity.ugui":"1.0.0","com.unity.textmeshpro":"3.0.6","com.unity.modules.physics":"1.0.0"}}'
Copy-Item -LiteralPath "$PSScriptRoot/HudRenderGameStubs.cs", "$PSScriptRoot/HudRenderUnityCheck.cs", "$repo/modules/Hud/Runtime/ThirdPersonHudCenter.cs", "$repo/modules/Hud/Runtime/ThirdPersonWeaponProjection.cs", "$repo/modules/Hud/Runtime/ThirdPersonAirbaseProjection.cs", "$repo/modules/Hud/Runtime/ExternalHudEnabler.cs" -Destination "$PreviewDirectory/Assets/"
Get-ChildItem -LiteralPath 'C:/Program Files (x86)/Steam/steamapps/common/Nuclear Option/BepInEx/core' -Filter '*.dll' | Where-Object { $_.Name -match '^(Mono|0Harmony.dll$|HarmonyXInterop)' } | Copy-Item -Destination "$PreviewDirectory/Assets/"
if (Test-Path -LiteralPath "$PreviewDirectory/result.txt") { Remove-Item -LiteralPath "$PreviewDirectory/result.txt" }
$arguments = @('-batchmode', '-projectPath', ('"' + $PreviewDirectory + '"'), '-executeMethod', 'HudRenderUnityCheck.Run', '-logFile', ('"' + "$PreviewDirectory/check.log" + '"'))
$process = Start-Process -FilePath $Unity -ArgumentList $arguments -WorkingDirectory $PreviewDirectory -WindowStyle Hidden -PassThru
$process.WaitForExit()
Write-Output "Results: $PreviewDirectory"
if (Test-Path -LiteralPath "$PreviewDirectory/result.txt") { Get-Content -LiteralPath "$PreviewDirectory/result.txt" }
else { Get-Content -LiteralPath "$PreviewDirectory/check.log" -Tail 60 }
if ($process.ExitCode -ne 0 -or !(Test-Path -LiteralPath "$PreviewDirectory/result.txt") -or !(Select-String -LiteralPath "$PreviewDirectory/result.txt" -Pattern '^PASS:' -Quiet)) { throw "HUD render check failed: $PreviewDirectory" }
