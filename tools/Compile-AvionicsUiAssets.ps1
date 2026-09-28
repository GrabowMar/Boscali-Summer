param(
    [string]$Unity = 'C:/Program Files/Unity/Hub/Editor/2022.3.62f3/Editor/Unity.exe',
    [string]$OutputDir = (Join-Path $PSScriptRoot '../AvionicsUi/Assets')
)
$ErrorActionPreference = 'Stop'
if (-not (Test-Path $Unity)) { throw "Unity 2022.3.62f3 not found at $Unity (62f2 fails in batchmode)" }
$src = Join-Path $PSScriptRoot 'AvionicsUiAssets'
$proj = Join-Path $env:TEMP ('AvionicsUiBundle-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path "$proj/Assets/NOA", "$proj/Assets/Editor", "$proj/ProjectSettings", "$proj/Packages" | Out-Null
Set-Content -LiteralPath "$proj/ProjectSettings/ProjectVersion.txt" -Value 'm_EditorVersion: 2022.3.62f3'
Set-Content -LiteralPath "$proj/Packages/manifest.json" -Value '{"dependencies":{"com.unity.ugui":"1.0.0","com.unity.textmeshpro":"3.0.6","com.unity.modules.assetbundle":"1.0.0","com.unity.modules.imgui":"1.0.0","com.unity.modules.uielements":"1.0.0","com.unity.modules.imageconversion":"1.0.0"}}'
Copy-Item -Recurse -Force "$src/Fonts", "$src/Shaders" -Destination "$proj/Assets/NOA/"
Copy-Item -Force "$src/icons.txt", "$src/charsets.txt" -Destination "$proj/Assets/NOA/"
Copy-Item -Force "$src/Editor/BuildAvionicsUiBundle.cs" -Destination "$proj/Assets/Editor/"

# TMP Essentials (the SDF shaders CreateFontAsset needs). AssetDatabase.ImportPackage is deferred in
# batchmode, so unpack the .unitypackage (a tar.gz of <guid>/{asset,asset.meta,pathname}) into Assets first.
$essentials = @(
    "$env:LOCALAPPDATA/Unity/cache/packages/packages.unity.com/com.unity.textmeshpro@3.0.6/Package Resources/TMP Essential Resources.unitypackage"
) | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (-not $essentials) { throw "TMP Essential Resources.unitypackage (TMP 3.0.6) not found in the Unity package cache; open any 2022.3 project with TMP 3.0.6 once, then retry" }
$unpack = Join-Path $proj 'tmp-essentials'
New-Item -ItemType Directory -Force -Path $unpack | Out-Null
tar -xzf "$essentials" -C "$unpack"
if ($LASTEXITCODE -ne 0) { throw "could not unpack $essentials" }
foreach ($entry in Get-ChildItem -LiteralPath $unpack -Directory) {
    $pathFile = Join-Path $entry.FullName 'pathname'
    if (-not (Test-Path -LiteralPath $pathFile)) { continue }
    $rel = (Get-Content -LiteralPath $pathFile -TotalCount 1).Trim()
    $dest = Join-Path $proj $rel
    $asset = Join-Path $entry.FullName 'asset'
    if (Test-Path -LiteralPath $asset) {
        New-Item -ItemType Directory -Force -Path (Split-Path $dest) | Out-Null
        Copy-Item -LiteralPath $asset -Destination $dest -Force
    } else {
        New-Item -ItemType Directory -Force -Path $dest | Out-Null
    }
    $meta = Join-Path $entry.FullName 'asset.meta'
    if (Test-Path -LiteralPath $meta) { Copy-Item -LiteralPath $meta -Destination "$dest.meta" -Force }
}
Remove-Item -Recurse -Force -LiteralPath $unpack
Write-Host "Baking in $proj ..."
$p = Start-Process -FilePath $Unity -ArgumentList @('-batchmode', '-nographics', '-projectPath', "`"$proj`"", '-executeMethod', 'BuildAvionicsUiBundle.Run', '-logFile', "`"$proj/build.log`"") -WorkingDirectory $proj -WindowStyle Hidden -PassThru
# WaitForExit, not Start-Process -Wait: -Wait also waits for Unity's licensing/crash-handler children,
# which can outlive the editor and hang the script.
$p.WaitForExit()
$result = if (Test-Path "$proj/build_result.txt") { (Get-Content "$proj/build_result.txt" -Raw).Trim() } else { 'NO RESULT' }
if ($p.ExitCode -ne 0 -or $result -ne 'SUCCESS') { Get-Content "$proj/build.log" -Tail 60; throw "Avionics UI bake failed ($result); see $proj/build.log" }
New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null
Copy-Item -Force "$proj/BundleOutput/avionics-ui.bundle", "$proj/BundleOutput/avionics-ui.manifest.json" -Destination $OutputDir
$size = (Get-Item "$OutputDir/avionics-ui.bundle").Length
Write-Host ("avionics-ui.bundle = {0:N0} bytes" -f $size)
if ($size -gt 4MB) { throw "Bundle exceeds the 4 MB budget: $size bytes" }
Write-Host "Evidence: $proj/build.log"
