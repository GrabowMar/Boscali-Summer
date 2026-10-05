param(
    [string]$SourceDir = (Join-Path $PSScriptRoot '../modules/Immersion/Assets/Source'),
    [string]$OutputDir = (Join-Path $PSScriptRoot '../modules/Immersion/Assets'),
    [string]$Unity = 'C:/Program Files/Unity/Hub/Editor/2022.3.62f3/Editor/Unity.exe',
    [string]$EvidenceDir = ''
)
$ErrorActionPreference = 'Stop'
if (-not (Test-Path -LiteralPath $Unity)) { throw "Unity Editor not found: $Unity" }
$fixture = if ($EvidenceDir) { [IO.Path]::GetFullPath($EvidenceDir) } else {
    Join-Path $env:TEMP ('BoscaliPilotBundle-' + [guid]::NewGuid().ToString('N'))
}
New-Item -ItemType Directory -Force -Path "$fixture/Assets/PilotShader", "$fixture/Assets/Editor", "$fixture/ProjectSettings", "$fixture/Packages", "$fixture/BundleOutput" | Out-Null
$editorVersion = if ($Unity -like '*62f3*') { '2022.3.62f3' } else { '2022.3.62f2' }
Set-Content -LiteralPath "$fixture/ProjectSettings/ProjectVersion.txt" -Value "m_EditorVersion: $editorVersion"
Set-Content -LiteralPath "$fixture/Packages/manifest.json" -Value '{"dependencies":{"com.unity.render-pipelines.universal":"14.0.12","com.unity.modules.assetbundle":"1.0.0"}}'
foreach ($name in 'PilotBody.shader', 'PilotCanopyReflection.shader', 'pilot-first-person.json') {
    $source = Join-Path $SourceDir $name
    if (-not (Test-Path -LiteralPath $source)) { throw "Required pilot asset source missing: $source" }
    Copy-Item -LiteralPath $source -Destination "$fixture/Assets/PilotShader/$name" -Force
}
Set-Content -LiteralPath "$fixture/Assets/Editor/BuildPilotBundle.cs" -Value @'
using System;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEditor;
public static class BuildPilotBundle
{
    [Serializable]
    private sealed class PilotMeshData
    {
        public int vertexCount;
        public float[] vertices, normals, tangents, uv, boneWeights, bindposes;
        public int[] triangles, boneIndices;
    }

    private static void BuildFirstPersonMesh()
    {
        var data = JsonUtility.FromJson<PilotMeshData>(File.ReadAllText("Assets/PilotShader/pilot-first-person.json"));
        int n = data == null ? 0 : data.vertexCount;
        if (n <= 0 || n > 65535 || data.vertices == null || data.vertices.Length != n * 3 ||
            data.normals == null || data.normals.Length != n * 3 ||
            data.tangents == null || data.tangents.Length != n * 4 ||
            data.uv == null || data.uv.Length != n * 2 ||
            data.boneIndices == null || data.boneIndices.Length != n * 4 ||
            data.boneWeights == null || data.boneWeights.Length != n * 4 ||
            data.bindposes == null || data.bindposes.Length != 16 * 16 ||
            data.triangles == null || data.triangles.Length == 0 || data.triangles.Length % 3 != 0)
            throw new Exception("Invalid dedicated pilot mesh arrays");
        var vertices = new Vector3[n];
        var normals = new Vector3[n];
        var tangents = new Vector4[n];
        var uv = new Vector2[n];
        var weights = new BoneWeight[n];
        for (int i = 0; i < n; i++)
        {
            vertices[i] = new Vector3(data.vertices[i*3], data.vertices[i*3+1], data.vertices[i*3+2]);
            normals[i] = new Vector3(data.normals[i*3], data.normals[i*3+1], data.normals[i*3+2]);
            tangents[i] = new Vector4(data.tangents[i*4], data.tangents[i*4+1], data.tangents[i*4+2], data.tangents[i*4+3]);
            uv[i] = new Vector2(data.uv[i*2], data.uv[i*2+1]);
            for (int slot = 0; slot < 4; slot++)
                if (data.boneIndices[i*4+slot] < 0 || data.boneIndices[i*4+slot] >= 16)
                    throw new Exception("Dedicated pilot mesh has a foreign bone index");
            weights[i] = new BoneWeight {
                boneIndex0 = data.boneIndices[i*4], boneIndex1 = data.boneIndices[i*4+1],
                boneIndex2 = data.boneIndices[i*4+2], boneIndex3 = data.boneIndices[i*4+3],
                weight0 = data.boneWeights[i*4], weight1 = data.boneWeights[i*4+1],
                weight2 = data.boneWeights[i*4+2], weight3 = data.boneWeights[i*4+3]
            };
        }
        foreach (int index in data.triangles)
            if (index < 0 || index >= n) throw new Exception("Dedicated pilot mesh has a foreign vertex index");
        var bind = new Matrix4x4[16];
        for (int i = 0; i < bind.Length; i++)
            for (int row = 0; row < 4; row++)
                for (int col = 0; col < 4; col++) bind[i][row,col] = data.bindposes[i*16+row*4+col];
        const string path = "Assets/PilotShader/pilot-first-person.asset";
        AssetDatabase.DeleteAsset(path);
        var mesh = new Mesh { name = "pilot-first-person" };
        mesh.vertices = vertices; mesh.normals = normals; mesh.tangents = tangents; mesh.uv = uv;
        mesh.boneWeights = weights; mesh.bindposes = bind; mesh.triangles = data.triangles;
        mesh.RecalculateBounds();
        AssetDatabase.CreateAsset(mesh, path);
        mesh.UploadMeshData(true);
        EditorUtility.SetDirty(mesh);
        AssetDatabase.SaveAssets();
        File.WriteAllText("mesh-result.txt", "PASS dedicated first-person pilot " + n + " vertices / " +
            data.triangles.Length / 3 + " triangles / 16 bones; readable=" + mesh.isReadable);
    }

