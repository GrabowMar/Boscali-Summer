param(
    [string]$Unity = "C:/Program Files/Unity/Hub/Editor/2022.3.62f3/Editor/Unity.exe",
    [string]$PreviewDirectory = (Join-Path $env:TEMP ("BoscaliWingRadioCheck-" + [guid]::NewGuid().ToString("N"))),
    [string]$ProductionDll,
    [string]$AtlasStyles,
    [switch]$RadioOnly
)
$ErrorActionPreference = "Stop"
function Get-PreviewHash([string]$Path) {
    $stream = [IO.File]::OpenRead($Path)
    $sha = [Security.Cryptography.SHA256]::Create()
    try { ([BitConverter]::ToString($sha.ComputeHash($stream))).Replace('-', '') }
    finally { $stream.Dispose(); $sha.Dispose() }
}
$repo = (Resolve-Path (Join-Path $PSScriptRoot "../../../..")).Path
$gameManaged = 'C:/Program Files (x86)/Steam/steamapps/common/Nuclear Option/NuclearOption_Data/Managed'
New-Item -ItemType Directory -Force -Path "$PreviewDirectory/Assets/Harness", "$PreviewDirectory/ProjectSettings", "$PreviewDirectory/Packages", "$PreviewDirectory/NOAvionics" | Out-Null
if ($RadioOnly) {
    # Old nomod Radio projects compiled copied presenters/kit sources. Keep those
    # generated references outside Assets so this fixture resolves production DLL types.
    $previewResolved = (Resolve-Path -LiteralPath $PreviewDirectory).Path
    $previewPrefix = $previewResolved.TrimEnd([char[]]'\/') + [IO.Path]::DirectorySeparatorChar
    $assetResolved = (Resolve-Path -LiteralPath "$PreviewDirectory/Assets").Path
    if (-not $assetResolved.StartsWith($previewPrefix, [StringComparison]::OrdinalIgnoreCase)) { throw 'Preview Assets escaped the isolated project' }
    $legacySources = @(Get-ChildItem -LiteralPath $assetResolved -File -Filter '*.cs')
    foreach ($legacySource in $legacySources) {
        if ($legacySource.Name -notmatch '^(Av.+|AtlasHarness|BezelRegistry|MapPicker|PngIconHeader|PresenceBoard|Radio.+|TheaterScoring)\.cs$') {
            throw "Unattributed source in production DLL preview: $($legacySource.FullName)"
        }
    }
    if ($legacySources.Count -gt 0) {
        $referenceDirectory = [IO.Path]::GetFullPath((Join-Path $previewResolved ('legacy-source-reference/' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss-fff'))))
        if (-not $referenceDirectory.StartsWith($previewPrefix, [StringComparison]::OrdinalIgnoreCase)) { throw 'Legacy source archive escaped the isolated project' }
        New-Item -ItemType Directory -Path $referenceDirectory -Force | Out-Null
        $legacySources | ForEach-Object { [pscustomobject]@{ Path=$_.FullName; SHA256=Get-PreviewHash $_.FullName } } |
            Export-Csv -LiteralPath "$referenceDirectory/source-attribution.csv" -NoTypeInformation
        foreach ($legacySource in $legacySources) {
            Move-Item -LiteralPath $legacySource.FullName -Destination (Join-Path $referenceDirectory $legacySource.Name)
            if (Test-Path -LiteralPath ($legacySource.FullName + '.meta')) {
                Move-Item -LiteralPath ($legacySource.FullName + '.meta') -Destination (Join-Path $referenceDirectory ($legacySource.Name + '.meta'))
            }
        }
        Write-Output "Archived $($legacySources.Count) generated preview sources: $referenceDirectory"
    }
}
$keepFrozenStyles = $AtlasStyles -and [IO.Path]::GetFullPath($AtlasStyles) -eq [IO.Path]::GetFullPath($PreviewDirectory)
if (-not $keepFrozenStyles) {
    Get-ChildItem -LiteralPath "$repo/AvionicsUi" -Filter 'avionics.*.avss' | Copy-Item -Destination "$PreviewDirectory/NOAvionics/"
    if ($AtlasStyles) { Get-ChildItem -LiteralPath (Join-Path $AtlasStyles 'NOAvionics') -Filter '*.avss' | Copy-Item -Destination "$PreviewDirectory/NOAvionics/" -Force }
}
Set-Content -LiteralPath "$PreviewDirectory/ProjectSettings/ProjectVersion.txt" -Value 'm_EditorVersion: 2022.3.62f3'
Set-Content -LiteralPath "$PreviewDirectory/Packages/manifest.json" -Value '{"dependencies":{"com.unity.ugui":"1.0.0","com.unity.textmeshpro":"3.0.6","com.unity.modules.audio":"1.0.0","com.unity.modules.imageconversion":"1.0.0","com.unity.modules.uielements":"1.0.0","com.unity.modules.physics":"1.0.0","com.unity.modules.animation":"1.0.0","com.unity.modules.ai":"1.0.0","com.unity.modules.particlesystem":"1.0.0","com.unity.modules.assetbundle":"1.0.0","com.unity.modules.imgui":"1.0.0"}}'
Get-ChildItem -LiteralPath $gameManaged -Filter '*.dll' | Where-Object {
    $_.Name -notmatch '^(System|Mono|Microsoft|UnityEngine|UnityEditor|mscorlib|netstandard|Unity.TextMeshPro|Accessibility|Novell)'
} | Copy-Item -Destination "$PreviewDirectory/Assets/"
Get-ChildItem -LiteralPath 'C:/Program Files (x86)/Steam/steamapps/common/Nuclear Option/BepInEx/core' -Filter '*.dll' | Where-Object { $_.Name -match '^(BepInEx.dll$|Mono|0Harmony.dll$|HarmonyXInterop)' } | Copy-Item -Destination "$PreviewDirectory/Assets/"
if (-not $ProductionDll) { $ProductionDll = "$repo/bin/Release/netstandard2.1/BoscaliSummer.dll" }
if ((Resolve-Path -LiteralPath $ProductionDll).Path -ne [IO.Path]::GetFullPath("$PreviewDirectory/Assets/BoscaliSummer.dll")) {
    Copy-Item -LiteralPath $ProductionDll -Destination "$PreviewDirectory/Assets/BoscaliSummer.dll"
}
Copy-Item -LiteralPath "$PSScriptRoot/WingRadioUnityCheck.cs", "$PSScriptRoot/BoscaliWingRadioPreview.asmdef" -Destination "$PreviewDirectory/Assets/Harness/"
Set-Content -LiteralPath "$PreviewDirectory/production-dll.sha256" -Value (Get-PreviewHash "$PreviewDirectory/Assets/BoscaliSummer.dll")
Set-Content -LiteralPath "$PreviewDirectory/preview-mode.txt" -Value $(if ($RadioOnly) { 'RadioOnly' } else { 'WingRadioHistorical' })
@((Get-Item -LiteralPath "$PreviewDirectory/Assets/BoscaliSummer.dll"), (Get-Item -LiteralPath "$PreviewDirectory/preview-mode.txt"), (Get-ChildItem -LiteralPath "$PreviewDirectory/NOAvionics" -Filter '*.avss'), (Get-ChildItem -LiteralPath "$PreviewDirectory/Assets/Harness" -File)) |
    ForEach-Object { $_ } | ForEach-Object { [pscustomobject]@{ Hash = Get-PreviewHash $_.FullName; Path = $_.FullName } } |
    Export-Csv -LiteralPath "$PreviewDirectory/input-manifest.csv" -NoTypeInformation
$executeMethod = if ($RadioOnly) { 'WingRadioUnityCheck.RunRadioOnly' } else { 'WingRadioUnityCheck.Run' }
$arguments = @('-batchmode', '-disable-assembly-updater', '-projectPath', ('"' + $PreviewDirectory + '"'), '-executeMethod', $executeMethod, '-logFile', ('"' + "$PreviewDirectory/check.log" + '"'))
if (Test-Path -LiteralPath "$PreviewDirectory/result.txt") { Remove-Item -LiteralPath "$PreviewDirectory/result.txt" }
$process = Start-Process -FilePath $Unity -ArgumentList $arguments -WorkingDirectory $PreviewDirectory -WindowStyle Hidden -PassThru
$process.WaitForExit()
Write-Output "Results and renders: $PreviewDirectory"
if (Test-Path "$PreviewDirectory/result.txt") { Get-Content "$PreviewDirectory/result.txt" }
else { Get-Content "$PreviewDirectory/check.log" -Tail 80 }
if ($process.ExitCode -ne 0) { throw "Unity Wing/Radio check failed: $($process.ExitCode)" }
if (-not (Test-Path -LiteralPath "$PreviewDirectory/result.txt") -or (Get-Content -LiteralPath "$PreviewDirectory/result.txt" -Raw) -notmatch '\APASS:') { throw "Unity exited without a successful result: $PreviewDirectory" }
