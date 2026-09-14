param(
    [string]$Unity = "C:/Program Files/Unity/Hub/Editor/2022.3.62f3/Editor/Unity.exe",
    [string]$PreviewDirectory = (Join-Path $env:TEMP ("BoscaliRooftopCheck-" + [guid]::NewGuid().ToString("N"))),
    [switch]$BuildPlayer
)
$ErrorActionPreference = "Stop"
$repo = (Resolve-Path (Join-Path $PSScriptRoot "../../../..")).Path
New-Item -ItemType Directory -Force -Path "$PreviewDirectory/Assets", "$PreviewDirectory/ProjectSettings", "$PreviewDirectory/Packages" | Out-Null
Set-Content -LiteralPath "$PreviewDirectory/ProjectSettings/ProjectVersion.txt" -Value "m_EditorVersion: 2022.3.62f3"
Set-Content -LiteralPath "$PreviewDirectory/Packages/manifest.json" -Value '{"dependencies":{"com.unity.modules.physics":"1.0.0","com.unity.modules.imgui":"1.0.0","com.unity.modules.imageconversion":"1.0.0"}}'
Copy-Item -LiteralPath "$PSScriptRoot/RooftopUnityCheck.cs" -Destination "$PreviewDirectory/Assets/"
Copy-Item -LiteralPath "$repo/modules/UrbanCombat/Runtime/RooftopPlacement.cs", "$repo/modules/UrbanCombat/Runtime/GarrisonMarkerInfo.cs", "$repo/modules/UrbanCombat/Visuals/OccupiedBuildingMarking.cs", "$repo/modules/UrbanCombat/Visuals/GarrisonVisual.cs" -Destination "$PreviewDirectory/Assets/"
$entryPoint = if ($BuildPlayer) { "RooftopUnityCheck.BuildPlayer" } else { "RooftopUnityCheck.Run" }
$arguments = @("-batchmode", "-projectPath", ('"' + $PreviewDirectory + '"'), "-executeMethod", $entryPoint, "-logFile", ('"' + "$PreviewDirectory/check.log" + '"'))
$process = Start-Process -FilePath $Unity -ArgumentList $arguments -WindowStyle Hidden -PassThru
Write-Output "Unity PID: $($process.Id)"
Write-Output "Results, renders and log: $PreviewDirectory"
if ($BuildPlayer) { Write-Output "After the build exits, run $PreviewDirectory/Player/RooftopCheck.exe -batchmode to execute player regression checks." }

