param(
    [string]$Unity = "C:/Program Files/Unity/Hub/Editor/2022.3.62f3/Editor/Unity.exe",
    [string]$PreviewDirectory = (Join-Path $env:TEMP ("BoscaliSoldierCheck-" + [guid]::NewGuid().ToString("N")))
)
$ErrorActionPreference = "Stop"
. "$PSScriptRoot/../../../UnityCheck.Common.ps1"
New-UnityCheckProject $PreviewDirectory '{"dependencies":{"com.unity.modules.physics":"1.0.0","com.unity.modules.imgui":"1.0.0","com.unity.modules.animation":"1.0.0"}}'
Copy-Item -LiteralPath "$PSScriptRoot/SoldierFactoryUnityCheck.cs", "$repo/modules/UrbanCombat/Visuals/VanillaSoldierFactory.cs", "$repo/modules/UrbanCombat/Visuals/MaterialProvider.cs" -Destination "$PreviewDirectory/Assets/"
$process = Invoke-UnityCheck $Unity $PreviewDirectory 'SoldierFactoryUnityCheck.Run' -Flags '-batchmode' -InheritWorkingDirectory
Write-Output "Unity exit: $($process.ExitCode)"
Write-Output "Results and log: $PreviewDirectory"
if ($process.ExitCode -ne 0) { throw "Soldier factory check failed with exit $($process.ExitCode)" }
Select-String "$PreviewDirectory/check.log" -Pattern '\[SoldierFactoryCheck\].*(assertions passed|FAIL)' | Select-Object -Last 3
if (!(Select-String -LiteralPath "$PreviewDirectory/check.log" -Pattern '\[SoldierFactoryCheck\].*assertions passed' -Quiet) -or
    (Select-String -LiteralPath "$PreviewDirectory/check.log" -Pattern '\[SoldierFactoryCheck\] FAIL:' -Quiet)) {
    throw "Soldier factory check did not report a pass: $PreviewDirectory"
}
