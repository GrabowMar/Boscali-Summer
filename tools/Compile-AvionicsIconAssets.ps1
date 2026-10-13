param(
    [string]$AssetsDir = (Join-Path $PSScriptRoot '../AvionicsUi/Assets'),
    [string]$Unity = 'C:/Program Files/Unity/Hub/Editor/2022.3.62f3/Editor/Unity.exe',
    [string]$EvidenceDir = ''
)
# Bakes AvionicsUi/Assets/Icons/noa-icons.ttf (from tools/Build-AvIcons.py) into avionics-icons.bundle as the TMP
# font asset "NOA Icons SDF". It ships apart from avionics-ui.bundle so a glyph change never re-bakes the text fonts.
$ErrorActionPreference = 'Stop'
if (-not (Test-Path -LiteralPath $Unity)) { throw "Unity Editor not found: $Unity (62f2 fails in batchmode)" }
$font = Join-Path $AssetsDir 'Icons/noa-icons.ttf'
if (-not (Test-Path -LiteralPath $font)) { throw "Icon font missing: $font (run tools/Build-AvIcons.py first)" }
$icons = (Get-Content -Raw (Join-Path $AssetsDir 'avionics-ui.manifest.json') | ConvertFrom-Json).icons
$codes = @($icons.PSObject.Properties | ForEach-Object { $_.Value }) + '005f'   # '_' is not an icon: TMP wants it in every face
$fixture = if ($EvidenceDir) { [IO.Path]::GetFullPath($EvidenceDir) } else {
    Join-Path $env:TEMP ('BoscaliIconBundle-' + [guid]::NewGuid().ToString('N'))
}
New-Item -ItemType Directory -Force -Path "$fixture/Assets/NOA", "$fixture/Assets/Editor", "$fixture/ProjectSettings", "$fixture/Packages", "$fixture/BundleOutput" | Out-Null
Set-Content -LiteralPath "$fixture/ProjectSettings/ProjectVersion.txt" -Value 'm_EditorVersion: 2022.3.62f3'
Set-Content -LiteralPath "$fixture/Packages/manifest.json" -Value '{"dependencies":{"com.unity.ugui":"1.0.0","com.unity.textmeshpro":"3.0.6","com.unity.modules.assetbundle":"1.0.0","com.unity.modules.imgui":"1.0.0","com.unity.modules.uielements":"1.0.0"}}'
Copy-Item -LiteralPath $font -Destination "$fixture/Assets/NOA/noa-icons.ttf" -Force
Set-Content -LiteralPath "$fixture/Assets/NOA/codes.txt" -Value ($codes -join "`n")

