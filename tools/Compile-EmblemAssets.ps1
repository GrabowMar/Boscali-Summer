param(
    [string]$SourceDir = (Join-Path $PSScriptRoot '../modules/Progression/Assets/Source'),
    [string]$OutputDir = (Join-Path $PSScriptRoot '../modules/Progression/Assets'),
    [string]$Unity = 'C:/Program Files/Unity/Hub/Editor/2022.3.62f3/Editor/Unity.exe',
    [string]$EvidenceDir = ''
)
# Compiles EmblemBake.shader (the tail-fin insignia texture-space bake) into emblem.bundle.
# The shader is plain CG with no pipeline includes, so the fixture needs no render pipeline package.
$ErrorActionPreference = 'Stop'
if (-not (Test-Path -LiteralPath $Unity)) { throw "Unity Editor not found: $Unity" }
$fixture = if ($EvidenceDir) { [IO.Path]::GetFullPath($EvidenceDir) } else {
    Join-Path $env:TEMP ('BoscaliEmblemBundle-' + [guid]::NewGuid().ToString('N'))
}
New-Item -ItemType Directory -Force -Path "$fixture/Assets/EmblemShader", "$fixture/Assets/Editor", "$fixture/ProjectSettings", "$fixture/Packages", "$fixture/BundleOutput" | Out-Null
$editorVersion = if ($Unity -like '*62f3*') { '2022.3.62f3' } else { '2022.3.62f2' }
Set-Content -LiteralPath "$fixture/ProjectSettings/ProjectVersion.txt" -Value "m_EditorVersion: $editorVersion"
Set-Content -LiteralPath "$fixture/Packages/manifest.json" -Value '{"dependencies":{"com.unity.modules.assetbundle":"1.0.0"}}'
$source = Join-Path $SourceDir 'EmblemBake.shader'
if (-not (Test-Path -LiteralPath $source)) { throw "Required shader source missing: $source" }
Copy-Item -LiteralPath $source -Destination "$fixture/Assets/EmblemShader/EmblemBake.shader" -Force
Set-Content -LiteralPath "$fixture/Assets/Editor/BuildEmblemBundle.cs" -Value @'
using System;
using System.IO;
using UnityEngine;
using UnityEditor;
public static class BuildEmblemBundle
{
    public static void Run()
    {
        try
        {
            const string path = "Assets/EmblemShader/EmblemBake.shader";
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(path);
            if (shader == null || ShaderUtil.ShaderHasError(shader))
            {
                string detail = "";
                if (shader != null) foreach (var message in ShaderUtil.GetShaderMessages(shader))
                    detail += "\n" + message.severity + " " + message.file + ":" + message.line + " " + message.message;
                throw new Exception("Shader import failed: " + path + detail);
            }
            var material = new Material(shader);
            if (!shader.isSupported || material.passCount != 2 ||
                material.FindPass("OUTBOARD") != 0 || material.FindPass("INBOARD") != 1)
                throw new Exception("Emblem shader does not preserve OUTBOARD0 / INBOARD1");
            if (!material.HasProperty("_MainTex")) throw new Exception("Emblem material property missing: _MainTex");
            UnityEngine.Object.DestroyImmediate(material);
            var build = new AssetBundleBuild { assetBundleName = "emblem.bundle", assetNames = new[] { path } };
            var manifest = BuildPipeline.BuildAssetBundles("BundleOutput", new[] { build },
                BuildAssetBundleOptions.ChunkBasedCompression, BuildTarget.StandaloneWindows64);
            if (manifest == null) throw new Exception("AssetBundle compiler returned null");
            if (ShaderUtil.ShaderHasError(AssetDatabase.LoadAssetAtPath<Shader>(path))) throw new Exception("Shader compile failed: " + path);
            if (new FileInfo("BundleOutput/emblem.bundle").Length > 256 * 1024)
                throw new Exception("Emblem bundle exceeds its 256 KiB loader limit");
            File.WriteAllText("build-result.txt", "PASS emblem bake shader bundle, OUTBOARD0 / INBOARD1");
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
Write-Output "Emblem shader compiler: $fixture"
$resultPath = Join-Path $fixture 'build-result.txt'
if (Test-Path -LiteralPath $resultPath) { Remove-Item -LiteralPath $resultPath }
$arguments = @('-batchmode', '-projectPath', ('"' + $fixture + '"'), '-executeMethod', 'BuildEmblemBundle.Run', '-logFile', ('"' + "$fixture/build.log" + '"'))
$process = Start-Process -FilePath $Unity -ArgumentList $arguments -WorkingDirectory $fixture -WindowStyle Hidden -PassThru
$deadline = [DateTime]::UtcNow.AddMinutes(10)
while (-not $process.WaitForExit(1000)) {
    if ([DateTime]::UtcNow -ge $deadline) { $process.Kill(); $process.WaitForExit(); throw "Emblem shader compiler timed out: $fixture/build.log" }
}
if ($process.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $resultPath) -or
    -not (Select-String -LiteralPath $resultPath -Pattern '^PASS ' -Quiet)) {
    if (Test-Path -LiteralPath $resultPath) { Get-Content -LiteralPath $resultPath }
    throw "Emblem shader compilation failed: $fixture/build.log"
}
New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null
$target = Join-Path $OutputDir 'emblem.bundle'
Copy-Item -LiteralPath "$fixture/BundleOutput/emblem.bundle" -Destination $target -Force
Get-Content -LiteralPath $resultPath
Write-Output "Bundle: $target"
Write-Output "Evidence: $fixture"
