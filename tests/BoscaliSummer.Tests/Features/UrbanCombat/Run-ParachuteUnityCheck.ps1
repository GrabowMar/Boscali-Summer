param(
    [string]$Unity = "C:/Program Files/Unity/Hub/Editor/2022.3.62f3/Editor/Unity.exe",
    [string]$PreviewDirectory = (Join-Path $env:TEMP ("BoscaliParachuteCheck-" + [guid]::NewGuid().ToString("N")))
)
$ErrorActionPreference = "Stop"
. "$PSScriptRoot/../../../UnityCheck.Common.ps1"
New-UnityCheckProject $PreviewDirectory '{"dependencies":{"com.unity.modules.imageconversion":"1.0.0","com.unity.modules.imgui":"1.0.0"}}'
Copy-Item -LiteralPath "$PSScriptRoot/ParachuteUnityCheck.cs", "$repo/modules/UrbanCombat/Visuals/ParachuteMeshBuilder.cs" -Destination "$PreviewDirectory/Assets/"
$process = Invoke-UnityCheck $Unity $PreviewDirectory 'ParachuteUnityCheck.Run' -Flags '-batchmode' -InheritWorkingDirectory `
    -TimeoutSeconds (15 * 60) -TimeoutMessage "Parachute Unity check timed out: $PreviewDirectory"
Write-Output "Results, renders and log: $PreviewDirectory"
if ($process.ExitCode -ne 0 -or
    !(Select-String -LiteralPath "$PreviewDirectory/check.log" -Pattern '\[ParachuteCheck\].*assertions passed' -Quiet)) {
    throw "Parachute Unity check did not report a pass: $PreviewDirectory"
}