# TMP Essentials (the SDF shader CreateFontAsset needs). AssetDatabase.ImportPackage is deferred in batchmode,
# so unpack the .unitypackage (a tar.gz of <guid>/{asset,asset.meta,pathname}) into Assets first.
$essentials = "$env:LOCALAPPDATA/Unity/cache/packages/packages.unity.com/com.unity.textmeshpro@3.0.6/Package Resources/TMP Essential Resources.unitypackage"
if (-not (Test-Path -LiteralPath $essentials)) { throw "TMP Essential Resources.unitypackage (TMP 3.0.6) not found in the Unity package cache; open any 2022.3 project with TMP 3.0.6 once, then retry" }
$unpack = Join-Path $fixture 'tmp-essentials'
New-Item -ItemType Directory -Force -Path $unpack | Out-Null
tar -xzf "$essentials" -C "$unpack"
if ($LASTEXITCODE -ne 0) { throw "could not unpack $essentials" }
foreach ($entry in Get-ChildItem -LiteralPath $unpack -Directory) {
    $pathFile = Join-Path $entry.FullName 'pathname'
    if (-not (Test-Path -LiteralPath $pathFile)) { continue }
    $dest = Join-Path $fixture (Get-Content -LiteralPath $pathFile -TotalCount 1).Trim()
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

Set-Content -LiteralPath "$fixture/Assets/Editor/BuildIconBundle.cs" -Value @'
using System;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;
public static class BuildIconBundle
{
    public static void Run()
    {
        try
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            if (Shader.Find("TextMeshPro/Distance Field") == null) throw new Exception("TMP SDF shader missing");
            var font = AssetDatabase.LoadAssetAtPath<Font>("Assets/NOA/noa-icons.ttf");
            if (font == null) throw new Exception("Font not imported");
            string chars = new string(File.ReadAllLines("Assets/NOA/codes.txt").Where(l => l.Trim().Length > 0)
                .Select(l => (char)Convert.ToInt32(l.Trim(), 16)).ToArray());
            TMP_FontAsset fa = TMP_FontAsset.CreateFontAsset(font, 88, 6, GlyphRenderMode.SDFAA, 1024, 1024,
                AtlasPopulationMode.Dynamic, false);
            fa.name = "NOA Icons SDF";
            fa.TryAddCharacters(chars, out string miss);
            if (!string.IsNullOrEmpty(miss)) throw new Exception(miss.Length + " glyphs did not fit the atlas");
            fa.atlasPopulationMode = AtlasPopulationMode.Static;
            const string path = "Assets/NOA/NOA Icons SDF.asset";
            AssetDatabase.CreateAsset(fa, path);
            fa.atlasTextures[0].name = "NOA Icons SDF Atlas";
            AssetDatabase.AddObjectToAsset(fa.atlasTextures[0], fa);
            fa.material.name = "NOA Icons SDF Material";
            AssetDatabase.AddObjectToAsset(fa.material, fa);
            AssetDatabase.SaveAssets();
            var build = new AssetBundleBuild { assetBundleName = "avionics-icons.bundle", assetNames = new[] { path } };
            if (BuildPipeline.BuildAssetBundles("BundleOutput", new[] { build },
                    BuildAssetBundleOptions.ChunkBasedCompression, BuildTarget.StandaloneWindows64) == null)
                throw new Exception("AssetBundle compiler returned null");
            File.WriteAllText("build-result.txt", "PASS avionics-icons.bundle: " + chars.Length + " glyphs");
            EditorApplication.Exit(0);
        }
        catch (Exception error)
        {
            Debug.LogException(error);
            File.WriteAllText("build-result.txt", "FAIL " + error);
            EditorApplication.Exit(1);
        }
    }
}
'@
Write-Output "Avionics icon bake: $fixture"
$resultPath = Join-Path $fixture 'build-result.txt'
if (Test-Path -LiteralPath $resultPath) { Remove-Item -LiteralPath $resultPath }
$arguments = @('-batchmode', '-nographics', '-projectPath', ('"' + $fixture + '"'), '-executeMethod', 'BuildIconBundle.Run', '-logFile', ('"' + "$fixture/build.log" + '"'))
$process = Start-Process -FilePath $Unity -ArgumentList $arguments -WorkingDirectory $fixture -WindowStyle Hidden -PassThru
$deadline = [DateTime]::UtcNow.AddMinutes(15)
while (-not $process.WaitForExit(1000)) {
    if ([DateTime]::UtcNow -ge $deadline) { $process.Kill(); $process.WaitForExit(); throw "Avionics icon bake timed out: $fixture/build.log" }
}
if ($process.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $resultPath) -or
    -not (Select-String -LiteralPath $resultPath -Pattern '^PASS ' -Quiet)) {
    if (Test-Path -LiteralPath $resultPath) { Get-Content -LiteralPath $resultPath }
    throw "Avionics icon bake failed: $fixture/build.log"
}
$target = Join-Path $AssetsDir 'avionics-icons.bundle'
Copy-Item -LiteralPath "$fixture/BundleOutput/avionics-icons.bundle" -Destination $target -Force
Get-Content -LiteralPath $resultPath
Write-Output ("Bundle: $target ({0:N0} bytes)" -f (Get-Item $target).Length)
Write-Output "Evidence: $fixture"
