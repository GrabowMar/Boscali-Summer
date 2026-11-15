param(
    [string]$Unity = 'C:/Program Files/Unity/Hub/Editor/2022.3.62f3/Editor/Unity.exe',
    [string]$ProjectDirectory = '',
    [string]$OutputDirectory = ''
)
$ErrorActionPreference = 'Stop'
$repoDirectory = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (-not $ProjectDirectory) { $ProjectDirectory = Join-Path $repoDirectory '.nomodkit/sky-assets' }
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $repoDirectory 'modules/Weather/Assets' }
$ProjectDirectory = [IO.Path]::GetFullPath($ProjectDirectory)
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
if (-not (Test-Path -LiteralPath $Unity -PathType Leaf)) { throw "Unity Editor not found: $Unity" }
foreach ($folder in 'Assets/Sky', 'Assets/Editor', 'Packages', 'ProjectSettings', 'BundleOutput') {
    New-Item -ItemType Directory -Force -Path (Join-Path $ProjectDirectory $folder) | Out-Null
}
Set-Content -LiteralPath "$ProjectDirectory/ProjectSettings/ProjectVersion.txt" -Value 'm_EditorVersion: 2022.3.62f3'
Set-Content -LiteralPath "$ProjectDirectory/Packages/manifest.json" -Value '{"dependencies":{"com.unity.modules.assetbundle":"1.0.0"}}'
Copy-Item -LiteralPath "$repoDirectory/modules/Weather/Assets/Source/AtmosphereSky.shader" -Destination "$ProjectDirectory/Assets/Sky/AtmosphereSky.shader" -Force
Set-Content -LiteralPath "$ProjectDirectory/Assets/Editor/BuildSkyAssets.cs" -Value @'
using System;
using System.IO;
using UnityEditor;
using UnityEngine;
public static class BuildSkyAssets
{
    public static void Run()
    {
        try
        {
            const string source = "Assets/Sky/AtmosphereSky.shader";
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(source);
            if (shader == null || ShaderUtil.ShaderHasError(shader)) throw new Exception("Atmosphere shader failed to compile.");
            var build = new AssetBundleBuild { assetBundleName = "sky.bundle", assetNames = new[] { source } };
            if (BuildPipeline.BuildAssetBundles("BundleOutput", new[] { build }, BuildAssetBundleOptions.ChunkBasedCompression,
                BuildTarget.StandaloneWindows64) == null || ShaderUtil.ShaderHasError(shader)) throw new Exception("Sky bundle build failed.");
            File.WriteAllText("result.txt", "PASS: production sky shader compiled and bundled.");
            EditorApplication.Exit(0);
        }
        catch (Exception error)
        {
            Debug.LogException(error);
            File.WriteAllText("result.txt", "FAIL: " + error);
            EditorApplication.Exit(1);
        }
    }
}
'@
$resultPath = Join-Path $ProjectDirectory 'result.txt'
if (Test-Path -LiteralPath $resultPath) { Remove-Item -LiteralPath $resultPath }
$arguments = @('-batchmode', '-force-d3d11', '-projectPath', $ProjectDirectory, '-executeMethod', 'BuildSkyAssets.Run', '-logFile', "$ProjectDirectory/build.log")
& $Unity @arguments | Out-Host
if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $resultPath) -or
    (Get-Content -LiteralPath $resultPath -Raw) -notmatch '\APASS:') { throw "Sky build failed: $ProjectDirectory/build.log" }
$bundlePath = Join-Path $ProjectDirectory 'BundleOutput/sky.bundle'
if (-not (Test-Path -LiteralPath $bundlePath) -or (Get-Item -LiteralPath $bundlePath).Length -gt 512KB) { throw 'Sky bundle missing or exceeds 512 KiB.' }
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
Copy-Item -LiteralPath $bundlePath -Destination (Join-Path $OutputDirectory 'sky.bundle') -Force
Get-Content -LiteralPath $resultPath
Write-Output "Sky bundle: $(Join-Path $OutputDirectory 'sky.bundle')"
