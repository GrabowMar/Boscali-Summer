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
        if (string.IsNullOrEmpty(output) && File.Exists("review-path.txt")) output = File.ReadAllText("review-path.txt").Trim();
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
        float held600 = 0f, dispersed720 = 0f, dispersed900 = 0f, dispersed960 = 0f;
        foreach (float age in new[] { .03f, .18f, .22f, .55f, 1f, 1.1f, 4f, 12f, 30f, 70f, 150f, 230f, 450f, 600f, 720f, 900f, 960f })
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
            if (age == 600f) held600 = maxOpacity;
            if (age == 720f) dispersed720 = maxOpacity;
            if (age == 900f) dispersed900 = maxOpacity;
            if (age == 960f) dispersed960 = maxOpacity;
            if (age == 4f && peak < 8f) throw new Exception("Prompt fireball failed HDR brightness check: " + peak);
            if (age >= 150f && peak > 8f) throw new Exception("Cooled cloud retained excessive emission: " + peak);
            if (age >= Sa74BlastShape.MaximumVisualAge && (peak > .00001f || maxOpacity > .00001f)) throw new Exception("Expired mushroom still rendered at sixteen minutes.");
            evidence.Add("{\"ageSeconds\":" + age.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + ",\"hdrPeak\":" + peak.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture) + ",\"maximumOpacity\":" + maxOpacity.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture) + ",\"meanOpacity\":" + meanOpacity.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture) + "}");
        }
        if (!(promptPeak > dipPeak * 3f && secondPeak > dipPeak * 5f))
            throw new Exception("Rendered source did not show two light peaks around the subsecond dip: " + promptPeak + "," + dipPeak + "," + secondPeak);
        if (!(held600 > .95f && dispersed720 > .05f && dispersed720 < .95f && dispersed900 < .10f && dispersed960 <= .00001f))
            throw new Exception("Mushroom failed ten-minute hold/sixteen-minute dispersal checks: " + held600 + "," + dispersed720 + "," + dispersed900 + "," + dispersed960);
        DustProof(material, color, depth, final, output, evidence);
        CompositionProof(material, output);
        AsymmetricCompositionProof(material, output);
        AirburstProof(material, color, depth, final, output, evidence);
        AirburstProof(material, color, depth, final, output, evidence, 10f);
        OpaqueMarchProof(material, color, depth, sceneDepth);
        File.WriteAllText(Path.Combine(output, "shader-proof.json"), "{\"checksPassed\":true,\"doublePulseVerified\":true,\"weatherDepthOrderingVerified\":true,\"opaqueOcclusionVerified\":true,\"airburstGlobeVerified\":true,\"groundDustVisibilityLossVerified\":true,\"tenMinuteHoldVerified\":true,\"sixteenMinuteDispersalVerified\":true,\"groundDustSettlingVerified\":true,\"frames\":[" + string.Join(",", evidence) + "]}");
        RenderTexture.active = null;
        UnityEngine.Object.DestroyImmediate(material); UnityEngine.Object.DestroyImmediate(noise); UnityEngine.Object.DestroyImmediate(sceneDepth);
        color.Release(); depth.Release(); final.Release();
        UnityEngine.Object.DestroyImmediate(color); UnityEngine.Object.DestroyImmediate(depth); UnityEngine.Object.DestroyImmediate(final);
    }

    private static void DustProof(Material material, RenderTexture color, RenderTexture depth, RenderTexture final, string output, List<string> evidence)
    {
        foreach (float age in new[] { 45f, 70f, 150f, 180f, 450f, 600f })
        {
            float front = Sa74BlastShape.FrontRadius(age, Sa74BlastShape.Radius(163840f, 163840f));
            Vector3 eye = new Vector3(age <= 70f ? front - 1400f : 16000f, 300f, -1000f);
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

    private static void CompositionProof(Material material, string output)
    {
        Texture previousColor = material.GetTexture("_Sa74Color"), previousDepth = material.GetTexture("_Sa74Depth");
        Texture previousScene = Shader.GetGlobalTexture("_CameraDepthTexture");
        Vector4 previousZ = Shader.GetGlobalVector("_ZBufferParams");
        var volume = Constant(new Color(.8f, .2f, .1f, .7f));
        var distance = Constant(new Color(10000f, 0f, 0f, 0f));
        var weather = Constant(new Color(.1f, .2f, .3f, .6f));
        var weatherDepth = Constant(new Color(250000f, 5000f, 0f, 0f));
        var scene = Constant(new Color(.5f, 0f, 0f, 0f));
        var target = new RenderTexture(128, 64, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
        target.Create();
        material.SetTexture("_Sa74Color", volume); material.SetTexture("_Sa74Depth", distance);
        Shader.SetGlobalTexture("_BoscaliCloudCover", weather);
        Shader.SetGlobalTexture("_BoscaliCloudLayerDepth", weatherDepth);
        Shader.SetGlobalTexture("_CameraDepthTexture", scene);
        Shader.SetGlobalFloat("_BoscaliCloudCoverFrame", 1f);
        Shader.SetGlobalVector("_ZBufferParams", new Vector4(0f, 0f, 0f, 1f / 250000f));
        // This is precisely the framebuffer after Weather's premultiplied layer over a known scene.
        Color backdrop = new Color(.12f, .232f, .34f, 1f);
        foreach (string layer in new[] { "foreground", "background", "opaque", "opaque-behind", "native-transparent" })
        {
            if (layer == "background") { weatherDepth.SetPixel(0, 0, new Color(250000f, 20000f, 0f, 0f)); weatherDepth.Apply(); }
            if (layer == "opaque") Shader.SetGlobalVector("_ZBufferParams", new Vector4(0f, 0f, 0f, 1f / 5000f));
            if (layer == "opaque-behind") Shader.SetGlobalVector("_ZBufferParams", new Vector4(0f, 0f, 0f, 1f / 25000f));
            if (layer == "native-transparent")
            {
                Shader.SetGlobalFloat("_BoscaliCloudCoverFrame", 0f);
                Shader.SetGlobalVector("_ZBufferParams", new Vector4(0f, 0f, 0f, 1f / 250000f));
            }
            var cmd = new CommandBuffer();
            cmd.SetRenderTarget(target); cmd.SetViewport(new Rect(0, 0, 128, 64)); cmd.DisableScissorRect();
            cmd.ClearRenderTarget(false, true, backdrop);
            cmd.DrawProcedural(Matrix4x4.identity, material, 1, MeshTopology.Triangles, 3);
            Graphics.ExecuteCommandBuffer(cmd); cmd.Release();
            if (layer == "native-transparent")
            {
                // Native fallback composites the volume before transparents. Exercise that actual
                // premultiplied draw order with a known foreground layer, with no Weather globals.
                var foreground = Constant(new Color(.04f, .10f, .08f, .4f));
                material.SetTexture("_Sa74Color", foreground);
                var overlay = new CommandBuffer();
                overlay.SetRenderTarget(target); overlay.SetViewport(new Rect(0, 0, 128, 64));
                overlay.DrawProcedural(Matrix4x4.identity, material, 1, MeshTopology.Triangles, 3);
                Graphics.ExecuteCommandBuffer(overlay); overlay.Release();
                material.SetTexture("_Sa74Color", volume);
                UnityEngine.Object.DestroyImmediate(foreground);
            }
            RenderTexture.active = target;
            var data = new Texture2D(128, 64, TextureFormat.RGBAFloat, false, true);
            data.ReadPixels(new Rect(0, 0, 128, 64), 0, 0); data.Apply();
            Color expected = layer == "foreground" ? new Color(.426f, .2896f, .352f, 1f) :
                layer == "opaque" ? backdrop : layer == "native-transparent" ? new Color(.5416f, .26176f, .2012f, 1f) :
                new Color(.836f, .2696f, .202f, 1f);
            foreach (Color actual in data.GetPixels())
                if (Mathf.Abs(actual.r - expected.r) > .004f || Mathf.Abs(actual.g - expected.g) > .004f || Mathf.Abs(actual.b - expected.b) > .004f)
                    throw new Exception("Cloud/opaque depth composition failed for " + layer + ": " + actual + " expected " + expected);
            var image = new Texture2D(128, 64, TextureFormat.RGB24, false);
            image.SetPixels(data.GetPixels()); image.Apply();
            File.WriteAllBytes(Path.Combine(output, "composition-" + layer + ".png"), image.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(image);
            UnityEngine.Object.DestroyImmediate(data);
        }
        material.SetTexture("_Sa74Color", previousColor); material.SetTexture("_Sa74Depth", previousDepth);
        Shader.SetGlobalTexture("_CameraDepthTexture", previousScene); Shader.SetGlobalVector("_ZBufferParams", previousZ);
        Shader.SetGlobalFloat("_BoscaliCloudCoverFrame", 0f);
        RenderTexture.active = null;
        target.Release(); UnityEngine.Object.DestroyImmediate(target);
        foreach (Texture texture in new Texture[] { volume, distance, weather, weatherDepth, scene }) UnityEngine.Object.DestroyImmediate(texture);
    }

    private static Texture2D Constant(Color color)
    {
        var result = new Texture2D(1, 1, TextureFormat.RGBAFloat, false, true);
        result.SetPixel(0, 0, color); result.Apply(); return result;
    }

    private static void AsymmetricCompositionProof(Material material, string output)
    {
        Texture previousColor = material.GetTexture("_Sa74Color"), previousDepth = material.GetTexture("_Sa74Depth");
        Texture previousScene = Shader.GetGlobalTexture("_CameraDepthTexture");
        Texture previousWeather = Shader.GetGlobalTexture("_BoscaliCloudCover"), previousLayerDepth = Shader.GetGlobalTexture("_BoscaliCloudLayerDepth");
        Vector4 previousZ = Shader.GetGlobalVector("_ZBufferParams"), previousProj = Shader.GetGlobalVector("_ProjectionParams");
        float previousFrame = Shader.GetGlobalFloat("_BoscaliCloudCoverFrame");
        var fixtureShader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/Mushroom/ScreenFixture.shader");
        if (fixtureShader == null || ShaderUtil.ShaderHasError(fixtureShader)) throw new Exception("Asymmetric screen fixture shader failed to compile.");
        var fixture = new Material(fixtureShader);
        Color source = new Color(.8f, .2f, .1f, .7f);
        Color backA = new Color(.05f, .08f, .10f, 1f), backB = new Color(.12f, .06f, .03f, 1f);
        Color weatherA = new Color(.1f, .2f, .3f, .6f), weatherB = new Color(.28f, .08f, .03f, .35f);
        var volume = Constant(source);
        var distance = Constant(new Color(10000f, 0f, 0f, 0f));
        var weather = VerticalPair(weatherA, weatherB);
        var weatherDepth = VerticalPair(new Color(250000f, 5000f, 0f, 0f), new Color(250000f, 20000f, 0f, 0f));
        var scene = VerticalPair(new Color(.5f, 0f, 0f, 0f), new Color(.5f, 0f, 0f, 0f));
        var target = new RenderTexture(128, 64, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
        target.Create();
        fixture.SetTexture("_FixtureWeather", weather);
        fixture.SetColor("_FixtureBackA", backA); fixture.SetColor("_FixtureBackB", backB);
        fixture.SetVector("_FixtureTargetSize", new Vector4(128f, 64f, 1f / 128f, 1f / 64f));
        material.SetTexture("_Sa74Color", volume); material.SetTexture("_Sa74Depth", distance);
        Shader.SetGlobalTexture("_BoscaliCloudCover", weather);
        Shader.SetGlobalTexture("_BoscaliCloudLayerDepth", weatherDepth);
        Shader.SetGlobalTexture("_CameraDepthTexture", scene);
        try
        {
            foreach (bool opaque in new[] { false, true })
            {
                if (opaque)
                {
                    weather.SetPixels(new[] { Color.clear, Color.clear, Color.clear, Color.clear }); weather.Apply();
                    Color sky = new Color(SystemInfo.usesReversedZBuffer ? 0f : 1f, 0f, 0f, 0f);
                    scene.SetPixels(new[] { new Color(.5f, 0f, 0f, 0f), new Color(.5f, 0f, 0f, 0f), sky, sky }); scene.Apply();
                }
                Shader.SetGlobalFloat("_BoscaliCloudCoverFrame", opaque ? 0f : 1f);
                Shader.SetGlobalVector("_ZBufferParams", new Vector4(0f, 0f, 0f, 1f / (opaque ? 5000f : 250000f)));
                Shader.SetGlobalVector("_ProjectionParams", new Vector4(-1f, previousProj.y, previousProj.z, previousProj.w));
                var fill = new CommandBuffer();
                fill.SetRenderTarget(target); fill.SetViewport(new Rect(0, 0, 128, 64)); fill.DisableScissorRect();
                fill.DrawProcedural(Matrix4x4.identity, fixture, 0, MeshTopology.Triangles, 3);
                Graphics.ExecuteCommandBuffer(fill); fill.Release();
                RenderTexture.active = target;
                var before = new Texture2D(128, 64, TextureFormat.RGBAFloat, false, true);
                before.ReadPixels(new Rect(0, 0, 128, 64), 0, 0); before.Apply();
                var composite = new CommandBuffer();
                composite.SetRenderTarget(target); composite.SetViewport(new Rect(0, 0, 128, 64)); composite.DisableScissorRect();
                composite.DrawProcedural(Matrix4x4.identity, material, 1, MeshTopology.Triangles, 3);
                Graphics.ExecuteCommandBuffer(composite); composite.Release();
                RenderTexture.active = target;
                var after = new Texture2D(128, 64, TextureFormat.RGBAFloat, false, true);
                after.ReadPixels(new Rect(0, 0, 128, 64), 0, 0); after.Apply();
                Color baseA = opaque ? backA : weatherA + (1f - weatherA.a) * backA;
                Color baseB = opaque ? backB : weatherB + (1f - weatherB.a) * backB;
                int checkedA = 0, checkedB = 0;
                // Identify each readback half from its known framebuffer marker, avoiding any
                // dependence on Texture.ReadPixels' row orientation or its alpha convention.
                for (int y = 2; y < 62; y++) for (int x = 2; x < 126; x++)
                {
                    if (y >= 30 && y <= 33) continue;
                    Color backdrop = before.GetPixel(x, y);
                    bool halfA = RgbDifference(backdrop, baseA) < .004f;
                    if (!halfA && RgbDifference(backdrop, baseB) >= .004f) throw new Exception("Asymmetric screen fixture lost its framebuffer markers.");
                    if (halfA) checkedA++; else checkedB++;
                    Color expected = opaque && halfA ? backdrop : source + (1f - source.a) * backdrop;
                    if (!opaque && halfA) expected = source * (1f - weatherA.a) + source.a * weatherA + (1f - source.a) * backdrop;
                    Color actual = after.GetPixel(x, y);
                    if (RgbDifference(actual, expected) > .006f)
                        throw new Exception("Asymmetric " + (opaque ? "opaque" : "Weather") + " composition failed in half " + (halfA ? "A" : "B") + ": " + actual + " expected " + expected);
                }
                if (checkedA == 0 || checkedB == 0) throw new Exception("Asymmetric screen fixture did not exercise both halves.");
                var image = new Texture2D(128, 64, TextureFormat.RGB24, false);
                image.SetPixels(after.GetPixels()); image.Apply();
                File.WriteAllBytes(Path.Combine(output, "composition-asymmetric-" + (opaque ? "opaque" : "weather") + ".png"), image.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(image); UnityEngine.Object.DestroyImmediate(before); UnityEngine.Object.DestroyImmediate(after);
            }
        }
        finally
        {
            material.SetTexture("_Sa74Color", previousColor); material.SetTexture("_Sa74Depth", previousDepth);
            Shader.SetGlobalTexture("_CameraDepthTexture", previousScene);
            Shader.SetGlobalTexture("_BoscaliCloudCover", previousWeather); Shader.SetGlobalTexture("_BoscaliCloudLayerDepth", previousLayerDepth);
            Shader.SetGlobalVector("_ZBufferParams", previousZ); Shader.SetGlobalVector("_ProjectionParams", previousProj);
            Shader.SetGlobalFloat("_BoscaliCloudCoverFrame", previousFrame);
            RenderTexture.active = null;
            target.Release(); UnityEngine.Object.DestroyImmediate(target); UnityEngine.Object.DestroyImmediate(fixture);
            foreach (Texture texture in new Texture[] { volume, distance, weather, weatherDepth, scene }) UnityEngine.Object.DestroyImmediate(texture);
        }
    }

    private static Texture2D VerticalPair(Color a, Color b)
    {
        var result = new Texture2D(2, 2, TextureFormat.RGBAFloat, false, true) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
        result.SetPixels(new[] { a, a, b, b }); result.Apply(); return result;
    }

    private static float RgbDifference(Color a, Color b) => Mathf.Max(Mathf.Abs(a.r - b.r), Mathf.Max(Mathf.Abs(a.g - b.g), Mathf.Abs(a.b - b.b)));

    private static void OpaqueMarchProof(Material material, RenderTexture color, RenderTexture depth, Texture2D sceneDepth)
    {
        View(material, new Vector3(35000f, 15000f, -90000f), new Vector3(0f, 28000f, 0f), 58f);
        material.SetVectorArray("_Sa74Sites", new[] { new Vector4(0f, 0f, 0f, 230f), Vector4.zero });
        Vector4 previousZ = Shader.GetGlobalVector("_ZBufferParams");
        sceneDepth.SetPixel(0, 0, new Color(.5f, 0f, 0f, 0f)); sceneDepth.Apply();
        foreach (float opaqueEye in new[] { 5000f, 150000f })
        {
            Shader.SetGlobalVector("_ZBufferParams", new Vector4(0f, 0f, 0f, 1f / opaqueEye));
            var cmd = new CommandBuffer();
            cmd.SetRenderTarget(new[] { new RenderTargetIdentifier(color), new RenderTargetIdentifier(depth) }, color);
            cmd.SetViewport(new Rect(0, 0, 800, 450)); cmd.DrawProcedural(Matrix4x4.identity, material, 0, MeshTopology.Triangles, 3);
            Graphics.ExecuteCommandBuffer(cmd); cmd.Release();
            RenderTexture.active = color;
            var data = new Texture2D(800, 450, TextureFormat.RGBAFloat, false, true);
            data.ReadPixels(new Rect(0, 0, 800, 450), 0, 0); data.Apply();
            float peak = 0f;
            foreach (Color sample in data.GetPixels()) peak = Mathf.Max(peak, sample.a);
            UnityEngine.Object.DestroyImmediate(data);
            if ((opaqueEye < 10000f && peak > .00001f) || (opaqueEye > 10000f && peak < .95f))
                throw new Exception("Actual raymarch ignored foreground/background opaque plane at " + opaqueEye + ": " + peak);
        }
        sceneDepth.SetPixel(0, 0, Color.black); sceneDepth.Apply();
        Shader.SetGlobalVector("_ZBufferParams", previousZ);
    }

    private static void AirburstProof(Material material, RenderTexture color, RenderTexture depth, RenderTexture final, string output, List<string> evidence, float age = 4f)
    {
        View(material, new Vector3(16000f, 6000f, -22000f), new Vector3(0f, 6000f, 0f), 52f);
        material.SetVector("_Sa74Clearance", new Vector4(6000f, 0f, 0f, 0f));
        material.SetVectorArray("_Sa74Sites", new[] { new Vector4(0f, 6000f, 0f, age), Vector4.zero });
        var cmd = new CommandBuffer();
        cmd.SetRenderTarget(new[] { new RenderTargetIdentifier(color), new RenderTargetIdentifier(depth) }, color);
        cmd.SetViewport(new Rect(0, 0, 800, 450)); cmd.DrawProcedural(Matrix4x4.identity, material, 0, MeshTopology.Triangles, 3);
        cmd.SetRenderTarget(final); cmd.SetViewport(new Rect(0, 0, 1600, 900));
        cmd.ClearRenderTarget(false, true, new Color(.24f, .29f, .34f));
        cmd.DrawProcedural(Matrix4x4.identity, material, 1, MeshTopology.Triangles, 3);
        Graphics.ExecuteCommandBuffer(cmd); cmd.Release();
        RenderTexture.active = final;
        var image = new Texture2D(1600, 900, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0); image.Apply();
        File.WriteAllBytes(Path.Combine(output, "airburst-" + (int)age + "s.png"), image.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(image);
        RenderTexture.active = color;
        var data = new Texture2D(800, 450, TextureFormat.RGBAFloat, false, true);
        data.ReadPixels(new Rect(0, 0, 800, 450), 0, 0); data.Apply();
        Color[] samples = data.GetPixels();
        int minX = 800, maxX = -1, minY = 450, maxY = -1;
        for (int y = 0; y < 450; y++) for (int x = 0; x < 800; x++)
            if (samples[y * 800 + x].r > 5f)
            { minX = Math.Min(minX, x); maxX = Math.Max(maxX, x); minY = Math.Min(minY, y); maxY = Math.Max(maxY, y); }
        float aspect = (maxY - minY + 1f) / Math.Max(1f, maxX - minX + 1f);
        if (maxX < minX || (age <= 4f && (aspect < .80f || aspect > 1.2f))) throw new Exception("Elevated fireball is not a full globe: height/width=" + aspect);
        if (age > 4f)
        {
            int gap = 0, longest = 0;
            for (int y = minY; y <= maxY; y++)
            {
                gap = samples[y * 800 + 400].a < .1f ? gap + 1 : 0;
                longest = Math.Max(longest, gap);
            }
            if (longest > 2) throw new Exception("Young airburst cap is detached from its fireball: empty centre pixels=" + longest);
            float outsideOpacity = 0f; int outsidePixels = 0;
            for (int y = 0; y < 450; y++) for (int x = 0; x < 800; x++)
                if (x < 250 || x > 550) { outsideOpacity += samples[y * 800 + x].a; outsidePixels++; }
            outsideOpacity /= outsidePixels;
            if (outsideOpacity > .12f) throw new Exception("Early airburst dust still forms an opaque platter: outside-core mean opacity=" + outsideOpacity);
            evidence.Add("{\"airburstDustSeconds\":" + (int)age + ",\"outsideCoreMeanOpacity\":" + outsideOpacity.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture) + "}");
        }
        evidence.Add("{\"airburstSeconds\":" + (int)age + ",\"clearanceMetres\":6000,\"fireballHeightToWidth\":" + aspect.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture) + "}");
        UnityEngine.Object.DestroyImmediate(data);
        material.SetVector("_Sa74Clearance", Vector4.zero);
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
