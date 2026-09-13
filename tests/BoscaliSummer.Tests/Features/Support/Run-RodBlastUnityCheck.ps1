param(
    [string]$Unity = "C:/Program Files/Unity/Hub/Editor/2022.3.62f3/Editor/Unity.exe",
    [string]$PreviewDirectory = (Join-Path $env:TEMP ("BoscaliRodCheck-" + [guid]::NewGuid().ToString("N"))),
    [switch]$ReproducePreviousPrefix
)
$ErrorActionPreference = "Stop"
$repo = (Resolve-Path (Join-Path $PSScriptRoot "../../../..")).Path
New-Item -ItemType Directory -Force -Path "$PreviewDirectory/Assets", "$PreviewDirectory/ProjectSettings", "$PreviewDirectory/Packages" | Out-Null
Set-Content -LiteralPath "$PreviewDirectory/ProjectSettings/ProjectVersion.txt" -Value "m_EditorVersion: 2022.3.62f3"
Set-Content -LiteralPath "$PreviewDirectory/Packages/manifest.json" -Value '{"dependencies":{"com.unity.modules.physics":"1.0.0"}}'
Copy-Item -LiteralPath "$PSScriptRoot/RodBlastUnityCheck.cs", "$repo/modules/Support/Runtime/RodBlast.cs", "$repo/modules/Support/Runtime/SupportEffectPolicy.cs", "$repo/modules/Support/Patches/SupportMissileVisualPatch.cs" -Destination "$PreviewDirectory/Assets/"
Copy-Item -LiteralPath "C:/Program Files (x86)/Steam/steamapps/common/Nuclear Option/BepInEx/core/0Harmony.dll" -Destination "$PreviewDirectory/Assets/"
foreach ($dependency in @('Mono.Cecil.dll', 'MonoMod.Utils.dll', 'MonoMod.RuntimeDetour.dll')) {
    Copy-Item -LiteralPath (Join-Path 'C:/Program Files (x86)/Steam/steamapps/common/Nuclear Option/BepInEx/core' $dependency) -Destination "$PreviewDirectory/Assets/"
}
if ($ReproducePreviousPrefix) {
    $patchPath = "$PreviewDirectory/Assets/SupportMissileVisualPatch.cs"
    $source = Get-Content -LiteralPath $patchPath -Raw
    $start = $source.IndexOf('        private static void Prefix(Missile __instance, out bool __state)')
    $end = $source.IndexOf('    [HarmonyPatch(typeof(Missile), "OnStartClient")]', $start)
    if ($start -lt 0 -or $end -lt 0) { throw 'Cannot locate authority patch for regression reproduction.' }
    $old = @'
        private static void Prefix(Missile __instance)
        {
            if (__instance == null || __instance.disabled || !BoscaliSummer.Runtime.GameAccess.IsServer()) return;
            if (__instance.UniqueName?.StartsWith("BoscaliSummer:Support:Rod:", StringComparison.Ordinal) == true)
                Runtime.RodBlast.Apply(__instance.transform.position, __instance.ownerID);
        }
    }

'@
    Set-Content -LiteralPath $patchPath -Value ($source.Substring(0, $start) + $old + $source.Substring($end))
}
$arguments = @("-batchmode", "-nographics", "-projectPath", ('"' + $PreviewDirectory + '"'), "-executeMethod", "RodBlastUnityCheck.Run", "-logFile", ('"' + "$PreviewDirectory/check.log" + '"'))
$process = Start-Process -FilePath $Unity -ArgumentList $arguments -WorkingDirectory $PreviewDirectory -WindowStyle Hidden -PassThru
Write-Output "Unity PID: $($process.Id)"
Write-Output "Results: $PreviewDirectory/result.txt"
