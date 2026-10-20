param(
    [string]$Unity = 'C:/Program Files/Unity/Hub/Editor/2022.3.62f3/Editor/Unity.exe',
    [string]$WorkDir = (Join-Path $env:TEMP 'BoscaliParticleFx')
)
# Bakes the authored particle prefabs (tools/ParticleFx/Fx, edited live in C:\Users\marci\dev\fx-authoring through the
# Unity-MCP + Unity-AI-ParticleSystem tools) into Core/Fx/Assets/particles.bundle, embedded and spawned by Core/Fx/ParticleFx.
# A separate throwaway URP 14 project, so the authoring editor can stay open.
$ErrorActionPreference = 'Stop'
if (-not (Test-Path -LiteralPath $Unity)) { throw "Unity Editor not found: $Unity" }
$project = [IO.Path]::GetFullPath($WorkDir)
if (Test-Path -LiteralPath "$project/Assets/Fx") { Remove-Item -Recurse -Force -LiteralPath "$project/Assets/Fx" }
New-Item -ItemType Directory -Force -Path "$project/Assets/Fx", "$project/Assets/Editor", "$project/Packages", "$project/ProjectSettings", "$project/BundleOutput" | Out-Null
Set-Content -LiteralPath "$project/ProjectSettings/ProjectVersion.txt" -Value 'm_EditorVersion: 2022.3.62f3'
Set-Content -LiteralPath "$project/Packages/manifest.json" -Value '{"dependencies":{"com.unity.render-pipelines.universal":"14.0.12","com.unity.modules.assetbundle":"1.0.0","com.unity.modules.particlesystem":"1.0.0"}}'
Copy-Item -Path (Join-Path $PSScriptRoot 'ParticleFx/Fx/*') -Destination "$project/Assets/Fx" -Recurse -Force
Set-Content -LiteralPath "$project/Assets/Editor/BuildParticles.cs" -Value @'
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
public static class BuildParticles
{
    public static void Run()
    {
        try
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>("Assets/Renderer.asset");
            if (renderer == null) { renderer = ScriptableObject.CreateInstance<UniversalRendererData>(); AssetDatabase.CreateAsset(renderer, "Assets/Renderer.asset"); }
            var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/Pipeline.asset");
            if (pipeline == null) { pipeline = UniversalRenderPipelineAsset.Create(renderer); AssetDatabase.CreateAsset(pipeline, "Assets/Pipeline.asset"); }
            GraphicsSettings.renderPipelineAsset = pipeline;
            QualitySettings.renderPipeline = pipeline;
            AssetDatabase.SaveAssets();
            string[] prefabs = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Fx/Prefabs" }).Select(AssetDatabase.GUIDToAssetPath).ToArray();
            if (prefabs.Length == 0) throw new Exception("no prefabs under Assets/Fx/Prefabs");
            var manifest = BuildPipeline.BuildAssetBundles("BundleOutput",
                new[] { new AssetBundleBuild { assetBundleName = "particles.bundle", assetNames = prefabs } },
                BuildAssetBundleOptions.ChunkBasedCompression, BuildTarget.StandaloneWindows64);
            if (manifest == null) throw new Exception("Asset bundle build failed");
            File.WriteAllText("result.txt", "PASS: " + prefabs.Length + " particle prefab(s): " + string.Join(", ", prefabs.Select(Path.GetFileNameWithoutExtension)));
            EditorApplication.Exit(0);
        }
        catch (Exception error)
        {
            File.WriteAllText("result.txt", "FAIL: " + error);
            Debug.LogException(error);
            EditorApplication.Exit(1);
        }
    }
}
'@
Remove-Item -LiteralPath "$project/result.txt" -ErrorAction SilentlyContinue
$arguments = @('-batchmode', '-projectPath', "`"$project`"", '-executeMethod', 'BuildParticles.Run', '-logFile', "`"$project/build.log`"")
$process = Start-Process -FilePath $Unity -ArgumentList $arguments -WindowStyle Hidden -Wait -PassThru
if ($process.ExitCode -ne 0 -or -not (Test-Path -LiteralPath "$project/result.txt") -or
    -not (Get-Content -LiteralPath "$project/result.txt" -Raw).StartsWith('PASS')) {
    throw "Particle bundle build failed: $project/build.log"
}
$out = Join-Path $PSScriptRoot '../Core/Fx/Assets'
New-Item -ItemType Directory -Force -Path $out | Out-Null
Copy-Item -LiteralPath "$project/BundleOutput/particles.bundle" -Destination (Join-Path $out 'particles.bundle') -Force
Get-Content -LiteralPath "$project/result.txt"
