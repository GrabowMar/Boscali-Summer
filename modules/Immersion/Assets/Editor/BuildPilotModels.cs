#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

// Offline model compiler. The existing pilot/visor shaders are shipped separately and stay unchanged.
public static class BuildPilotModels
{
    [Serializable]
    private sealed class Data
    {
        public int vertexCount;
        public float[] vertices, normals, tangents, uv, boneWeights, bindposes;
        public int[] triangles, boneIndices;
    }

    private static Matrix4x4[] referenceBind;
    private static readonly List<string> paths = new List<string>();
    private static readonly List<string> checks = new List<string>();

    public static void Run()
    {
        try
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            foreach (string suffix in new[] { "", "-recon", "-rescue", "-nightops" })
            {
                Mesh(suffix);
                Texture("pilot_albedo" + suffix.Replace('-', '_'), false, false);
            }
            Texture("pilot_normal", true, false); Texture("pilot_mask", false, true);
            Texture("kneeboard_albedo", false, false); Texture("kneeboard_normal", true, false); Texture("kneeboard_mask", false, true);
            const string inkPath = "Assets/PilotShader/KneeboardInk.shader";
            var ink = AssetDatabase.LoadAssetAtPath<Shader>(inkPath);
            if (ink == null || ShaderUtil.ShaderHasError(ink)) throw new Exception("Invalid kneeboard ink shader");
            paths.Add(inkPath);
            AssetDatabase.SaveAssets();
            var build = new AssetBundleBuild { assetBundleName = "pilot-models.bundle", assetNames = paths.ToArray() };
            var manifest = BuildPipeline.BuildAssetBundles("BundleOutput", new[] { build },
                BuildAssetBundleOptions.ChunkBasedCompression, BuildTarget.StandaloneWindows64);
            if (manifest == null) throw new Exception("Model bundle compiler returned null");
            long bytes = new FileInfo("BundleOutput/pilot-models.bundle").Length;
            if (bytes > 24 * 1024 * 1024) throw new Exception("Model bundle exceeds bounded 24 MiB resource limit");
            File.WriteAllText("build-result.txt", "PASS four cockpit models; " + bytes + " bytes\n" + string.Join("\n", checks));
            EditorApplication.Exit(0);
        }
        catch (Exception error)
        { File.WriteAllText("build-result.txt", "FAIL " + error); Debug.LogException(error); EditorApplication.Exit(1); }
    }

    private static void Mesh(string suffix)
    {
        string stem = "Assets/PilotShader/pilot-first-person" + suffix;
        var d = JsonUtility.FromJson<Data>(File.ReadAllText(stem + ".json"));
        int n = d == null ? 0 : d.vertexCount;
        if (n <= 0 || n > 50000 || d.vertices?.Length != n * 3 || d.normals?.Length != n * 3 ||
            d.tangents?.Length != n * 4 || d.uv?.Length != n * 2 || d.boneIndices?.Length != n * 4 ||
            d.boneWeights?.Length != n * 4 || d.bindposes?.Length != 256 || d.triangles == null ||
            d.triangles.Length == 0 || d.triangles.Length % 3 != 0 || d.triangles.Length > 45000 * 3)
            throw new Exception("Invalid model arrays/budget: " + stem);
        var vertices = new Vector3[n]; var normals = new Vector3[n]; var tangents = new Vector4[n];
        var uv = new Vector2[n]; var weights = new BoneWeight[n]; var bind = new Matrix4x4[16];
        for (int i = 0; i < n; i++)
        {
            vertices[i] = new Vector3(d.vertices[i*3], d.vertices[i*3+1], d.vertices[i*3+2]);
            normals[i] = new Vector3(d.normals[i*3], d.normals[i*3+1], d.normals[i*3+2]);
            tangents[i] = new Vector4(d.tangents[i*4], d.tangents[i*4+1], d.tangents[i*4+2], d.tangents[i*4+3]);
            uv[i] = new Vector2(d.uv[i*2], d.uv[i*2+1]);
            float sum = 0;
            for (int s = 0; s < 4; s++)
            {
                int k = i*4+s;
                if (d.boneIndices[k] < 0 || d.boneIndices[k] >= 16 || !float.IsFinite(d.boneWeights[k]) || d.boneWeights[k] < 0)
                    throw new Exception("Invalid skin weights: " + stem);
                sum += d.boneWeights[k];
            }
            if (Mathf.Abs(sum - 1) > .001f || !float.IsFinite(vertices[i].sqrMagnitude) ||
                !float.IsFinite(normals[i].sqrMagnitude) || !float.IsFinite(tangents[i].sqrMagnitude) || !float.IsFinite(uv[i].sqrMagnitude))
                throw new Exception("Nonfinite or unnormalised model vertex: " + stem);
            weights[i] = new BoneWeight { boneIndex0=d.boneIndices[i*4], boneIndex1=d.boneIndices[i*4+1],
                boneIndex2=d.boneIndices[i*4+2], boneIndex3=d.boneIndices[i*4+3], weight0=d.boneWeights[i*4],
                weight1=d.boneWeights[i*4+1], weight2=d.boneWeights[i*4+2], weight3=d.boneWeights[i*4+3] };
        }
        foreach (int index in d.triangles) if ((uint)index >= n) throw new Exception("Invalid triangle: " + stem);
        for (int i = 0; i < 16; i++) for (int r = 0; r < 4; r++) for (int c = 0; c < 4; c++)
        {
            float value = d.bindposes[i*16+r*4+c];
            if (!float.IsFinite(value) || (referenceBind != null && Mathf.Abs(value-referenceBind[i][r,c]) > .00001f))
                throw new Exception("Variant changed native bind pose: " + stem);
            bind[i][r,c] = value;
        }
        if (referenceBind == null) referenceBind = bind;
        string path = stem + ".asset";
        AssetDatabase.DeleteAsset(path);
        var mesh = new UnityEngine.Mesh { name = "pilot-first-person" + suffix, vertices = vertices, normals = normals,
            tangents = tangents, uv = uv, boneWeights = weights, bindposes = bind, triangles = d.triangles };
        mesh.RecalculateBounds(); AssetDatabase.CreateAsset(mesh, path);
        mesh.UploadMeshData(true); EditorUtility.SetDirty(mesh); paths.Add(path);
        checks.Add(mesh.name + ": " + n + " vertices / " + d.triangles.Length/3 + " triangles / 16 identical bind poses; readable=" + mesh.isReadable);
    }

    private static void Texture(string name, bool normal, bool mask)
    {
        string path = "Assets/PilotShader/body/" + name + ".png";
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null) throw new Exception("Missing texture: " + path);
        importer.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
        importer.sRGBTexture = !normal && !mask; importer.mipmapEnabled = true;
        importer.anisoLevel = mask ? 1 : 4; importer.isReadable = false; importer.maxTextureSize = 2048;
        var platform = importer.GetPlatformTextureSettings("Standalone"); platform.overridden = true;
        platform.maxTextureSize = importer.maxTextureSize; platform.format = normal ? TextureImporterFormat.BC5 : TextureImporterFormat.DXT1;
        importer.SetPlatformTextureSettings(platform); importer.SaveAndReimport(); paths.Add(path);
    }
}
#endif
