param(
    [string]$Unity = "C:/Program Files/Unity/Hub/Editor/2022.3.62f3/Editor/Unity.exe",
    [string]$PreviewDirectory = (Join-Path $env:TEMP ("BoscaliSymbolCheck-" + [guid]::NewGuid().ToString("N"))),
    [string]$VanillaIcons,
    [string]$Backdrop
)
$ErrorActionPreference = "Stop"
$repo = (Resolve-Path (Join-Path $PSScriptRoot "../../../..")).Path
if ($VanillaIcons) { $env:BOSCALI_VANILLA_ICONS = $VanillaIcons }
if ($Backdrop) { $env:BOSCALI_BACKDROP = $Backdrop }
elseif (Test-Path -LiteralPath (Join-Path $env:TEMP "BoscaliMapSources/terrain2_basecolor_preview.png")) {
    $env:BOSCALI_BACKDROP = Join-Path $env:TEMP "BoscaliMapSources/terrain2_basecolor_preview.png"
}
New-Item -ItemType Directory -Force -Path "$PreviewDirectory/Assets", "$PreviewDirectory/ProjectSettings", "$PreviewDirectory/Packages" | Out-Null
Set-Content -LiteralPath "$PreviewDirectory/ProjectSettings/ProjectVersion.txt" -Value "m_EditorVersion: 2022.3.62f3"
Set-Content -LiteralPath "$PreviewDirectory/Packages/manifest.json" -Value '{"dependencies":{"com.unity.ugui":"1.0.0","com.unity.modules.imageconversion":"1.0.0","com.unity.modules.ui":"1.0.0"}}'
# The atlas is pure UnityEngine, so the sheet compiles only it plus a stand-in for the runtime's plate ratio.
Copy-Item -LiteralPath "$repo/modules/Command/Presentation/MapUi/MapSymbolAtlas.cs", "$PSScriptRoot/MapSymbologyUnityCheck.cs" -Destination "$PreviewDirectory/Assets/"
$symbologySource = Get-Content -LiteralPath "$repo/modules/Command/Presentation/MapUi/MapSymbology.cs" -Raw
$ratio = [regex]::Match($symbologySource, 'internal const float PlateRatio = ([0-9.]+f);')
$medium = [regex]::Match($symbologySource, 'internal const float MediumPlate = ([0-9.]+f);')
if (-not $ratio.Success -or -not $medium.Success) { throw 'Production map-symbol sizing constants were not found.' }
Set-Content -LiteralPath "$PreviewDirectory/Assets/MapSymbologyConst.cs" -Value ("namespace BoscaliSummer.Modules.Command.Presentation.MapUi { internal static class MapSymbology { internal const float PlateRatio = " + $ratio.Groups[1].Value + "; internal const float MediumPlate = " + $medium.Groups[1].Value + "; } }")
if (Test-Path -LiteralPath "$PreviewDirectory/result.txt") { Remove-Item -LiteralPath "$PreviewDirectory/result.txt" }
$arguments = @('-batchmode', '-projectPath', ('"' + $PreviewDirectory + '"'), '-executeMethod', 'MapSymbologyUnityCheck.Run', '-logFile', ('"' + "$PreviewDirectory/check.log" + '"'))
$process = Start-Process -FilePath $Unity -ArgumentList $arguments -WorkingDirectory $PreviewDirectory -WindowStyle Hidden -PassThru
$process.WaitForExit()
Write-Output "Results and renders: $PreviewDirectory"
if (Test-Path "$PreviewDirectory/result.txt") { Get-Content "$PreviewDirectory/result.txt" }
else { Get-Content "$PreviewDirectory/check.log" -Tail 60 }
if ($process.ExitCode -ne 0) { throw "Unity symbol sheet failed: $($process.ExitCode)" }

if (-not (Test-Path -LiteralPath "$PreviewDirectory/result.txt") -or (Get-Content -LiteralPath "$PreviewDirectory/result.txt" -Raw) -notmatch '\APASS:') { throw "Unity exited without a successful result: $PreviewDirectory" }
