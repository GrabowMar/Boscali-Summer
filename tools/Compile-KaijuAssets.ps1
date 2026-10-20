param(
    [string]$Unity = 'C:/Program Files/Unity/Hub/Editor/2022.3.62f3/Editor/Unity.exe',
    [string]$WorkDir = (Join-Path $env:TEMP 'BoscaliKaiju')
)
# Bakes modules/Kaiju/Assets/kaiju.bundle (embedded, loaded by modules/Kaiju/Runtime/KaijuAssets.cs) from the sources
# BuildKaijuModel.py writes and the generator in modules/Kaiju/Assets/Editor/KaijuAssetsBuilder.cs (materials, clips,
# animator, prefabs). A throwaway URP 14 project, so the Unity MCP editor where the clips are iterated can stay open.
$ErrorActionPreference = 'Stop'
if (-not (Test-Path -LiteralPath $Unity)) { throw "Unity Editor not found: $Unity" }
$repo = Resolve-Path (Join-Path $PSScriptRoot '..')
$project = [IO.Path]::GetFullPath($WorkDir)
$kaiju = "$project/Assets/BoscaliSummer/Kaiju"
if (Test-Path -LiteralPath $kaiju) { Remove-Item -Recurse -Force -LiteralPath $kaiju }
New-Item -ItemType Directory -Force -Path "$kaiju/Editor", "$project/Assets/Editor", "$project/Packages", "$project/ProjectSettings", "$project/BundleOutput" | Out-Null
Set-Content -LiteralPath "$project/ProjectSettings/ProjectVersion.txt" -Value 'm_EditorVersion: 2022.3.62f3'
Set-Content -LiteralPath "$project/Packages/manifest.json" -Value '{"dependencies":{"com.unity.render-pipelines.universal":"14.0.12","com.unity.modules.assetbundle":"1.0.0","com.unity.modules.animation":"1.0.0","com.unity.modules.physics":"1.0.0"}}'
foreach ($name in 'Kaiju.fbx', 'KaijuSpike.fbx', 'KaijuBlister.fbx', 'Kaiju_Albedo.png', 'Kaiju_Normal.png', 'Kaiju_Emission.png') {
    $source = Join-Path $repo "modules/Kaiju/Assets/Source/$name"
    if (-not (Test-Path -LiteralPath $source)) { throw "Missing $source (run modules/Kaiju/Assets/Source/BuildKaijuModel.py in Blender)" }
    Copy-Item -LiteralPath $source -Destination $kaiju -Force
}
Copy-Item -LiteralPath (Join-Path $repo 'modules/Kaiju/Assets/Editor/KaijuAssetsBuilder.cs') -Destination "$kaiju/Editor" -Force
Set-Content -LiteralPath "$project/Assets/Editor/KaijuBundleBuild.cs" -Value @'
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
public static class KaijuBundleBuild
{
    public static void Run()
    {
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>("Assets/Renderer.asset");
        if (renderer == null) { renderer = ScriptableObject.CreateInstance<UniversalRendererData>(); AssetDatabase.CreateAsset(renderer, "Assets/Renderer.asset"); }
        var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/Pipeline.asset");
        if (pipeline == null) { pipeline = UniversalRenderPipelineAsset.Create(renderer); AssetDatabase.CreateAsset(pipeline, "Assets/Pipeline.asset"); }
        GraphicsSettings.renderPipelineAsset = pipeline;
        QualitySettings.renderPipeline = pipeline;
        AssetDatabase.SaveAssets();
        KaijuAssetsBuilder.BuildBundle(); // exits the editor with 0/1 and writes result.txt
    }
}
'@
Remove-Item -LiteralPath "$project/result.txt" -ErrorAction SilentlyContinue
$arguments = @('-batchmode', '-projectPath', "`"$project`"", '-executeMethod', 'KaijuBundleBuild.Run', '-logFile', "`"$project/build.log`"")
$process = Start-Process -FilePath $Unity -ArgumentList $arguments -WorkingDirectory $project -WindowStyle Hidden -PassThru
if (-not $process.WaitForExit(30 * 60 * 1000)) { $process.Kill(); throw "Kaiju bundle build timed out: $project/build.log" }
if ($process.ExitCode -ne 0 -or -not (Test-Path -LiteralPath "$project/result.txt") -or
    -not (Get-Content -LiteralPath "$project/result.txt" -Raw).StartsWith('PASS')) {
    throw "Kaiju bundle build failed: $project/build.log"
}
Copy-Item -LiteralPath "$project/BundleOutput/kaiju.bundle" -Destination (Join-Path $repo 'modules/Kaiju/Assets/kaiju.bundle') -Force
Get-Content -LiteralPath "$project/result.txt"
