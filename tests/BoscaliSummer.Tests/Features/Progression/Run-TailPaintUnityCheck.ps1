param(
    [string]$Unity = 'C:/Program Files/Unity/Hub/Editor/2022.3.62f3/Editor/Unity.exe',
    [string]$PreviewDirectory = (Join-Path $env:TEMP ('BoscaliTailPaintCheck-' + [guid]::NewGuid().ToString('N')))
)
# The tail-fin insignia bake against synthetic fins, through the production TailPaint.Bake and the bundle
# embedded in bin/Release/netstandard2.1/BoscaliSummer.dll (build Release first). Needs a GPU: batch mode, with graphics.
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/../../../UnityCheck.Common.ps1"
New-UnityCheckProject $PreviewDirectory $UnityCheckManifest.MfdFull -Folders 'Assets/Harness'
Copy-GameDlls "$PreviewDirectory/Assets/" $UnityCheckFilter.ManagedWide $UnityCheckFilter.BepInEx
Copy-Item -LiteralPath "$repo/bin/Release/netstandard2.1/BoscaliSummer.dll" -Destination "$PreviewDirectory/Assets/"
Copy-Item -LiteralPath "$PSScriptRoot/TailPaintUnityCheck.cs", $UnityCheckHarness, "$PSScriptRoot/BoscaliPresentationPreview.asmdef" -Destination "$PreviewDirectory/Assets/Harness/"
$process = Invoke-UnityCheck $Unity $PreviewDirectory 'TailPaintUnityCheck.Run' -TimeoutSeconds 600
Write-Output "Results and read-back atlases: $PreviewDirectory"
Show-UnityCheckResult $PreviewDirectory -LogTail 80
Assert-UnityResult $PreviewDirectory $process -ExitMessage "Unity tail paint check failed: $($process.ExitCode)"
