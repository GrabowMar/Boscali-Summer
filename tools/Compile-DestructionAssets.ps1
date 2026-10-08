param(
    [string]$Unity = 'C:/Program Files/Unity/Hub/Editor/2022.3.62f3/Editor/Unity.exe',
    [string]$EvidenceDir = (Join-Path $PSScriptRoot '../.nomodkit/evidence/2026-10-07-destruction-remake/shader-build')
)
$ErrorActionPreference = 'Stop'
$project = [IO.Path]::GetFullPath($EvidenceDir)
New-Item -ItemType Directory -Force -Path "$project/Assets/Structure", "$project/Assets/Editor", "$project/Packages", "$project/ProjectSettings", "$project/BundleOutput" | Out-Null
Set-Content -LiteralPath "$project/ProjectSettings/ProjectVersion.txt" -Value 'm_EditorVersion: 2022.3.62f3'
Set-Content -LiteralPath "$project/Packages/manifest.json" -Value '{"dependencies":{"com.unity.render-pipelines.universal":"14.0.12","com.unity.modules.assetbundle":"1.0.0"}}'
Copy-Item -Path (Join-Path $PSScriptRoot '../modules/FireAndDestruction/Assets/Source/*') -Destination "$project/Assets/Structure" -Force
Set-Content -LiteralPath "$project/Assets/Editor/BuildStructure.cs" -Value @'
using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
public static class BuildStructure
{
    public static void Run()
    {
        try
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>("Assets/Renderer.asset");
            if (renderer == null) { renderer = ScriptableObject.CreateInstance<UniversalRendererData>(); AssetDatabase.CreateAsset(renderer,"Assets/Renderer.asset"); }
            var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/Pipeline.asset");
            if (pipeline == null) { pipeline = UniversalRenderPipelineAsset.Create(renderer); AssetDatabase.CreateAsset(pipeline,"Assets/Pipeline.asset"); }
            var settings = new SerializedObject(pipeline);
            settings.FindProperty("m_MainLightShadowsSupported").boolValue = true;
            settings.FindProperty("m_SoftShadowsSupported").boolValue = true;
            settings.ApplyModifiedPropertiesWithoutUndo();
            pipeline.supportsCameraDepthTexture = true;
            GraphicsSettings.renderPipelineAsset = pipeline;
            QualitySettings.renderPipeline = pipeline;
            AssetDatabase.SaveAssets();
            string[] paths = { "Assets/Structure/ConcreteDust.shader" };
            foreach (string path in paths)
            {
                var shader = AssetDatabase.LoadAssetAtPath<Shader>(path);
                string errors = "";
                if (shader != null) foreach (var message in ShaderUtil.GetShaderMessages(shader))
                    if (message.severity == UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error)
                        errors += message.message + " " + message.line + "\n";
                if (shader == null || errors.Length != 0) throw new Exception(path + "\n" + errors);
            }
            var manifest = BuildPipeline.BuildAssetBundles("BundleOutput",
                new[] { new AssetBundleBuild { assetBundleName = "structure.bundle", assetNames = paths } },
                BuildAssetBundleOptions.ChunkBasedCompression, BuildTarget.StandaloneWindows64);
            if (manifest == null) throw new Exception("Asset bundle build failed");
            File.WriteAllText("result.txt", "PASS: soft concrete dust shader, URP 14");
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
$arguments = @('-batchmode', '-projectPath', "`"$project`"", '-executeMethod', 'BuildStructure.Run', '-logFile', "`"$project/build.log`"")
$process = Start-Process -FilePath $Unity -ArgumentList $arguments -WindowStyle Hidden -Wait -PassThru
if ($process.ExitCode -ne 0 -or -not (Test-Path -LiteralPath "$project/result.txt") -or
    -not (Get-Content -LiteralPath "$project/result.txt" -Raw).StartsWith('PASS')) {
    throw "Structural shader build failed: $project/build.log"
}
Copy-Item -LiteralPath "$project/BundleOutput/structure.bundle" -Destination (Join-Path $PSScriptRoot '../modules/FireAndDestruction/Assets/structure.bundle') -Force
Get-Content -LiteralPath "$project/result.txt"
