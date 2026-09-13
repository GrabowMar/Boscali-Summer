param(
    [string]$Unity = "C:/Program Files/Unity/Hub/Editor/2022.3.62f3/Editor/Unity.exe",
    [string]$PreviewDirectory = (Join-Path $env:TEMP ("BoscaliTrenchCheck-" + [guid]::NewGuid().ToString("N")))
)
$ErrorActionPreference = "Stop"
$repo = (Resolve-Path (Join-Path $PSScriptRoot "../../../..")).Path
New-Item -ItemType Directory -Force -Path "$PreviewDirectory/Assets", "$PreviewDirectory/ProjectSettings", "$PreviewDirectory/Packages" | Out-Null
Set-Content -LiteralPath "$PreviewDirectory/ProjectSettings/ProjectVersion.txt" -Value "m_EditorVersion: 2022.3.62f3"
Set-Content -LiteralPath "$PreviewDirectory/Packages/manifest.json" -Value '{"dependencies":{"com.unity.modules.physics":"1.0.0","com.unity.modules.imageconversion":"1.0.0"}}'
Copy-Item -LiteralPath "$PSScriptRoot/TrenchUnityCheck.cs", "$repo/modules/Trenches/Visuals/TrenchMeshBuilder.cs" -Destination "$PreviewDirectory/Assets/"
Copy-Item -LiteralPath "$repo/modules/Trenches/Runtime/TrenchNetwork.cs", "$repo/modules/Trenches/Runtime/TrenchNode.cs", "$repo/modules/Trenches/Runtime/TrenchEdge.cs", "$repo/modules/Trenches/Runtime/TrenchGrowthSimulator.cs", "$repo/modules/Trenches/Runtime/TrenchGarrison.cs", "$repo/modules/Trenches/Domain/TrenchTacticalMath.cs", "$PSScriptRoot/TrenchGameStubs.cs" -Destination "$PreviewDirectory/Assets/"
$arguments = @("-batchmode", "-projectPath", ('"' + $PreviewDirectory + '"'), "-executeMethod", "TrenchUnityCheck.Run", "-logFile", ('"' + "$PreviewDirectory/check.log" + '"'))
$process = Start-Process -FilePath $Unity -ArgumentList $arguments -WorkingDirectory $PreviewDirectory -WindowStyle Hidden -PassThru
Write-Output "Unity PID: $($process.Id)"
Write-Output "Results and render: $PreviewDirectory"
