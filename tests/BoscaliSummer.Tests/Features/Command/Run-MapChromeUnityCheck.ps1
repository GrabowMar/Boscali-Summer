param(
    [string]$Unity = 'C:/Program Files/Unity/Hub/Editor/2022.3.62f3/Editor/Unity.exe',
    [string]$PreviewDirectory = (Join-Path $env:TEMP ('BoscaliMapChromeCheck-' + [guid]::NewGuid().ToString('N'))),
    [string]$ModDll,
    [string]$AtlasStyles,
    [string]$Out,
    [switch]$PrepareOnly
)
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/../../../UnityCheck.Common.ps1"
$PreviewDirectory = [IO.Path]::GetFullPath($PreviewDirectory)
if (-not $ModDll) { $ModDll = Join-Path $repo 'bin/Release/netstandard2.1/BoscaliSummer.dll' }
New-UnityCheckProject $PreviewDirectory '{"dependencies":{"com.unity.ugui":"1.0.0","com.unity.textmeshpro":"3.0.6","com.unity.modules.audio":"1.0.0","com.unity.modules.imageconversion":"1.0.0","com.unity.modules.uielements":"1.0.0","com.unity.modules.physics":"1.0.0","com.unity.modules.animation":"1.0.0","com.unity.modules.ai":"1.0.0","com.unity.modules.particlesystem":"1.0.0","com.unity.modules.assetbundle":"1.0.0","com.unity.modules.imgui":"1.0.0"}}' -Folders 'Assets/Harness/Native', 'NOAvionics'
Copy-GameDlls "$PreviewDirectory/Assets/" $UnityCheckFilter.ManagedWide $UnityCheckFilter.BepInEx
Copy-Item -LiteralPath $ModDll -Destination "$PreviewDirectory/Assets/BoscaliSummer.dll"
Copy-Item -LiteralPath "$PSScriptRoot/MapChromeUnityCheck.cs", $UnityCheckHarness, "$PSScriptRoot/MapChromeUnityCheck.asmdef" -Destination "$PreviewDirectory/Assets/Harness/"
Copy-Item -LiteralPath "$PSScriptRoot/MapChromeNativeShell.cs", "$PSScriptRoot/MapChromeNative.asmdef" -Destination "$PreviewDirectory/Assets/Harness/Native/"
Copy-AvionicsStyles $PreviewDirectory
$fixtureHash = (Get-FileHash -LiteralPath "$PreviewDirectory/Assets/Harness/MapChromeUnityCheck.cs").Hash
[ordered]@{
    assembly_sha256 = (Get-FileHash -LiteralPath "$PreviewDirectory/Assets/BoscaliSummer.dll").Hash
    fixture_sha256 = $fixtureHash
    production_widgets = @('MfdMapDeck', 'MfdMapFooter', 'MfdMapFooter.RectSnapshot')
    limits = 'Real private placement/adopt/snapshot/tick/restore methods; synthetic native surfaces, no public Ensure native discovery adapter. Owned deck overlay converted to world-space only for offscreen screenshot after sorting/scaler assertions. Deferred Destroy unavailable in editor; fixture uses DestroyImmediate only after restoration gates. No native gameplay action, input, mission or scene reload acceptance.'
} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath "$PreviewDirectory/inputs.json"
if ($PrepareOnly) { Write-Output "Prepared: $PreviewDirectory"; return }
Copy-AvionicsStyles $PreviewDirectory $AtlasStyles -NoDefaults
$process = Invoke-UnityCheck $Unity $PreviewDirectory 'MapChromeUnityCheck.Run' -Flags '-batchmode', '--burst-disable-compilation', '-disable-assembly-updater'
Write-Output "Results and renders: $PreviewDirectory"
Show-UnityCheckResult $PreviewDirectory -LogTail 80
if ($Out -and (Test-Path -LiteralPath "$PreviewDirectory/renders")) {
    New-Item -ItemType Directory -Force -Path $Out | Out-Null
    Copy-Item -Path "$PreviewDirectory/renders/*.png" -Destination $Out -Force
    foreach ($name in @('result.txt', 'failures.txt', 'measurements.tsv', 'captures.tsv', 'inputs.json')) {
        if (Test-Path -LiteralPath "$PreviewDirectory/$name") { Copy-Item -LiteralPath "$PreviewDirectory/$name" -Destination $Out -Force }
    }
}
if ((Get-FileHash -LiteralPath "$PSScriptRoot/MapChromeUnityCheck.cs").Hash -ne $fixtureHash) { throw 'MapChrome fixture changed during render; rerun settled source.' }
Assert-UnityResult $PreviewDirectory $process -Pattern '\APASS: MAP CHROME ' -ExitMessage "Unity map chrome check failed: $($process.ExitCode)" -FailMessage "Unity exited without a successful MapChrome result: $PreviewDirectory"
