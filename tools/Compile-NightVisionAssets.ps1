param(
    [string]$SourceDir = (Join-Path $PSScriptRoot '../modules/QoL/Assets/Source'),
    [string]$OutputDir = (Join-Path $PSScriptRoot '../modules/QoL/Assets'),
    [string]$Unity = 'C:/Program Files/Unity/Hub/Editor/2022.3.62f3/Editor/Unity.exe',
    [string]$EvidenceDir = ''
)
# Compiles the night-vision shaders (NvgPhosphor.shader, NvgHeat.shader) into nightvision.bundle.
# Plain CG, no pipeline includes; same recipe as Compile-AvionicsFxAssets.ps1.
$ErrorActionPreference = 'Stop'
if (-not (Test-Path -LiteralPath $Unity)) { throw "Unity Editor not found: $Unity" }
$fixture = if ($EvidenceDir) { [IO.Path]::GetFullPath($EvidenceDir) } else {
    Join-Path $env:TEMP ('BoscaliNvgBundle-' + [guid]::NewGuid().ToString('N'))
}
New-Item -ItemType Directory -Force -Path "$fixture/Assets/Nvg", "$fixture/Assets/Editor", "$fixture/ProjectSettings", "$fixture/Packages", "$fixture/BundleOutput" | Out-Null
Set-Content -LiteralPath "$fixture/ProjectSettings/ProjectVersion.txt" -Value 'm_EditorVersion: 2022.3.62f3'
Set-Content -LiteralPath "$fixture/Packages/manifest.json" -Value '{"dependencies":{"com.unity.modules.assetbundle":"1.0.0"}}'
foreach ($name in 'NvgPhosphor.shader', 'NvgHeat.shader') {
    $source = Join-Path $SourceDir $name
    if (-not (Test-Path -LiteralPath $source)) { throw "Required shader source missing: $source" }
    Copy-Item -LiteralPath $source -Destination "$fixture/Assets/Nvg/$name" -Force
}
Set-Content -LiteralPath "$fixture/Assets/Editor/BuildNvgBundle.cs" -Value @'
using System;
using System.IO;
using UnityEngine;
using UnityEditor;
public static class BuildNvgBundle
{
    public static void Run()
    {
        try
        {
            string[] paths = { "Assets/Nvg/NvgPhosphor.shader", "Assets/Nvg/NvgHeat.shader" };
            string[] names = { "Boscali/NvgPhosphor", "Boscali/NvgHeat" };
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
            if (AssetDatabase.LoadAssetAtPath<Shader>(paths[0]).passCount != 2) throw new Exception("NvgPhosphor must keep its two passes (picture, meter)");
            var build = new AssetBundleBuild { assetBundleName = "nightvision.bundle", assetNames = paths };
            var manifest = BuildPipeline.BuildAssetBundles("BundleOutput", new[] { build },
                BuildAssetBundleOptions.ChunkBasedCompression, BuildTarget.StandaloneWindows64);
            if (manifest == null) throw new Exception("AssetBundle compiler returned null");
            foreach (string path in paths)
                if (ShaderUtil.ShaderHasError(AssetDatabase.LoadAssetAtPath<Shader>(path))) throw new Exception("Shader compile failed: " + path);
            if (new FileInfo("BundleOutput/nightvision.bundle").Length > 512 * 1024)
                throw new Exception("nightvision.bundle exceeds its 512 KiB loader limit");
            File.WriteAllText("build-result.txt", "PASS nightvision.bundle: Boscali/NvgPhosphor, Boscali/NvgHeat");
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
Write-Output "Night vision shader compiler: $fixture"
$resultPath = Join-Path $fixture 'build-result.txt'
if (Test-Path -LiteralPath $resultPath) { Remove-Item -LiteralPath $resultPath }
$arguments = @('-batchmode', '-projectPath', ('"' + $fixture + '"'), '-executeMethod', 'BuildNvgBundle.Run', '-logFile', ('"' + "$fixture/build.log" + '"'))
$process = Start-Process -FilePath $Unity -ArgumentList $arguments -WorkingDirectory $fixture -WindowStyle Hidden -PassThru
$deadline = [DateTime]::UtcNow.AddMinutes(10)
while (-not $process.WaitForExit(1000)) {
    if ([DateTime]::UtcNow -ge $deadline) { $process.Kill(); $process.WaitForExit(); throw "Night vision shader compiler timed out: $fixture/build.log" }
}
if ($process.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $resultPath) -or
    -not (Select-String -LiteralPath $resultPath -Pattern '^PASS ' -Quiet)) {
    if (Test-Path -LiteralPath $resultPath) { Get-Content -LiteralPath $resultPath }
    throw "Night vision shader compilation failed: $fixture/build.log"
}
New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null
$target = Join-Path $OutputDir 'nightvision.bundle'
Copy-Item -LiteralPath "$fixture/BundleOutput/nightvision.bundle" -Destination $target -Force
Get-Content -LiteralPath $resultPath
Write-Output "Bundle: $target"
Write-Output "Evidence: $fixture"
