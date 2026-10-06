param(
    [string]$Unity = "C:/Program Files/Unity/Hub/Editor/2022.3.62f3/Editor/Unity.exe",
    [string]$PreviewDirectory = (Join-Path $env:TEMP ("BoscaliTrenchCheck-" + [guid]::NewGuid().ToString("N")))
)
$ErrorActionPreference = "Stop"
. "$PSScriptRoot/../../../UnityCheck.Common.ps1"
New-UnityCheckProject $PreviewDirectory '{"dependencies":{"com.unity.modules.assetbundle":"1.0.0","com.unity.modules.physics":"1.0.0","com.unity.modules.imageconversion":"1.0.0"}}'
Copy-Item -LiteralPath "$PSScriptRoot/TrenchUnityCheck.cs", "$PSScriptRoot/TrenchGameStubs.cs" -Destination "$PreviewDirectory/Assets/"
Copy-Item -LiteralPath "$repo/modules/Trenches/Visuals/TrenchMeshBuilder.cs", "$repo/modules/Trenches/Visuals/TrenchMaterialResolver.cs", "$repo/modules/Trenches/Visuals/TrenchVisualChunk.cs", "$repo/modules/Trenches/Visuals/TrenchNestVisual.cs" -Destination "$PreviewDirectory/Assets/"
Copy-Item -LiteralPath "$repo/modules/Trenches/Runtime/TrenchLine.cs", "$repo/modules/Trenches/Runtime/TrenchPlanner.cs", "$repo/modules/Trenches/Runtime/TrenchGarrison.cs", "$repo/modules/Trenches/Runtime/TrenchWorks.cs", "$repo/modules/Trenches/Domain/TrenchTraceMath.cs" -Destination "$PreviewDirectory/Assets/"
Copy-Item -LiteralPath "$repo/Core/Contracts/ITerritoryIngress.cs" -Destination "$PreviewDirectory/Assets/"
$process = Invoke-UnityCheck $Unity $PreviewDirectory 'TrenchUnityCheck.Run' -Flags '-batchmode'
Write-Output "Results and render: $PreviewDirectory"
if (Test-Path "$PreviewDirectory/result.txt") {
    Get-Content "$PreviewDirectory/result.txt"
} elseif (Test-Path "$PreviewDirectory/check.log") {
    Get-Content "$PreviewDirectory/check.log" -Tail 40
}
Assert-UnityResult $PreviewDirectory $process -Pattern '(?m)^PASS:' -ExitMessage "Trench Unity check failed: $($process.ExitCode) ($PreviewDirectory)" -FailMessage "Trench Unity check did not report a pass: $PreviewDirectory"
