param(
    [string]$Unity = "C:/Program Files/Unity/Hub/Editor/2022.3.62f3/Editor/Unity.exe",
    [string]$PreviewDirectory = (Join-Path $env:TEMP ("BoscaliSoldierCheck-" + [guid]::NewGuid().ToString("N")))
)
$ErrorActionPreference = "Stop"
$repo = (Resolve-Path (Join-Path $PSScriptRoot "../../../..")).Path
New-Item -ItemType Directory -Force -Path "$PreviewDirectory/Assets", "$PreviewDirectory/ProjectSettings", "$PreviewDirectory/Packages" | Out-Null
Set-Content -LiteralPath "$PreviewDirectory/ProjectSettings/ProjectVersion.txt" -Value "m_EditorVersion: 2022.3.62f3"
Set-Content -LiteralPath "$PreviewDirectory/Packages/manifest.json" -Value '{"dependencies":{"com.unity.modules.physics":"1.0.0","com.unity.modules.imgui":"1.0.0","com.unity.modules.animation":"1.0.0"}}'
Copy-Item -LiteralPath "$PSScriptRoot/SoldierFactoryUnityCheck.cs", "$repo/modules/UrbanCombat/Visuals/VanillaSoldierFactory.cs", "$repo/modules/UrbanCombat/Visuals/MaterialProvider.cs" -Destination "$PreviewDirectory/Assets/"
$arguments = @("-batchmode", "-projectPath", ('"' + $PreviewDirectory + '"'), "-executeMethod", "SoldierFactoryUnityCheck.Run", "-logFile", ('"' + "$PreviewDirectory/check.log" + '"'))
$process = Start-Process -FilePath $Unity -ArgumentList $arguments -WindowStyle Hidden -PassThru -Wait
Write-Output "Unity exit: $($process.ExitCode)"
Write-Output "Results and log: $PreviewDirectory"
if ($process.ExitCode -ne 0) { throw "Soldier factory check failed with exit $($process.ExitCode)" }
Select-String "$PreviewDirectory/check.log" -Pattern 'SoldierFactoryCheck.*assertions passed|FAIL' | Select-Object -Last 3
