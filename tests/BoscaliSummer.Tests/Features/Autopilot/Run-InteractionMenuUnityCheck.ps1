param(
    [string]$AtlasStyles,
    [string]$Unity = "C:/Program Files/Unity/Hub/Editor/2022.3.62f3/Editor/Unity.exe",
    [string]$PreviewDirectory = (Join-Path $env:TEMP ("BoscaliInteractionMenuCheck-" + [guid]::NewGuid().ToString("N"))),
    # A cockpit screenshot (PNG; Unity cannot load webp or jpg reliably) behind the menu. Default: the checkout's local backdrop, if it exists.
    [string]$Backdrop,
    # Comma-separated gallery ids to render (default: all), e.g. "root-idle,flight-dock".
    [string]$States,
    # The prototype's accepted shots, for the side-by-side comparison sheets.
    [string]$Shots
)
$ErrorActionPreference = "Stop"
. "$PSScriptRoot/../../../UnityCheck.Common.ps1"
$spec = "$repo/docs/superpowers/specs/2026-10-10-cmenu-v7"
if (-not $Backdrop -and (Test-Path -LiteralPath "$spec/backdrops/cockpit-cricket.png")) { $Backdrop = "$spec/backdrops/cockpit-cricket.png" }
if (-not $Shots -and (Test-Path -LiteralPath "$spec/prototype/shots")) { $Shots = "$spec/prototype/shots" }
New-UnityCheckProject $PreviewDirectory '{"dependencies":{"com.unity.ugui":"1.0.0","com.unity.textmeshpro":"3.0.9","com.unity.modules.audio":"1.0.0","com.unity.modules.physics":"1.0.0","com.unity.modules.imageconversion":"1.0.0","com.unity.modules.uielements":"1.0.0","com.unity.modules.assetbundle":"1.0.0","com.unity.modules.imgui":"1.0.0"}}' -Folders 'NOAvionics'
Get-ChildItem -LiteralPath "$repo/AvionicsUi", "$repo/AvionicsUi/Pure" -Filter '*.cs' | Where-Object { $_.Name -notlike '*Tests.cs' } | Copy-Item -Destination "$PreviewDirectory/Assets/"
# AvDisplayGlass (kit v2's AvLay.Fill) and the rest of the shared kit live one level down.
Get-ChildItem -LiteralPath "$repo/AvionicsUi/Fui" -Filter '*.cs' | Copy-Item -Destination "$PreviewDirectory/Assets/"
# The v7 menu: every pure Domain/Menu file (model, catalog, layout, controller, hint, icon table) and the three presentation files that draw it.
Get-ChildItem -LiteralPath "$repo/modules/Autopilot/Domain/Menu" -Filter '*.cs' | Copy-Item -Destination "$PreviewDirectory/Assets/"
foreach ($f in 'MenuPalette.cs', 'MenuTextPool.cs', 'MenuPainter.cs', 'MenuPainter.Dock.cs') { Copy-Item -LiteralPath "$repo/modules/Autopilot/Presentation/$f" -Destination "$PreviewDirectory/Assets/" }
# The same pure fixtures the unit tests use: scenario worlds, the input rig, the geometry checks (and the test font).
foreach ($f in 'MenuWorlds.cs', 'MenuRig.cs', 'MenuGeometryChecks.cs') { Copy-Item -LiteralPath "$repo/tests/BoscaliSummer.Tests/Features/Autopilot/$f" -Destination "$PreviewDirectory/Assets/" }
Copy-Item -LiteralPath "$PSScriptRoot/InteractionMenuUnityStubs.cs", "$PSScriptRoot/InteractionMenuUnityCheck.cs", $UnityCheckHarness -Destination "$PreviewDirectory/Assets/"
Copy-Item -LiteralPath "$repo/AvionicsUi/Assets/avionics-ui.bundle" -Destination "$PreviewDirectory/avionics-ui.bundle"
Copy-AvionicsStyles $PreviewDirectory $AtlasStyles
$extra = @()
if ($Backdrop) { $extra += @('-menuBackdrop', ('"' + (Resolve-Path -LiteralPath $Backdrop).Path + '"')) }
if ($States) { $extra += @('-menuStates', $States) }
$process = Invoke-UnityCheck $Unity $PreviewDirectory 'InteractionMenuUnityCheck.Run' -Flags '-batchmode' -ExtraArguments $extra
Write-Output "Results and renders: $PreviewDirectory"
Show-UnityCheckResult $PreviewDirectory
Assert-UnityResult $PreviewDirectory $process -Pattern '\APASS:' -ExitMessage "Unity interaction-menu check failed: $($process.ExitCode)" -FailMessage 'Unity interaction-menu check did not produce a PASS result.'
if ($Shots) {
    # PASS is not visual acceptance: build one image per state, our render left, the prototype's accepted shot right.
    python "$PSScriptRoot/Compare-MenuShots.py" $PreviewDirectory (Resolve-Path -LiteralPath $Shots).Path "$PreviewDirectory/compare"
    Write-Output "Side-by-side sheets: $PreviewDirectory/compare (read them, then say which differences are acceptable)"
}