    public static void Run()
    {
        try
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            // URP strips its variants when no pipeline is configured. Build under the
            // same supported pipeline used by the standalone capture fixture.
            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>("Assets/PilotRenderer.asset");
            if (renderer == null)
            {
                renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
                AssetDatabase.CreateAsset(renderer, "Assets/PilotRenderer.asset");
            }
            var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/PilotPipeline.asset");
            if (pipeline == null)
            {
                pipeline = UniversalRenderPipelineAsset.Create(renderer);
                AssetDatabase.CreateAsset(pipeline, "Assets/PilotPipeline.asset");
            }
            pipeline.msaaSampleCount = 1;
            GraphicsSettings.renderPipelineAsset = pipeline;
            QualitySettings.renderPipeline = pipeline;
            AssetDatabase.SaveAssets();
            string[] shaderPaths = { "Assets/PilotShader/PilotBody.shader", "Assets/PilotShader/PilotCanopyReflection.shader" };
            foreach (string path in shaderPaths)
            {
                Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(path);
                if (shader == null || ShaderUtil.ShaderHasError(shader))
                {
                    string detail = "";
                    if (shader != null) foreach (var message in ShaderUtil.GetShaderMessages(shader))
                        detail += "\n" + message.severity + " " + message.file + ":" + message.line + " " + message.message;
                    throw new Exception("Shader import failed: " + path + detail);
                }
            }
            var bodyMaterial = new Material(AssetDatabase.LoadAssetAtPath<Shader>(shaderPaths[0]));
            if (!bodyMaterial.shader.isSupported || bodyMaterial.passCount != 2 ||
                bodyMaterial.FindPass("BODY") != 0 || bodyMaterial.FindPass("CAPTURE") != 1)
                throw new Exception("Pilot shader does not preserve BODY0 / CAPTURE1");
            string[] materialProperties = { "_BumpMap", "_BumpScale", "_MetallicGlossMap", "_Metallic", "_Smoothness",
                "_SmoothnessTextureChannel", "_SpecularHighlights", "_OcclusionMap", "_OcclusionStrength",
                "_HasNormalMap", "_HasMetallicMap", "_HasOcclusionMap", "_HeadMaskMode" };
            foreach (string property in materialProperties)
                if (!bodyMaterial.HasProperty(property)) throw new Exception("Pilot material property missing: " + property);
            File.WriteAllText("shader-result.txt", "PASS supported BODY0 / CAPTURE1; native material contract: " +
                string.Join(", ", materialProperties));
            UnityEngine.Object.DestroyImmediate(bodyMaterial);
            BuildFirstPersonMesh();
            string[] paths = { shaderPaths[0], shaderPaths[1], "Assets/PilotShader/pilot-first-person.asset" };
            var build = new AssetBundleBuild { assetBundleName = "pilot.bundle", assetNames = paths };
            var manifest = BuildPipeline.BuildAssetBundles("BundleOutput", new[] { build },
                BuildAssetBundleOptions.ChunkBasedCompression, BuildTarget.StandaloneWindows64);
            if (manifest == null) throw new Exception("AssetBundle compiler returned null");
            foreach (string path in shaderPaths)
                if (ShaderUtil.ShaderHasError(AssetDatabase.LoadAssetAtPath<Shader>(path)))
                    throw new Exception("Shader compile failed: " + path);
            if (new FileInfo("BundleOutput/pilot.bundle").Length > 4 * 1024 * 1024)
                throw new Exception("Pilot bundle exceeds its 4 MiB loader limit");
            File.WriteAllText("build-result.txt", "PASS pilot shader bundle");
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
Get-ChildItem -LiteralPath "$fixture/Assets" -Recurse -File | Get-FileHash |
    Select-Object Hash, Path | Export-Csv -LiteralPath "$fixture/input-manifest.csv" -NoTypeInformation
Write-Output "Pilot shader compiler: $fixture"
$resultPath = Join-Path $fixture 'build-result.txt'
if (Test-Path -LiteralPath $resultPath) { Remove-Item -LiteralPath $resultPath }
$arguments = @('-batchmode', '-projectPath', ('"' + $fixture + '"'), '-executeMethod', 'BuildPilotBundle.Run', '-logFile', ('"' + "$fixture/build.log" + '"'))
$process = Start-Process -FilePath $Unity -ArgumentList $arguments -WorkingDirectory $fixture -WindowStyle Hidden -PassThru
$deadline = [DateTime]::UtcNow.AddMinutes(10)
while (-not $process.WaitForExit(1000)) {
    if ([DateTime]::UtcNow -ge $deadline) { $process.Kill(); $process.WaitForExit(); throw "Pilot shader compiler timed out: $fixture/build.log" }
}
if ($process.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $resultPath) -or
    -not (Select-String -LiteralPath $resultPath -Pattern '^PASS ' -Quiet)) {
    if (Test-Path -LiteralPath $resultPath) { Get-Content -LiteralPath $resultPath }
    throw "Pilot shader compilation failed: $fixture/build.log"
}
New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null
$target = Join-Path $OutputDir 'pilot.bundle'
Copy-Item -LiteralPath "$fixture/BundleOutput/pilot.bundle" -Destination $target -Force
Get-FileHash -LiteralPath $target | Select-Object Hash, Path | Export-Csv -LiteralPath "$fixture/bundle-sha256.csv" -NoTypeInformation
Get-Content -LiteralPath $resultPath
Write-Output "Bundle: $target"
Write-Output "Evidence: $fixture"
