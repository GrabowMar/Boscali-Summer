param(
    [string]$Unity = "C:/Program Files/Unity/Hub/Editor/2022.3.62f3/Editor/Unity.exe",
    [string]$PreviewDirectory = (Join-Path $env:TEMP ("BoscaliWingRadioCheck-" + [guid]::NewGuid().ToString("N"))),
    [string]$ProductionDll,
    [string]$AtlasStyles,
    [switch]$RadioOnly
)
$ErrorActionPreference = "Stop"
. "$PSScriptRoot/../../../UnityCheck.Common.ps1"
New-UnityCheckProject $PreviewDirectory $UnityCheckManifest.MfdFull -Folders 'Assets/Harness', 'NOAvionics'
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
        $legacySources | ForEach-Object { [pscustomobject]@{ Path=$_.FullName; SHA256=(Get-FileHash -LiteralPath $_.FullName).Hash } } |
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
if (-not $keepFrozenStyles) { Copy-AvionicsStyles $PreviewDirectory $AtlasStyles }
Copy-GameDlls "$PreviewDirectory/Assets/" $UnityCheckFilter.ManagedWide $UnityCheckFilter.BepInEx
if (-not $ProductionDll) { $ProductionDll = "$repo/bin/Release/netstandard2.1/BoscaliSummer.dll" }
if ((Resolve-Path -LiteralPath $ProductionDll).Path -ne [IO.Path]::GetFullPath("$PreviewDirectory/Assets/BoscaliSummer.dll")) {
    Copy-Item -LiteralPath $ProductionDll -Destination "$PreviewDirectory/Assets/BoscaliSummer.dll"
}
Copy-Item -LiteralPath "$PSScriptRoot/WingRadioUnityCheck.cs", $UnityCheckHarness, "$PSScriptRoot/BoscaliWingRadioPreview.asmdef" -Destination "$PreviewDirectory/Assets/Harness/"
Set-Content -LiteralPath "$PreviewDirectory/production-dll.sha256" -Value (Get-FileHash -LiteralPath "$PreviewDirectory/Assets/BoscaliSummer.dll").Hash
Set-Content -LiteralPath "$PreviewDirectory/preview-mode.txt" -Value $(if ($RadioOnly) { 'RadioOnly' } else { 'WingRadioHistorical' })
Export-UnityCheckHashes "$PreviewDirectory/input-manifest.csv" (@((Get-Item -LiteralPath "$PreviewDirectory/Assets/BoscaliSummer.dll"), (Get-Item -LiteralPath "$PreviewDirectory/preview-mode.txt"), (Get-ChildItem -LiteralPath "$PreviewDirectory/NOAvionics" -Filter '*.avss'), (Get-ChildItem -LiteralPath "$PreviewDirectory/Assets/Harness" -File)) | ForEach-Object { $_ })
$executeMethod = if ($RadioOnly) { 'WingRadioUnityCheck.RunRadioOnly' } else { 'WingRadioUnityCheck.Run' }
$process = Invoke-UnityCheck $Unity $PreviewDirectory $executeMethod
Write-Output "Results and renders: $PreviewDirectory"
Show-UnityCheckResult $PreviewDirectory -LogTail 80
Assert-UnityResult $PreviewDirectory $process -ExitMessage "Unity Wing/Radio check failed: $($process.ExitCode)"
