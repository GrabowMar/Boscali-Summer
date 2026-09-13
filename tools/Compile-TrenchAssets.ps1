param(
    [Parameter(Mandatory=$true)]
    [string]$SourceDir,
    [string]$OutputDir = (Join-Path $PSScriptRoot "../modules/Trenches/Assets"),
    [string]$BundleName = "trenches.bundle",
    [string]$Unity = "C:/Program Files/Unity/Hub/Editor/2022.3.62f3/Editor/Unity.exe"
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path $Unity)) {
    throw "Unity Editor not found at: $Unity"
}

if (-not (Test-Path $SourceDir)) {
    throw "Source directory not found at: $SourceDir"
}

$tempProject = Join-Path $env:TEMP ("TrenchBundleBuild-" + [guid]::NewGuid().ToString("N"))
Write-Host "Creating temporary Unity project at: $tempProject"

New-Item -ItemType Directory -Force -Path "$tempProject/Assets/TrenchModels", "$tempProject/Assets/Editor", "$tempProject/ProjectSettings", "$tempProject/Packages", "$tempProject/BundleOutput" | Out-Null
Set-Content -LiteralPath "$tempProject/ProjectSettings/ProjectVersion.txt" -Value "m_EditorVersion: 2022.3.62f3"
Set-Content -LiteralPath "$tempProject/Packages/manifest.json" -Value '{"dependencies":{"com.unity.modules.assetbundle":"1.0.0","com.unity.modules.imageconversion":"1.0.0","com.unity.modules.physics":"1.0.0"}}'

# Copy source assets into project
Copy-Item -Path "$SourceDir/*" -Destination "$tempProject/Assets/TrenchModels/" -Recurse -Force

# Create editor build script
$buildScript = @'
using System;
using System.IO;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static class BuildTrenchBundle
{
    public static void Run()
    {
        try
        {
            string outDir = "BundleOutput";
            if (!Directory.Exists(outDir)) Directory.CreateDirectory(outDir);

            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            string[] guids = AssetDatabase.FindAssets("", new[] { "Assets/TrenchModels" });
            var assetPaths = new List<string>();
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!Directory.Exists(path) && !path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                {
                    assetPaths.Add(path);
                    Debug.Log("Bundling asset: " + path);
                }
            }

            if (assetPaths.Count == 0)
            {
                throw new Exception("No assets found in Assets/TrenchModels to bundle!");
            }

            var build = new AssetBundleBuild
            {
                assetBundleName = "trenches.bundle",
                assetNames = assetPaths.ToArray()
            };

            var manifest = BuildPipeline.BuildAssetBundles(outDir, new[] { build },
                BuildAssetBundleOptions.ChunkBasedCompression, BuildTarget.StandaloneWindows64);

            if (manifest == null)
            {
                throw new Exception("BuildPipeline.BuildAssetBundles returned null!");
            }

            File.WriteAllText("build_result.txt", "SUCCESS");
            EditorApplication.Exit(0);
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
            File.WriteAllText("build_result.txt", "FAILED: " + ex);
            EditorApplication.Exit(1);
        }
    }
}
'@

Set-Content -LiteralPath "$tempProject/Assets/Editor/BuildTrenchBundle.cs" -Value $buildScript

Write-Host "Running Unity batchmode AssetBundle compiler..."
$arguments = @("-batchmode", "-projectPath", $tempProject, "-executeMethod", "BuildTrenchBundle.Run", "-logFile", "$tempProject/build.log")
$process = Start-Process -FilePath $Unity -ArgumentList $arguments -WorkingDirectory $tempProject -WindowStyle Hidden -Wait -PassThru

if (Test-Path "$tempProject/build_result.txt") {
    $res = Get-Content "$tempProject/build_result.txt"
    Write-Host "Build Result: $res"
}

$builtBundle = Join-Path $tempProject "BundleOutput/trenches.bundle"
if (Test-Path $builtBundle) {
    if (-not (Test-Path $OutputDir)) {
        New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null
    }
    $targetFile = Join-Path $OutputDir $BundleName
    Copy-Item -LiteralPath $builtBundle -Destination $targetFile -Force
    Write-Host "Successfully generated AssetBundle at: $targetFile"
} else {
    Write-Error "AssetBundle output not found. Unity log tail:"
    if (Test-Path "$tempProject/build.log") {
        Get-Content "$tempProject/build.log" -Tail 50
    }
}

# Cleanup
Remove-Item -Recurse -Force -LiteralPath $tempProject -ErrorAction SilentlyContinue
