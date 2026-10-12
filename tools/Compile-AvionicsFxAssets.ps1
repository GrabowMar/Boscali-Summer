param(
    [string]$SourceDir = (Join-Path $PSScriptRoot '../AvionicsUi/Assets/Source'),
    [string]$OutputDir = (Join-Path $PSScriptRoot '../AvionicsUi/Assets'),
    [string]$Unity = 'C:/Program Files/Unity/Hub/Editor/2022.3.62f3/Editor/Unity.exe',
    [string]$EvidenceDir = ''
)
# Compiles the second-generation UI shaders (NOA_UI_Glass2.shader) into avionics-fx.bundle. They ship apart from
# avionics-ui.bundle so an effect change never re-bakes the fonts. Plain CG, no pipeline includes.
$ErrorActionPreference = 'Stop'
if (-not (Test-Path -LiteralPath $Unity)) { throw "Unity Editor not found: $Unity" }
$fixture = if ($EvidenceDir) { [IO.Path]::GetFullPath($EvidenceDir) } else {
    Join-Path $env:TEMP ('BoscaliFxBundle-' + [guid]::NewGuid().ToString('N'))
}
New-Item -ItemType Directory -Force -Path "$fixture/Assets/Fx", "$fixture/Assets/Editor", "$fixture/ProjectSettings", "$fixture/Packages", "$fixture/BundleOutput" | Out-Null
Set-Content -LiteralPath "$fixture/ProjectSettings/ProjectVersion.txt" -Value 'm_EditorVersion: 2022.3.62f3'
Set-Content -LiteralPath "$fixture/Packages/manifest.json" -Value '{"dependencies":{"com.unity.ugui":"1.0.0","com.unity.modules.assetbundle":"1.0.0"}}'
$source = Join-Path $SourceDir 'NOA_UI_Glass2.shader'
if (-not (Test-Path -LiteralPath $source)) { throw "Required shader source missing: $source" }
Copy-Item -LiteralPath $source -Destination "$fixture/Assets/Fx/NOA_UI_Glass2.shader" -Force
Set-Content -LiteralPath "$fixture/Assets/Editor/BuildFxBundle.cs" -Value @'
using System;
using System.IO;
using UnityEngine;
using UnityEditor;
public static class BuildFxBundle
{
    public static void Run()
    {
        try
        {
            const string path = "Assets/Fx/NOA_UI_Glass2.shader";
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(path);
            if (shader == null || ShaderUtil.ShaderHasError(shader))
            {
                string detail = "";
                if (shader != null) foreach (var message in ShaderUtil.GetShaderMessages(shader))
                    detail += "\n" + message.severity + " " + message.file + ":" + message.line + " " + message.message;
                throw new Exception("Shader import failed: " + path + detail);
            }
            if (shader.name != "NOA/UI/Glass2") throw new Exception("Unexpected shader name: " + shader.name);
            var material = new Material(shader);
            foreach (string property in new[] { "_Reflection", "_Wear", "_Edge", "_Light", "_Frost", "_Life" })
                if (!material.HasProperty(property)) throw new Exception("Glass2 material property missing: " + property);
            if (!shader.isSupported) throw new Exception("Glass2 is not supported on the build machine");
            UnityEngine.Object.DestroyImmediate(material);
            var build = new AssetBundleBuild { assetBundleName = "avionics-fx.bundle", assetNames = new[] { path } };
            var manifest = BuildPipeline.BuildAssetBundles("BundleOutput", new[] { build },
                BuildAssetBundleOptions.ChunkBasedCompression, BuildTarget.StandaloneWindows64);
            if (manifest == null) throw new Exception("AssetBundle compiler returned null");
            if (ShaderUtil.ShaderHasError(AssetDatabase.LoadAssetAtPath<Shader>(path))) throw new Exception("Shader compile failed: " + path);
            if (new FileInfo("BundleOutput/avionics-fx.bundle").Length > 512 * 1024)
                throw new Exception("avionics-fx.bundle exceeds its 512 KiB loader limit");
            File.WriteAllText("build-result.txt", "PASS avionics-fx.bundle: NOA/UI/Glass2");
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
Write-Output "Avionics FX shader compiler: $fixture"
$resultPath = Join-Path $fixture 'build-result.txt'
if (Test-Path -LiteralPath $resultPath) { Remove-Item -LiteralPath $resultPath }
$arguments = @('-batchmode', '-projectPath', ('"' + $fixture + '"'), '-executeMethod', 'BuildFxBundle.Run', '-logFile', ('"' + "$fixture/build.log" + '"'))
$process = Start-Process -FilePath $Unity -ArgumentList $arguments -WorkingDirectory $fixture -WindowStyle Hidden -PassThru
$deadline = [DateTime]::UtcNow.AddMinutes(10)
while (-not $process.WaitForExit(1000)) {
    if ([DateTime]::UtcNow -ge $deadline) { $process.Kill(); $process.WaitForExit(); throw "Avionics FX shader compiler timed out: $fixture/build.log" }
}
if ($process.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $resultPath) -or
    -not (Select-String -LiteralPath $resultPath -Pattern '^PASS ' -Quiet)) {
    if (Test-Path -LiteralPath $resultPath) { Get-Content -LiteralPath $resultPath }
    throw "Avionics FX shader compilation failed: $fixture/build.log"
}
New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null
$target = Join-Path $OutputDir 'avionics-fx.bundle'
Copy-Item -LiteralPath "$fixture/BundleOutput/avionics-fx.bundle" -Destination $target -Force
Get-Content -LiteralPath $resultPath
Write-Output "Bundle: $target"
Write-Output "Evidence: $fixture"
