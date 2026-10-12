param(
    [string]$Unity = "C:/Program Files/Unity/Hub/Editor/2022.3.62f3/Editor/Unity.exe",
    [string]$PreviewDirectory = (Join-Path $env:TEMP ("BoscaliMinisCheck-" + [guid]::NewGuid().ToString("N")))
)
$ErrorActionPreference = "Stop"
. "$PSScriptRoot/../../../UnityCheck.Common.ps1"
New-UnityCheckProject $PreviewDirectory '{"dependencies":{"com.unity.ugui":"1.0.0","com.unity.modules.imageconversion":"1.0.0","com.unity.modules.ui":"1.0.0"}}'
# The miniatures are pure UnityEngine: the sheet compiles them plus the atlas that owns the glyph enum.
Copy-Item -LiteralPath "$repo/modules/Command/Presentation/MapUi/MapMinis.cs", "$repo/modules/Command/Presentation/MapUi/MapSymbolAtlas.cs", "$PSScriptRoot/MapMinisUnityCheck.cs" -Destination "$PreviewDirectory/Assets/"
$process = Invoke-UnityCheck $Unity $PreviewDirectory 'MapMinisUnityCheck.Run' -Flags '-batchmode'
Write-Output "Results and renders: $PreviewDirectory"
Show-UnityCheckResult $PreviewDirectory
Assert-UnityResult $PreviewDirectory $process -ExitMessage "Unity miniature sheet failed: $($process.ExitCode)"
