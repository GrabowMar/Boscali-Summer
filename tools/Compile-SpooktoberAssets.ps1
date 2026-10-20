param(
    [string]$SourceDir = (Join-Path $PSScriptRoot '../modules/Spooktober/Assets/Source'),
    [string]$OutputDir = (Join-Path $PSScriptRoot '../modules/Spooktober/Assets'),
    [string]$Unity = 'C:/Program Files/Unity/Hub/Editor/2022.3.62f3/Editor/Unity.exe',
    [string]$EvidenceDir = ''
)
# Compiles the Spooktober rad-ghost shader (RadGhost.shader) into spooktober.bundle.
# Plain CG, no pipeline includes; same recipe as Compile-NightVisionAssets.ps1.
$ErrorActionPreference = 'Stop'
if (-not (Test-Path -LiteralPath $Unity)) { throw "Unity Editor not found: $Unity" }
$fixture = if ($EvidenceDir) { [IO.Path]::GetFullPath($EvidenceDir) } else {
    Join-Path $env:TEMP ('BoscaliSpookBundle-' + [guid]::NewGuid().ToString('N'))
}
New-Item -ItemType Directory -Force -Path "$fixture/Assets/Spook", "$fixture/Assets/Editor", "$fixture/ProjectSettings", "$fixture/Packages", "$fixture/BundleOutput" | Out-Null
Set-Content -LiteralPath "$fixture/ProjectSettings/ProjectVersion.txt" -Value 'm_EditorVersion: 2022.3.62f3'
Set-Content -LiteralPath "$fixture/Packages/manifest.json" -Value '{"dependencies":{"com.unity.modules.assetbundle":"1.0.0"}}'
foreach ($name in @('RadGhost.shader')) {
    $source = Join-Path $SourceDir $name
    if (-not (Test-Path -LiteralPath $source)) { throw "Required shader source missing: $source" }
    Copy-Item -LiteralPath $source -Destination "$fixture/Assets/Spook/$name" -Force
}
Set-Content -LiteralPath "$fixture/Assets/Editor/BuildSpookBundle.cs" -Value @'
using System;
using System.IO;
using UnityEngine;
using UnityEditor;
public static class BuildSpookBundle
{
    public static void Run()
    {
        try
        {
            string[] paths = { "Assets/Spook/RadGhost.shader" };
            string[] names = { "Boscali/RadGhost" };
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            for (int i = 0; i < paths.Length; i++)
            {
                Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(paths[i]);
                if (shader == null || ShaderUtil.ShaderHasError(shader))
                {
                    string detail = "";
                    if (shader != null) foreach (var message in ShaderUtil.GetShaderMessages(shader))
                        detail += "\n" + message.severity + " " + message.file + ":" + message.line + " " + message.message;
                    throw new Exception("Shader import failed: " + paths[i] + detail);
                }
                if (shader.name != names[i]) throw new Exception("Unexpected shader name: " + shader.name);
                if (!shader.isSupported) throw new Exception(names[i] + " is not supported on the build machine");
            }
            // The runtime sets these per target (RadGhostLook.Profile); a rename here would silently do nothing.
            var probe = new Material(AssetDatabase.LoadAssetAtPath<Shader>(paths[0]));
            foreach (string property in new[] { "_Intensity", "_BodyAlpha", "_RimPower", "_NoiseScale", "_ScrollSpeed",
                "_ScanDensity", "_ScanStrength", "_ShimmerScale", "_FarDistance", "_Strength" })
                if (!probe.HasProperty(property)) throw new Exception("RadGhost property missing: " + property);
            if (probe.renderQueue != 3010) throw new Exception("RadGhost queue is " + probe.renderQueue + ", expected Transparent+10");
            UnityEngine.Object.DestroyImmediate(probe);
            var build = new AssetBundleBuild { assetBundleName = "spooktober.bundle", assetNames = paths };
            var manifest = BuildPipeline.BuildAssetBundles("BundleOutput", new[] { build },
                BuildAssetBundleOptions.ChunkBasedCompression, BuildTarget.StandaloneWindows64);
            if (manifest == null) throw new Exception("AssetBundle compiler returned null");
            foreach (string path in paths)
                if (ShaderUtil.ShaderHasError(AssetDatabase.LoadAssetAtPath<Shader>(path))) throw new Exception("Shader compile failed: " + path);
            if (new FileInfo("BundleOutput/spooktober.bundle").Length > 512 * 1024)
                throw new Exception("spooktober.bundle exceeds its 512 KiB loader limit");
            File.WriteAllText("build-result.txt", "PASS spooktober.bundle: Boscali/RadGhost");
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
Write-Output "Spooktober shader compiler: $fixture"
$resultPath = Join-Path $fixture 'build-result.txt'
if (Test-Path -LiteralPath $resultPath) { Remove-Item -LiteralPath $resultPath }
$arguments = @('-batchmode', '-projectPath', ('"' + $fixture + '"'), '-executeMethod', 'BuildSpookBundle.Run', '-logFile', ('"' + "$fixture/build.log" + '"'))
$process = Start-Process -FilePath $Unity -ArgumentList $arguments -WorkingDirectory $fixture -WindowStyle Hidden -PassThru
$deadline = [DateTime]::UtcNow.AddMinutes(10)
while (-not $process.WaitForExit(1000)) {
    if ([DateTime]::UtcNow -ge $deadline) { $process.Kill(); $process.WaitForExit(); throw "Spooktober shader compiler timed out: $fixture/build.log" }
}
if ($process.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $resultPath) -or
    -not (Select-String -LiteralPath $resultPath -Pattern '^PASS ' -Quiet)) {
    if (Test-Path -LiteralPath $resultPath) { Get-Content -LiteralPath $resultPath }
    throw "Spooktober shader compilation failed: $fixture/build.log"
}
New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null
$target = Join-Path $OutputDir 'spooktober.bundle'
Copy-Item -LiteralPath "$fixture/BundleOutput/spooktober.bundle" -Destination $target -Force
Get-Content -LiteralPath $resultPath
Write-Output "Bundle: $target"
Write-Output "Evidence: $fixture"
