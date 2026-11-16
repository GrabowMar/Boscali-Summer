param(
    [string]$SourceDir = (Join-Path $PSScriptRoot '../AvionicsUi/Assets/Source'),
    [string]$OutputDir = (Join-Path $PSScriptRoot '../AvionicsUi/Assets'),
    [string]$Unity = 'C:/Program Files/Unity/Hub/Editor/2022.3.62f3/Editor/Unity.exe',
    [string]$EvidenceDir = ''
)
# Compiles the second-generation UI shaders (NOA_UI_Glass2.shader, the visor glitch pair NOA_UI_VisorGlitch.shader and NOA_TMP_VisorGlitchSDF.shader)
# into avionics-fx.bundle. They ship apart from avionics-ui.bundle so an effect change never re-bakes the fonts. Plain CG, no pipeline includes; every
# *.shader and *.cginc in the source folder is staged (the glitch shaders include each other's .cginc files). Keep -EvidenceDir short (a long path breaks the batch build).
$ErrorActionPreference = 'Stop'
if (-not (Test-Path -LiteralPath $Unity)) { throw "Unity Editor not found: $Unity" }
$fixture = if ($EvidenceDir) { [IO.Path]::GetFullPath($EvidenceDir) } else {
    Join-Path $env:TEMP ('BoscaliFxBundle-' + [guid]::NewGuid().ToString('N'))
}
New-Item -ItemType Directory -Force -Path "$fixture/Assets/Fx", "$fixture/Assets/Editor", "$fixture/ProjectSettings", "$fixture/Packages", "$fixture/BundleOutput" | Out-Null
Set-Content -LiteralPath "$fixture/ProjectSettings/ProjectVersion.txt" -Value 'm_EditorVersion: 2022.3.62f3'
Set-Content -LiteralPath "$fixture/Packages/manifest.json" -Value '{"dependencies":{"com.unity.ugui":"1.0.0","com.unity.modules.assetbundle":"1.0.0"}}'
$names = @('NOA_UI_Glass2.shader', 'NOA_UI_VisorGlitch.shader', 'NOA_TMP_VisorGlitchSDF.shader')
foreach ($file in (Get-ChildItem -LiteralPath $SourceDir -File | Where-Object { $_.Extension -in '.shader', '.cginc' })) {
    Copy-Item -LiteralPath $file.FullName -Destination "$fixture/Assets/Fx/$($file.Name)" -Force
}
foreach ($n in $names) { if (-not (Test-Path -LiteralPath "$fixture/Assets/Fx/$n")) { throw "Required shader source missing: $SourceDir/$n" } }
Set-Content -LiteralPath "$fixture/Assets/Editor/BuildFxBundle.cs" -Value @'
using System;
using System.IO;
using UnityEngine;
using UnityEditor;
public static class BuildFxBundle
{
    static readonly string[] Paths = { "Assets/Fx/NOA_UI_Glass2.shader", "Assets/Fx/NOA_UI_VisorGlitch.shader", "Assets/Fx/NOA_TMP_VisorGlitchSDF.shader" };
    static readonly string[] Names = { "NOA/UI/Glass2", "NOA/UI/VisorGlitch", "NOA/TMP/VisorGlitchSDF" };
    public static void Run()
    {
        try
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            for (int i = 0; i < Paths.Length; i++)
            {
                Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(Paths[i]);
                if (shader == null || ShaderUtil.ShaderHasError(shader))
                {
                    string detail = "";
                    if (shader != null) foreach (var message in ShaderUtil.GetShaderMessages(shader))
                        detail += "\n" + message.severity + " " + message.file + ":" + message.line + " " + message.message;
                    throw new Exception("Shader import failed: " + Paths[i] + detail);
                }
                if (shader.name != Names[i]) throw new Exception("Unexpected shader name: " + shader.name);
                if (!shader.isSupported) throw new Exception(Names[i] + " is not supported on the build machine");
                var material = new Material(shader);
                if (i == 0) foreach (string property in new[] { "_Reflection", "_Wear", "_Edge", "_Light", "_Frost", "_Life" })
                    if (!material.HasProperty(property)) throw new Exception("Glass2 material property missing: " + property);
                UnityEngine.Object.DestroyImmediate(material);
            }
            var build = new AssetBundleBuild { assetBundleName = "avionics-fx.bundle", assetNames = Paths };
            var manifest = BuildPipeline.BuildAssetBundles("BundleOutput", new[] { build },
                BuildAssetBundleOptions.ChunkBasedCompression, BuildTarget.StandaloneWindows64);
            if (manifest == null) throw new Exception("AssetBundle compiler returned null");
            foreach (string p in Paths) if (ShaderUtil.ShaderHasError(AssetDatabase.LoadAssetAtPath<Shader>(p))) throw new Exception("Shader compile failed: " + p);
            long size = new FileInfo("BundleOutput/avionics-fx.bundle").Length;
            if (size > 1024 * 1024) throw new Exception("avionics-fx.bundle exceeds its 1 MiB loader limit: " + size);
            File.WriteAllText("build-result.txt", "PASS avionics-fx.bundle: " + string.Join(", ", Names) + " (" + size + " bytes)");
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
