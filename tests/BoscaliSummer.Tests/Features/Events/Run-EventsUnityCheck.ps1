param(
    [string]$Unity = "C:/Program Files/Unity/Hub/Editor/2022.3.62f3/Editor/Unity.exe",
    [string]$PreviewDirectory = (Join-Path $env:TEMP ("BoscaliEventsCheck-" + [guid]::NewGuid().ToString("N"))),
    [string]$AtlasStyles,
    [switch]$PrepareOnly
)
# Offline Unity gate for the EVN console and its field archive window: real presentation sources, stubbed game
# types, rendered pages plus the overlap / overflow / contrast gate from the kit gallery.
$ErrorActionPreference = "Stop"
$repo = (Resolve-Path (Join-Path $PSScriptRoot "../../../..")).Path
New-Item -ItemType Directory -Force -Path "$PreviewDirectory/Assets", "$PreviewDirectory/ProjectSettings", "$PreviewDirectory/Packages", "$PreviewDirectory/NOAvionics", "$PreviewDirectory/BepInEx/plugins/BoscaliSummer/Events" | Out-Null
Set-Content -LiteralPath "$PreviewDirectory/ProjectSettings/ProjectVersion.txt" -Value "m_EditorVersion: 2022.3.62f3"
Set-Content -LiteralPath "$PreviewDirectory/Packages/manifest.json" -Value '{"dependencies":{"com.unity.ugui":"1.0.0","com.unity.textmeshpro":"3.0.6","com.unity.modules.audio":"1.0.0","com.unity.modules.imageconversion":"1.0.0","com.unity.modules.uielements":"1.0.0","com.unity.modules.assetbundle":"1.0.0","com.unity.modules.physics":"1.0.0"}}'
Get-ChildItem -LiteralPath "$repo/AvionicsUi", "$repo/AvionicsUi/Pure" -Filter '*.cs' | Where-Object { $_.Name -notlike '*Tests.cs' } | Copy-Item -Destination "$PreviewDirectory/Assets/"
Get-ChildItem -LiteralPath "$repo/AvionicsUi/Fui" -Filter '*.cs' | Copy-Item -Destination "$PreviewDirectory/Assets/"
Copy-Item -LiteralPath "$repo/AvionicsUi/avionics.fui.avss", "$repo/AvionicsUi/avionics.steel.avss", "$repo/AvionicsUi/avionics.ace.avss", "$repo/AvionicsUi/avionics.phosphor.avss" -Destination "$PreviewDirectory/NOAvionics/"
Copy-Item -LiteralPath "$repo/AvionicsUi/Assets/avionics-ui.bundle" -Destination "$PreviewDirectory/"
Copy-Item -LiteralPath "$repo/modules/Events/Assets/event_atlas.png" -Destination "$PreviewDirectory/BepInEx/plugins/BoscaliSummer/Events/"
# The module's own presentation (no alert / plane HUD / tone), its pure domain, settings and the two shared files it reads.
Get-ChildItem -LiteralPath "$repo/modules/Events/Presentation" -Filter '*.cs' |
    Where-Object { $_.Name -notin @('SuperEventAlert.cs', 'SuperEventPlaneHud.cs', 'EventAlertTone.cs') } |
    Copy-Item -Destination "$PreviewDirectory/Assets/"
Get-ChildItem -LiteralPath "$repo/modules/Events/Domain" -Filter '*.cs' | Copy-Item -Destination "$PreviewDirectory/Assets/"
Copy-Item -LiteralPath "$repo/modules/Events/Configuration/EventsSettings.cs", "$repo/Core/Contracts/IActiveEventsView.cs", "$repo/Core/Math/Deterministic.cs", "$PSScriptRoot/EventsUnityCheck.cs", "$PSScriptRoot/EventsUnityStubs.cs" -Destination "$PreviewDirectory/Assets/"
Copy-Item -LiteralPath 'C:/Program Files (x86)/Steam/steamapps/common/Nuclear Option/BepInEx/core/BepInEx.dll' -Destination "$PreviewDirectory/Assets/"
Get-ChildItem -LiteralPath 'C:/Program Files (x86)/Steam/steamapps/common/Nuclear Option/BepInEx/core' -Filter '*.dll' | Where-Object { $_.Name -match '^(Mono|0Harmony\.dll$)' } | Copy-Item -Destination "$PreviewDirectory/Assets/"
if (Test-Path -LiteralPath "$PreviewDirectory/result.txt") { Remove-Item -LiteralPath "$PreviewDirectory/result.txt" }
$arguments = @('-batchmode', '-projectPath', ('"' + $PreviewDirectory + '"'), '-executeMethod', 'EventsUnityCheck.Run', '-logFile', ('"' + "$PreviewDirectory/check.log" + '"'))
if ($AtlasStyles -and (Test-Path -LiteralPath "$AtlasStyles/NOAvionics")) {
    Get-ChildItem -LiteralPath "$AtlasStyles/NOAvionics" -Filter '*.avss' | Copy-Item -Destination "$PreviewDirectory/NOAvionics/"
}
if ($PrepareOnly) { Write-Output "Prepared preview project: $PreviewDirectory"; return }
$process = Start-Process -FilePath $Unity -ArgumentList $arguments -WorkingDirectory $PreviewDirectory -WindowStyle Hidden -PassThru
$process.WaitForExit()
Write-Output "Results and renders: $PreviewDirectory"
if (Test-Path "$PreviewDirectory/result.txt") { Get-Content "$PreviewDirectory/result.txt" }
else { Get-Content "$PreviewDirectory/check.log" -Tail 60 }
if ($process.ExitCode -ne 0) { throw "Unity events check failed: $($process.ExitCode)" }
if (!(Test-Path -LiteralPath "$PreviewDirectory/result.txt") -or
    !(Select-String -LiteralPath "$PreviewDirectory/result.txt" -Pattern '^PASS:' -Quiet)) {
    throw "Unity events check did not report a pass: $PreviewDirectory"
}
