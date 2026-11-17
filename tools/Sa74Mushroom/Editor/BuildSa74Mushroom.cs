using System;
using System.Collections.Generic;
using System.IO;
using BoscaliSummer.Modules.Weather.Visuals;
using BoscaliSummer.Modules.Vanguard.Domain;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static class BuildSa74Mushroom
{
    public static void Run()
    {
        try
        {
            const string source = "Assets/Mushroom/Sa74Mushroom.shader";
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(source);
            if (shader == null || ShaderUtil.ShaderHasError(shader)) throw new Exception("SA-74N mushroom shader failed to compile.");
            var build = new AssetBundleBuild { assetBundleName = "sa74-mushroom.bundle", assetNames = new[] { source } };
            if (BuildPipeline.BuildAssetBundles("BundleOutput", new[] { build }, BuildAssetBundleOptions.ChunkBasedCompression,
                BuildTarget.StandaloneWindows64) == null || ShaderUtil.ShaderHasError(shader)) throw new Exception("SA-74N mushroom bundle build failed.");
            Preview(shader);
            File.WriteAllText("result.txt", "PASS: production SA-74N fireball and cloud compiled, bundled and rendered across prompt, rising and cooled stages.");
            EditorApplication.Exit(0);
        }
        catch (Exception error)
        {
            Debug.LogException(error);
            File.WriteAllText("result.txt", "FAIL: " + error);
            EditorApplication.Exit(1);
        }
    }

    private static void Preview(Shader shader)
    {
        string output = Environment.GetEnvironmentVariable("SA74_REVIEW");
        if (string.IsNullOrEmpty(output)) return;
        Directory.CreateDirectory(output);
        var material = new Material(shader);
        var noise = new Texture3D(64, 64, 64, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear };
        noise.SetPixelData(CloudNoise3D.Generate(64, 47), 0);
        noise.Apply(true, false);
        material.SetTexture("_Sa74Noise", noise);
        var color = new RenderTexture(800, 450, 0, RenderTextureFormat.ARGBHalf);
        var depth = new RenderTexture(800, 450, 0, RenderTextureFormat.RFloat);
        var final = new RenderTexture(1600, 900, 0, RenderTextureFormat.ARGB32);
        color.Create(); depth.Create(); final.Create();
        var sceneDepth = new Texture2D(1, 1, TextureFormat.RFloat, false, true);
        sceneDepth.SetPixel(0, 0, Color.black); sceneDepth.Apply();
        Shader.SetGlobalTexture("_CameraDepthTexture", sceneDepth);
        Shader.SetGlobalVector("_ZBufferParams", new Vector4(0f, 0f, 0f, .000001f));
        material.SetVector("_Sa74Size", new Vector4(800f, 450f, 1f / 800f, 1f / 450f));
        material.SetTexture("_Sa74Color", color); material.SetTexture("_Sa74Depth", depth);
        material.SetInt("_Sa74Count", 1);
        material.SetFloat("_Sa74ShockRadius", Sa74BlastShape.Radius(163840f, 163840f));
        material.SetVector("_Sa74Sun", new Vector3(.60f, .55f, -.55f).normalized);
        material.SetColor("_Sa74SunColor", new Color(.93f, .96f, 1f));
        material.SetColor("_Sa74Fog", new Color(.34f, .39f, .43f));
        var evidence = new List<string>();
        float promptPeak = 0f, dipPeak = 0f, secondPeak = 0f;
        float dispersed450 = 0f, dispersed540 = 0f, dispersed600 = 0f;
        foreach (float age in new[] { .03f, .18f, .22f, .55f, 1f, 1.1f, 4f, 12f, 30f, 70f, 150f, 230f, 420f, 450f, 540f, 600f })
        {
            if (age <= 12f) View(material, new Vector3(16000f, 5000f, -22000f), new Vector3(0f, 3000f, 0f), 52f);
            else if (age < 70f) View(material, new Vector3(25000f, 10000f, -50000f), new Vector3(0f, 15000f, 0f), 58f);
            else View(material, new Vector3(35000f, 15000f, -90000f), new Vector3(0f, 28000f, 0f), 58f);
            material.SetVectorArray("_Sa74Sites", new[] { new Vector4(0f, 0f, 0f, age), Vector4.zero });
            var commands = new CommandBuffer();
            commands.SetRenderTarget(new[] { new RenderTargetIdentifier(color), new RenderTargetIdentifier(depth) }, color);
            commands.SetViewport(new Rect(0, 0, 800, 450));
            commands.DrawProcedural(Matrix4x4.identity, material, 0, MeshTopology.Triangles, 3);
            commands.SetRenderTarget(final);
            commands.SetViewport(new Rect(0, 0, 1600, 900));
            commands.ClearRenderTarget(false, true, new Color(.24f, .29f, .34f));
            commands.DrawProcedural(Matrix4x4.identity, material, 1, MeshTopology.Triangles, 3);
            Graphics.ExecuteCommandBuffer(commands);
            commands.Release();
            RenderTexture.active = final;
            var image = new Texture2D(1600, 900, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0); image.Apply();
            File.WriteAllBytes(Path.Combine(output, "mushroom-" + age.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + "s.png"), image.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(image);
            RenderTexture.active = color;
            var hdr = new Texture2D(800, 450, TextureFormat.RGBAFloat, false, true);
            hdr.ReadPixels(new Rect(0, 0, 800, 450), 0, 0); hdr.Apply();
            float peak = 0f;
            float maxOpacity = 0f, meanOpacity = 0f;
            Color[] samples = hdr.GetPixels();
            foreach (Color sample in samples)
            {
                peak = Mathf.Max(peak, Mathf.Max(sample.r, Mathf.Max(sample.g, sample.b)));
                maxOpacity = Mathf.Max(maxOpacity, sample.a); meanOpacity += sample.a;
            }
            meanOpacity /= samples.Length;
            if (age <= 1.1f)
            {
                // Fixed reduced exposure reveals the source dip that ordinary 8-bit white clipping would conceal.
                for (int i = 0; i < samples.Length; i++)
                {
                    Color sample = samples[i] * .1f;
                    samples[i] = new Color(Mathf.LinearToGammaSpace(sample.r / (1f + sample.r)),
                        Mathf.LinearToGammaSpace(sample.g / (1f + sample.g)), Mathf.LinearToGammaSpace(sample.b / (1f + sample.b)), 1f);
                }
                var sourceView = new Texture2D(800, 450, TextureFormat.RGB24, false);
                sourceView.SetPixels(samples); sourceView.Apply();
                File.WriteAllBytes(Path.Combine(output, "pulse-source-" + age.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + "s.png"), sourceView.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(sourceView);
            }
            UnityEngine.Object.DestroyImmediate(hdr);
            if (age == .03f) promptPeak = peak;
            if (age == .22f) dipPeak = peak;
            if (age == 1.1f) secondPeak = peak;
            if (age == 450f) dispersed450 = maxOpacity;
            if (age == 540f) dispersed540 = maxOpacity;
            if (age == 600f) dispersed600 = maxOpacity;
            if (age == 4f && peak < 8f) throw new Exception("Prompt fireball failed HDR brightness check: " + peak);
            if (age >= 150f && peak > 8f) throw new Exception("Cooled cloud retained excessive emission: " + peak);
            if (age >= 600f && (peak > .00001f || maxOpacity > .00001f)) throw new Exception("Expired mushroom still rendered at ten minutes.");
            evidence.Add("{\"ageSeconds\":" + age.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + ",\"hdrPeak\":" + peak.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture) + ",\"maximumOpacity\":" + maxOpacity.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture) + ",\"meanOpacity\":" + meanOpacity.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture) + "}");
        }
        if (!(promptPeak > dipPeak * 3f && secondPeak > dipPeak * 5f))
            throw new Exception("Rendered source did not show two light peaks around the subsecond dip: " + promptPeak + "," + dipPeak + "," + secondPeak);
        if (!(dispersed450 > .05f && dispersed450 < .95f && dispersed540 < .27f && dispersed600 <= .00001f))
            throw new Exception("Mushroom failed dispersal opacity checks: " + dispersed450 + "," + dispersed540 + "," + dispersed600);
        DustProof(material, color, depth, final, output, evidence);
        File.WriteAllText(Path.Combine(output, "shader-proof.json"), "{\"checksPassed\":true,\"doublePulseVerified\":true,\"groundDustVisibilityLossVerified\":true,\"tenMinuteDispersalVerified\":true,\"groundDustSettlingVerified\":true,\"frames\":[" + string.Join(",", evidence) + "]}");
        RenderTexture.active = null;
        UnityEngine.Object.DestroyImmediate(material); UnityEngine.Object.DestroyImmediate(noise); UnityEngine.Object.DestroyImmediate(sceneDepth);
        color.Release(); depth.Release(); final.Release();
        UnityEngine.Object.DestroyImmediate(color); UnityEngine.Object.DestroyImmediate(depth); UnityEngine.Object.DestroyImmediate(final);
    }

    private static void DustProof(Material material, RenderTexture color, RenderTexture depth, RenderTexture final, string output, List<string> evidence)
    {
        foreach (float age in new[] { 45f, 70f, 150f, 180f, 450f, 600f })
        {
            Vector3 eye = new Vector3(16000f, 300f, -1000f);
            View(material, eye, age <= 70f ? new Vector3(0f, 1600f, 0f) : eye + new Vector3(16000f, 500f, 0f), 95f);
            material.SetVectorArray("_Sa74Sites", new[] { new Vector4(0f, 0f, 0f, age), Vector4.zero });
            var commands = new CommandBuffer();
            commands.SetRenderTarget(new[] { new RenderTargetIdentifier(color), new RenderTargetIdentifier(depth) }, color);
            commands.SetViewport(new Rect(0, 0, 800, 450));
            commands.DrawProcedural(Matrix4x4.identity, material, 0, MeshTopology.Triangles, 3);
            commands.SetRenderTarget(final);
            commands.SetViewport(new Rect(0, 0, 1600, 900));
            commands.ClearRenderTarget(false, true, new Color(.24f, .29f, .34f));
            commands.DrawProcedural(Matrix4x4.identity, material, 1, MeshTopology.Triangles, 3);
            Graphics.ExecuteCommandBuffer(commands); commands.Release();
            RenderTexture.active = final;
            var image = new Texture2D(1600, 900, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0); image.Apply();
            File.WriteAllBytes(Path.Combine(output, "dust-low-" + (int)age + "s.png"), image.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(image);
            RenderTexture.active = color;
            var data = new Texture2D(800, 450, TextureFormat.RGBAFloat, false, true);
            data.ReadPixels(new Rect(0, 0, 800, 450), 0, 0); data.Apply();
            Color[] samples = data.GetPixels();
            float alpha = 0f;
            // Both halves must contain a substantial near-field volume: a tiny central plume cannot pass this check.
            float lower = 0f, upper = 0f;
            for (int y = 0; y < 450; y++)
                for (int x = 0; x < 800; x++)
                    if (y < 225) lower += samples[y * 800 + x].a; else upper += samples[y * 800 + x].a;
            float lowerMean = lower / (800f * 225f), upperMean = upper / (800f * 225f);
            alpha = Mathf.Min(lowerMean, upperMean);
            UnityEngine.Object.DestroyImmediate(data);
            if (age <= 70f && alpha < .75f) throw new Exception("Ground dust failed near-field extinction check at " + age + " s: " + alpha);
            if (age >= 150f && Mathf.Max(lowerMean, upperMean) > .45f) throw new Exception("Settled dust still blankets the view at " + age + " s.");
            evidence.Add("{\"dustAgeSeconds\":" + (int)age + ",\"minimumHalfMeanOpacity\":" + alpha.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture) + ",\"maximumHalfMeanOpacity\":" + Mathf.Max(lowerMean, upperMean).ToString("0.####", System.Globalization.CultureInfo.InvariantCulture) + "}");
        }
    }

    private static void View(Material material, Vector3 camera, Vector3 aim, float fov)
    {
        Quaternion turn = Quaternion.LookRotation(aim - camera);
        Matrix4x4 inverseView = Matrix4x4.TRS(camera, turn, Vector3.one) * Matrix4x4.Scale(new Vector3(1f, 1f, -1f));
        Matrix4x4 inverseProjection = Matrix4x4.Perspective(fov, 1600f / 900f, .1f, 80000f).inverse;
        var rays = new Matrix4x4();
        for (int i = 0; i < 4; i++)
        {
            Vector3 corner = inverseProjection.MultiplyPoint(new Vector3((i & 1) == 0 ? -1f : 1f, i < 2 ? -1f : 1f, -1f));
            rays.SetRow(i, inverseView.MultiplyVector(corner / -corner.z));
        }
        material.SetMatrix("_Sa74Frustum", rays);
        material.SetVector("_Sa74Camera", camera);
    }
}
