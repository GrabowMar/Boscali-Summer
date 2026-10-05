param(
    [string]$Unity = "C:/Program Files/Unity/Hub/Editor/2022.3.62f3/Editor/Unity.exe",
    [string]$PreviewDirectory = (Join-Path $env:TEMP ("BoscaliParachuteCheck-" + [guid]::NewGuid().ToString("N")))
)
$ErrorActionPreference = "Stop"
$repo = (Resolve-Path (Join-Path $PSScriptRoot "../../../..")).Path
New-Item -ItemType Directory -Force -Path "$PreviewDirectory/Assets", "$PreviewDirectory/ProjectSettings", "$PreviewDirectory/Packages" | Out-Null
Set-Content -LiteralPath "$PreviewDirectory/ProjectSettings/ProjectVersion.txt" -Value "m_EditorVersion: 2022.3.62f3"
Set-Content -LiteralPath "$PreviewDirectory/Packages/manifest.json" -Value '{"dependencies":{"com.unity.modules.imageconversion":"1.0.0","com.unity.modules.imgui":"1.0.0"}}'
Copy-Item -LiteralPath "$PSScriptRoot/ParachuteUnityCheck.cs", "$repo/modules/UrbanCombat/Visuals/ParachuteMeshBuilder.cs" -Destination "$PreviewDirectory/Assets/"
$arguments = @("-batchmode", "-projectPath", ('"' + $PreviewDirectory + '"'), "-executeMethod", "ParachuteUnityCheck.Run", "-logFile", ('"' + "$PreviewDirectory/check.log" + '"'))
$process = Start-Process -FilePath $Unity -ArgumentList $arguments -WorkingDirectory $PreviewDirectory -WindowStyle Hidden -PassThru
if (-not $process.WaitForExit(15 * 60 * 1000)) { $process.Kill(); throw "Parachute Unity check timed out: $PreviewDirectory" }
Write-Output "Results, renders and log: $PreviewDirectory"
if ($process.ExitCode -ne 0 -or
    !(Select-String -LiteralPath "$PreviewDirectory/check.log" -Pattern '\[ParachuteCheck\].*assertions passed' -Quiet)) {
    throw "Parachute Unity check did not report a pass: $PreviewDirectory"
}
