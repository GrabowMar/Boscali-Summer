param(
    [string]$AtlasStyles,
    [string]$Unity = "C:/Program Files/Unity/Hub/Editor/2022.3.62f3/Editor/Unity.exe",
    [string]$PreviewDirectory = (Join-Path $env:TEMP ("BoscaliHudLogCheck-" + [guid]::NewGuid().ToString("N"))),
    # Comma list of TAC BOARD scope pages to render and nothing else of the check, e.g. 'systems' or 'threat,shot'
    # (systems threat shot missilecam approach wing strike dock objective), or 'shell' (the board's shell) / 'frame' (its layout, anchor, shade and furniture) / 'lines' (the wing's lamp line, gauge line and ticker at the declutter levels) / 'corners' (the kill feed and map frame, the visor's sway) / 'repair' (the faults the review of 2026-10-12 found). Empty: every page plus the rest of the check.
    [string]$Pages = '',
    # The TAC BOARD spec folder: its cockpit capture is the backdrop of the renders, so they line up with its shots/.
    [string]$SpecDirectory = ''
)
$ErrorActionPreference = "Stop"
. "$PSScriptRoot/../../../UnityCheck.Common.ps1"
if (-not $SpecDirectory) {
    # The spec is local-only (gitignored): a page-group worktree falls back to the foundation worktree's copy.
    $SpecDirectory = "$repo/docs/superpowers/specs/2026-10-10-hud-v8-tac-board"
    if (-not (Test-Path -LiteralPath $SpecDirectory)) { $SpecDirectory = "C:/Users/marci/dev/cmenu-hud/docs/superpowers/specs/2026-10-10-hud-v8-tac-board" }
}
New-UnityCheckProject $PreviewDirectory '{"dependencies":{"com.unity.ugui":"1.0.0","com.unity.textmeshpro":"3.0.6","com.unity.modules.audio":"1.0.0","com.unity.modules.imageconversion":"1.0.0","com.unity.modules.uielements":"1.0.0","com.unity.modules.physics":"1.0.0","com.unity.modules.animation":"1.0.0","com.unity.modules.ai":"1.0.0","com.unity.modules.particlesystem":"1.0.0"}}' -Folders 'Assets/Harness', 'NOAvionics'
Copy-GameDlls "$PreviewDirectory/Assets/" $UnityCheckFilter.ManagedWide $UnityCheckFilter.BepInEx
Copy-Item -LiteralPath "$repo/bin/Release/netstandard2.1/BoscaliSummer.dll" -Destination "$PreviewDirectory/Assets/"
# HudLogUnityCheck.cs and its partial files (HudLogUnityCheck.Scope.cs, HudLogUnityCheck.Scope.<Page>.cs) compile together.
$check = (Get-ChildItem -LiteralPath $PSScriptRoot -Filter 'HudLogUnityCheck*.cs').FullName
Copy-Item -LiteralPath (@($check) + $UnityCheckHarness + "$PSScriptRoot/../Command/BoscaliStockPreview.asmdef") -Destination "$PreviewDirectory/Assets/Harness/"
Copy-AvionicsStyles $PreviewDirectory $AtlasStyles
$extra = @('-spec', ('"' + $SpecDirectory + '"'))
if ($Pages) { $extra += @('-pages', $Pages) }
$process = Invoke-UnityCheck $Unity $PreviewDirectory 'HudLogUnityCheck.Run' -ExtraArguments $extra
Write-Output "Results and renders: $PreviewDirectory"
Show-UnityCheckResult $PreviewDirectory -LogTail 80
Assert-UnityResult $PreviewDirectory $process -ExitMessage "Unity HUD/log check failed: $($process.ExitCode)"
Write-Output "Scope shots: $((Get-ChildItem -LiteralPath $PreviewDirectory -Filter '*.png' | Where-Object { $_.Name -match '^[a-z]+-t[0-9.]+(-[a-z]+)?-[0-9]+x[0-9]+[.]png$' } | Measure-Object).Count) in $PreviewDirectory (compare: tests/BoscaliSummer.Tests/Features/Hud/Compare-ScopeShots.py)"
