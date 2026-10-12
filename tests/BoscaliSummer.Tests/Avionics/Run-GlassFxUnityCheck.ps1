param(
    [string]$Unity = 'C:/Program Files/Unity/Hub/Editor/2022.3.62f3/Editor/Unity.exe',
    [string]$PreviewDirectory = (Join-Path $env:TEMP ('GlassFxCheck-' + [guid]::NewGuid().ToString('N')))
)
# Offline look at the full-display finish (NOA/UI/Glass2): renders fixed instants to PNG and asserts the
# live-signal behaviour. Needs a Release build and tools/Compile-AvionicsFxAssets.ps1 to have produced the bundle.
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/../../UnityCheck.Common.ps1"
$proj = $PreviewDirectory
New-UnityCheckProject $proj $UnityCheckManifest.KitFull -Folders 'Assets/Harness'
Copy-Item "$repo/bin/Release/netstandard2.1/BoscaliSummer.dll" "$proj/Assets/"
Copy-Item "$PSScriptRoot/GlassFxUnityCheck.cs", "$PSScriptRoot/NOAvionicsKitChecks.asmdef" "$proj/Assets/Harness/"
Copy-Item "$repo/AvionicsUi/Assets/avionics-ui.bundle", "$repo/AvionicsUi/Assets/avionics-fx.bundle" "$proj/"
Copy-GameDlls "$proj/Assets/" $UnityCheckFilter.Managed $UnityCheckFilter.BepInEx
$p = Invoke-UnityCheck $Unity $proj 'GlassFxUnityCheck.Run' -Flags '-batchmode'
Write-Output "Results and renders: $proj"
Show-UnityCheckResult $proj
Assert-UnityResult $proj $p -Pattern '\APASS:' -ExitMessage "Unity glass check failed: $($p.ExitCode)" -FailMessage "Unity glass check did not report a pass: $proj"
