param(
    [string]$Unity = 'C:/Program Files/Unity/Hub/Editor/2022.3.62f3/Editor/Unity.exe',
    # Keep this short (for example %TEMP%\x): a long path hangs the editor on the TextMesh Pro import.
    [string]$PreviewDirectory = (Join-Path $env:TEMP ('BoscaliMet-' + [guid]::NewGuid().ToString('N').Substring(0, 8))),
    [int]$TimeoutSeconds = 420,
    [string]$PluginDll = '',
    # Portal theme only: the loop while building a column. The finished window must pass without it.
    [switch]$Fast
)
# Renders the Met Office (the weather console) in ten states on every theme at 1920x1080 / 1.5x into <PreviewDirectory>/renders and gates its text, sections, hit regions and presses.
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/../../../UnityCheck.Common.ps1"
if (-not $PluginDll) { $PluginDll = Join-Path $repo 'bin/Release/netstandard2.1/BoscaliSummer.dll' }
$PluginDll = (Resolve-Path -LiteralPath $PluginDll).Path
New-UnityCheckProject $PreviewDirectory $UnityCheckManifest.KitFull -Folders 'Assets/Harness', 'NOAvionics', 'renders'
Copy-Item -LiteralPath $PluginDll -Destination "$PreviewDirectory/Assets/BoscaliSummer.dll"
Copy-Item -LiteralPath "$PSScriptRoot/MetOfficeUnityCheck.cs", $UnityCheckHarness, "$PSScriptRoot/BoscaliWeatherEnv.asmdef" -Destination "$PreviewDirectory/Assets/Harness/"
Copy-Item -LiteralPath "$repo/AvionicsUi/Assets/avionics-ui.bundle" -Destination "$PreviewDirectory/"
Copy-GameDlls "$PreviewDirectory/Assets/" $UnityCheckFilter.Managed $UnityCheckFilter.BepInEx
Copy-AvionicsStyles $PreviewDirectory ''
if ($Fast) { $env:MET_FAST = '1' } else { Remove-Item Env:MET_FAST -ErrorAction SilentlyContinue }
Write-Output "Plugin DLL: $PluginDll (SHA256 $((Get-FileHash -LiteralPath $PluginDll -Algorithm SHA256).Hash))"
$process = Invoke-UnityCheck $Unity $PreviewDirectory 'MetOfficeUnityCheck.Run' -TimeoutSeconds $TimeoutSeconds `
    -TimeoutMessage "Unity Met Office check timed out after $TimeoutSeconds seconds: $PreviewDirectory/check.log"
Write-Output "Results and renders: $PreviewDirectory/renders"
Show-UnityCheckResult $PreviewDirectory -LogTail 90
Assert-UnityResult $PreviewDirectory $process -Pattern '(?m)^PASS: MET OFFICE' -ExitMessage "Unity Met Office check failed: $($process.ExitCode) ($PreviewDirectory)" -FailMessage "Unity Met Office check did not report a pass: $PreviewDirectory"
