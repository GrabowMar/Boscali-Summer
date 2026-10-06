param(
    [string]$Unity = "C:/Program Files/Unity/Hub/Editor/2022.3.62f3/Editor/Unity.exe",
    [string]$PreviewDirectory = (Join-Path $env:TEMP ("BoscaliHudRenderCheck-" + [guid]::NewGuid().ToString("N")))
)
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/../../../UnityCheck.Common.ps1"
New-UnityCheckProject $PreviewDirectory '{"dependencies":{"com.unity.ugui":"1.0.0","com.unity.textmeshpro":"3.0.6","com.unity.modules.physics":"1.0.0"}}'
Copy-Item -LiteralPath "$PSScriptRoot/HudRenderGameStubs.cs", "$PSScriptRoot/HudRenderUnityCheck.cs", "$repo/modules/Hud/Runtime/ThirdPersonHudCenter.cs", "$repo/modules/Hud/Runtime/ThirdPersonWeaponProjection.cs", "$repo/modules/Hud/Runtime/ThirdPersonAirbaseProjection.cs", "$repo/modules/Hud/Runtime/ExternalHudEnabler.cs" -Destination "$PreviewDirectory/Assets/"
Copy-GameDlls "$PreviewDirectory/Assets/" -BepInExMatch '^(Mono|0Harmony.dll$|HarmonyXInterop)'
$process = Invoke-UnityCheck $Unity $PreviewDirectory 'HudRenderUnityCheck.Run' -Flags '-batchmode'
Write-Output "Results: $PreviewDirectory"
Show-UnityCheckResult $PreviewDirectory
$failed = "HUD render check failed: $PreviewDirectory"
Assert-UnityResult $PreviewDirectory $process -Pattern '(?m)^PASS:' -ExitMessage $failed -FailMessage $failed
