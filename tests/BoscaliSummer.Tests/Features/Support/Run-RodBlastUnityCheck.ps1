param(
    [string]$Unity = "C:/Program Files/Unity/Hub/Editor/2022.3.62f3/Editor/Unity.exe",
    [string]$PreviewDirectory = (Join-Path $env:TEMP ("BoscaliRodCheck-" + [guid]::NewGuid().ToString("N"))),
    [switch]$ReproducePreviousPrefix,
    [int]$TimeoutSeconds = 180
)
$ErrorActionPreference = "Stop"
. "$PSScriptRoot/../../../UnityCheck.Common.ps1"
New-UnityCheckProject $PreviewDirectory '{"dependencies":{"com.unity.modules.physics":"1.0.0"}}'
Copy-Item -LiteralPath "$PSScriptRoot/RodBlastUnityCheck.cs", "$repo/modules/Support/Runtime/RodBlast.cs", "$repo/modules/Support/Runtime/SupportEffectPolicy.cs", "$repo/modules/Support/Patches/SupportMissileVisualPatch.cs" -Destination "$PreviewDirectory/Assets/"
Copy-GameDlls "$PreviewDirectory/Assets/" -BepInExMatch '^(0Harmony\.dll|Mono\.Cecil\.dll|MonoMod\.Utils\.dll|MonoMod\.RuntimeDetour\.dll)$'
if ($ReproducePreviousPrefix) {
    $patchPath = "$PreviewDirectory/Assets/SupportMissileVisualPatch.cs"
    $source = Get-Content -LiteralPath $patchPath -Raw
    $start = $source.IndexOf('        private static void Prefix(Missile __instance, out bool __state)')
    $end = $source.IndexOf('    [HarmonyPatch(typeof(Missile), "OnStartClient")]', $start)
    if ($start -lt 0 -or $end -lt 0) { throw 'Cannot locate authority patch for regression reproduction.' }
    $old = @'
        private static void Prefix(Missile __instance)
        {
            if (__instance == null || __instance.disabled || !BoscaliSummer.Core.Game.GameAccess.IsServer()) return;
            if (__instance.UniqueName?.StartsWith("BoscaliSummer:Support:Rod:", StringComparison.Ordinal) == true)
                Runtime.RodBlast.Apply(__instance.transform.position, __instance.ownerID);
        }
    }

'@
    Set-Content -LiteralPath $patchPath -Value ($source.Substring(0, $start) + $old + $source.Substring($end))
}
$process = Invoke-UnityCheck $Unity $PreviewDirectory 'RodBlastUnityCheck.Run' -Flags '-batchmode', '-nographics' -TimeoutSeconds $TimeoutSeconds `
    -TimeoutMessage "Unity rod blast check timed out after $TimeoutSeconds seconds: $PreviewDirectory/check.log"
Write-Output "Results: $PreviewDirectory/result.txt"
Show-UnityCheckResult $PreviewDirectory -LogTail 90
if ($process.ExitCode -ne 0) { throw "Unity rod blast check failed: $($process.ExitCode)" }
