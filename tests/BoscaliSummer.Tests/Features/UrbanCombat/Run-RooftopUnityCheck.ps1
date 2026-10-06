param(
    [string]$Unity = "C:/Program Files/Unity/Hub/Editor/2022.3.62f3/Editor/Unity.exe",
    [string]$PreviewDirectory = (Join-Path $env:TEMP ("BoscaliRooftopCheck-" + [guid]::NewGuid().ToString("N"))),
    [switch]$BuildPlayer
)
$ErrorActionPreference = "Stop"
. "$PSScriptRoot/../../../UnityCheck.Common.ps1"
New-UnityCheckProject $PreviewDirectory '{"dependencies":{"com.unity.modules.physics":"1.0.0","com.unity.modules.imgui":"1.0.0","com.unity.modules.imageconversion":"1.0.0"}}'
Copy-Item -LiteralPath "$PSScriptRoot/RooftopUnityCheck.cs" -Destination "$PreviewDirectory/Assets/"
Copy-Item -LiteralPath "$repo/modules/UrbanCombat/Runtime/RooftopPlacement.cs", "$repo/modules/UrbanCombat/Runtime/GarrisonComposition.cs", "$repo/Core/Math/Deterministic.cs", "$repo/modules/UrbanCombat/Runtime/GarrisonMarkerInfo.cs", "$repo/modules/UrbanCombat/Runtime/StrongpointHitPolicy.cs", "$repo/modules/UrbanCombat/Runtime/NestRegistry.cs", "$repo/modules/UrbanCombat/Visuals/OccupiedBuildingMarking.cs", "$repo/modules/UrbanCombat/Visuals/FactionBannerTexture.cs", "$repo/modules/UrbanCombat/Visuals/GarrisonVisual.cs" -Destination "$PreviewDirectory/Assets/"
$entryPoint = if ($BuildPlayer) { "RooftopUnityCheck.BuildPlayer" } else { "RooftopUnityCheck.Run" }
# OccupiedBuildingMarking deliberately skips decoration when Application.isBatchMode
# (headless servers must not build it), so the editor check runs windowed but hidden,
# like the presentation check; only the player build itself stays headless.
$headless = @(if ($BuildPlayer) { "-batchmode" })
$process = Invoke-UnityCheck $Unity $PreviewDirectory $entryPoint -Flags ($headless + '-disable-assembly-updater') -Result 'results.txt' -InheritWorkingDirectory `
    -TimeoutSeconds (15 * 60) -TimeoutMessage "Rooftop Unity check timed out after 15 minutes: $PreviewDirectory"
Write-Output "Unity exit: $($process.ExitCode)"
Write-Output "Results, renders and log: $PreviewDirectory"
if (Test-Path -LiteralPath "$PreviewDirectory/results.txt") { Get-Content -LiteralPath "$PreviewDirectory/results.txt" }
if ($BuildPlayer) { Write-Output "After the build exits, run $PreviewDirectory/Player/RooftopCheck.exe windowed (no -batchmode: batch mode skips decoration by design) to execute player regression checks." }
if ($process.ExitCode -ne 0) { throw "Rooftop Unity check failed with exit $($process.ExitCode): $PreviewDirectory" }
if (!$BuildPlayer -and (!(Test-Path -LiteralPath "$PreviewDirectory/results.txt") -or
    !(Select-String -LiteralPath "$PreviewDirectory/results.txt" -Pattern '^PASS:' -Quiet))) {
    throw "Rooftop Unity check did not report a pass: $PreviewDirectory"
}
