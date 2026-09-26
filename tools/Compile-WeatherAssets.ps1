param(
    [string]$SourceDir = (Join-Path $PSScriptRoot "../modules/Weather/Assets/Source"),
    [string]$OutputDir = (Join-Path $PSScriptRoot "../modules/Weather/Assets"),
    [string]$BundleName = "weatherrain.bundle",
    [string]$Unity = $(
        if (Test-Path "C:/Program Files/Unity/Hub/Editor/2022.3.62f3/Editor/Unity.exe") {
            "C:/Program Files/Unity/Hub/Editor/2022.3.62f3/Editor/Unity.exe"
        } else {
            "C:/Program Files/Unity/Hub/Editor/2022.3.62f2/Editor/Unity.exe"
        }
    )
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path $Unity)) {
    throw "Unity Editor not found at: $Unity"
}

if (-not (Test-Path $SourceDir)) {
    throw "Source directory not found at: $SourceDir"
}

$tempProject = Join-Path $env:TEMP ("WeatherBundleBuild-" + [guid]::NewGuid().ToString("N"))
Write-Host "Creating temporary Unity project at: $tempProject"

New-Item -ItemType Directory -Force -Path "$tempProject/Assets/WeatherShader", "$tempProject/Assets/Editor", "$tempProject/ProjectSettings", "$tempProject/Packages", "$tempProject/BundleOutput" | Out-Null
$editorVer = if ($Unity -like "*62f3*") { "2022.3.62f3" } else { "2022.3.62f2" }
Set-Content -LiteralPath "$tempProject/ProjectSettings/ProjectVersion.txt" -Value "m_EditorVersion: $editorVer"
Set-Content -LiteralPath "$tempProject/Packages/manifest.json" -Value '{"dependencies":{"com.unity.modules.assetbundle":"1.0.0"}}'

# Copy shader sources into project
Copy-Item -Path "$SourceDir/*" -Destination "$tempProject/Assets/WeatherShader/" -Recurse -Force

# Create editor build script
$buildScript = @'
using System;
using System.IO;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static class BuildWeatherBundle
{
    public static void Run()
    {
        try
        {
            string outDir = "BundleOutput";
            if (!Directory.Exists(outDir)) Directory.CreateDirectory(outDir);

            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            string[] guids = AssetDatabase.FindAssets("", new[] { "Assets/WeatherShader" });
            var assetPaths = new List<string>();
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (Directory.Exists(path)) continue;
                if (!path.EndsWith(".shader", StringComparison.OrdinalIgnoreCase) &&
                    !path.EndsWith(".tif", StringComparison.OrdinalIgnoreCase) &&
                    !path.EndsWith(".tiff", StringComparison.OrdinalIgnoreCase) &&
                    !path.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) continue;
                assetPaths.Add(path);
                Debug.Log("Bundling asset: " + path);
            }

            if (assetPaths.Count == 0)
            {
                throw new Exception("No assets found in Assets/WeatherShader to bundle!");
            }

            var build = new AssetBundleBuild
            {
                assetBundleName = "weatherrain.bundle",
                assetNames = assetPaths.ToArray()
            };

            var manifest = BuildPipeline.BuildAssetBundles(outDir, new[] { build },
                BuildAssetBundleOptions.ChunkBasedCompression, BuildTarget.StandaloneWindows64);

            if (manifest == null)
            {
                throw new Exception("BuildPipeline.BuildAssetBundles returned null!");
            }

            foreach (string path in assetPaths)
            {
                Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(path);
                if (shader != null && ShaderUtil.ShaderHasError(shader))
                    throw new Exception("Shader compilation failed: " + path);
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

Set-Content -LiteralPath "$tempProject/Assets/Editor/BuildWeatherBundle.cs" -Value $buildScript

Write-Host "Running Unity batchmode AssetBundle compiler..."
$arguments = @("-batchmode", "-projectPath", $tempProject, "-executeMethod", "BuildWeatherBundle.Run", "-logFile", "$tempProject/build.log")
$process = Start-Process -FilePath $Unity -ArgumentList $arguments -WorkingDirectory $tempProject -WindowStyle Hidden -Wait -PassThru

if ($process.ExitCode -ne 0 -or -not (Test-Path "$tempProject/build_result.txt") -or
    (Get-Content "$tempProject/build_result.txt" -Raw).Trim() -ne 'SUCCESS') {
    throw "Unity bundle build failed; inspect $tempProject/build.log"
}

if (Test-Path "$tempProject/build_result.txt") {
    $res = Get-Content "$tempProject/build_result.txt"
    Write-Host "Build Result: $res"
}

$builtBundle = Join-Path $tempProject "BundleOutput/weatherrain.bundle"
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

# Keep the compiler log and exact temporary project available for diagnosis.
Write-Host "Compiler evidence: $tempProject/build.log"
