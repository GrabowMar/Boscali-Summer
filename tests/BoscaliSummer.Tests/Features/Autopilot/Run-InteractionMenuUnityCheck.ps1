param(
    [string]$Unity = "C:/Program Files/Unity/Hub/Editor/2022.3.62f3/Editor/Unity.exe",
    [string]$PreviewDirectory = (Join-Path $env:TEMP ("BoscaliInteractionMenuCheck-" + [guid]::NewGuid().ToString("N"))),
    [string]$Backdrop
)
$ErrorActionPreference = "Stop"
if (Test-Path -LiteralPath "$PreviewDirectory/result.txt") { Remove-Item -LiteralPath "$PreviewDirectory/result.txt" }
$repo = (Resolve-Path (Join-Path $PSScriptRoot "../../../..")).Path
New-Item -ItemType Directory -Force -Path "$PreviewDirectory/Assets", "$PreviewDirectory/ProjectSettings", "$PreviewDirectory/Packages", "$PreviewDirectory/NOAvionics" | Out-Null
Set-Content -LiteralPath "$PreviewDirectory/ProjectSettings/ProjectVersion.txt" -Value "m_EditorVersion: 2022.3.62f3"
Set-Content -LiteralPath "$PreviewDirectory/Packages/manifest.json" -Value '{"dependencies":{"com.unity.ugui":"1.0.0","com.unity.textmeshpro":"3.0.9","com.unity.modules.audio":"1.0.0","com.unity.modules.physics":"1.0.0","com.unity.modules.imageconversion":"1.0.0","com.unity.modules.uielements":"1.0.0","com.unity.modules.assetbundle":"1.0.0","com.unity.modules.imgui":"1.0.0"}}'
Get-ChildItem -LiteralPath "$repo/Avionics", "$repo/AvionicsUi" -Filter '*.cs' | Where-Object { $_.Name -notlike '*Tests.cs' } | Copy-Item -Destination "$PreviewDirectory/Assets/"
# AvDisplayGlass (kit v2's AvLay.Fill) and the rest of the shared kit live one level down.
Get-ChildItem -LiteralPath "$repo/AvionicsUi/Fui" -Filter '*.cs' | Copy-Item -Destination "$PreviewDirectory/Assets/"
Get-ChildItem -LiteralPath "$repo/modules/Autopilot/Presentation" -Filter 'AceRadial*.cs' | Copy-Item -Destination "$PreviewDirectory/Assets/"
Get-ChildItem -LiteralPath "$repo/modules/Autopilot/Domain" -Filter 'AceRadial*.cs' | Copy-Item -Destination "$PreviewDirectory/Assets/"
Copy-Item -LiteralPath "$PSScriptRoot/InteractionMenuUnityStubs.cs", "$PSScriptRoot/InteractionMenuUnityCheck.cs" -Destination "$PreviewDirectory/Assets/"
$arguments = @('-batchmode', '-projectPath', ('"' + $PreviewDirectory + '"'), '-executeMethod', 'InteractionMenuUnityCheck.Run', '-logFile', ('"' + "$PreviewDirectory/check.log" + '"'))
if ($Backdrop) { $arguments += @('-menuBackdrop', ('"' + (Resolve-Path -LiteralPath $Backdrop).Path + '"')) }
$process = Start-Process -FilePath $Unity -ArgumentList $arguments -WorkingDirectory $PreviewDirectory -WindowStyle Hidden -PassThru
$process.WaitForExit()
Write-Output "Results and renders: $PreviewDirectory"
if (Test-Path -LiteralPath "$PreviewDirectory/result.txt") { Get-Content -LiteralPath "$PreviewDirectory/result.txt" }
else { Get-Content -LiteralPath "$PreviewDirectory/check.log" -Tail 60 }
if ($process.ExitCode -ne 0) { throw "Unity interaction-menu check failed: $($process.ExitCode)" }
if (!(Test-Path -LiteralPath "$PreviewDirectory/result.txt")) { throw "Unity interaction-menu check did not produce a result." }
