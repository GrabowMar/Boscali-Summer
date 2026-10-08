param([string]$Unity = 'C:/Program Files/Unity/Hub/Editor/2022.3.62f3/Editor/Unity.exe')
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/../tests/UnityCheck.Common.ps1"
$project = "$repo/.nomodkit/evidence/forest-front/shader"
New-UnityCheckProject $project '{"dependencies":{"com.unity.modules.assetbundle":"1.0.0"}}' -Folders 'Assets/Editor','BundleOutput'
Copy-Item -LiteralPath "$repo/modules/FireAndDestruction/Assets/Source/ForestBurnSurface.shader" -Destination "$project/Assets/"
Set-Content -LiteralPath "$project/Assets/Editor/BuildForest.cs" -Value @'
using System;
using System.IO;
using UnityEditor;
using UnityEngine;
public static class BuildForest
{
    public static void Run()
    {
        try
        {
            var shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/ForestBurnSurface.shader");
            if (shader == null) throw new Exception("Missing forest shader");
            foreach (var error in ShaderUtil.GetShaderMessages(shader))
                if (error.severity == UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error) throw new Exception(error.message);
            var result = BuildPipeline.BuildAssetBundles("BundleOutput", new[] { new AssetBundleBuild {
                assetBundleName="forestfire.bundle", assetNames=new[] {"Assets/ForestBurnSurface.shader"} } },
                BuildAssetBundleOptions.ChunkBasedCompression, BuildTarget.StandaloneWindows64);
            if (result == null) throw new Exception("Forest bundle build failed");
            File.WriteAllText("result.txt","PASS: forest surface shader/bundle"); EditorApplication.Exit(0);
        }
        catch (Exception error) { File.WriteAllText("result.txt","FAIL: "+error); EditorApplication.Exit(1); }
    }
}
'@
$process = Invoke-UnityCheck $Unity $project 'BuildForest.Run' -TimeoutSeconds 180
Assert-UnityResult $project $process
Copy-Item -LiteralPath "$project/BundleOutput/forestfire.bundle" -Destination "$repo/modules/FireAndDestruction/Assets/forestfire.bundle"
Get-Content "$project/result.txt"
