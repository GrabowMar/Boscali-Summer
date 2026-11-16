using System;
using UnityEngine;
using UnityEngine.Rendering;

// GPU comparison against the original copy/composite path, using the built player shader.
public static class StreakParityCheck
{
    public static string Run(Material material)
    {
        if (material.passCount != 4 || !material.shader.isSupported)
            throw new Exception("Built streak shader did not retain four supported passes");
        if (Array.IndexOf(Environment.GetCommandLineArgs(), "-skipAgxRenderCheck") >= 0)
            return "streak parity skipped; built shader retains four supported passes";
        var edgeShader = UnityEditor.AssetDatabase.LoadAssetAtPath<Shader>("Assets/Editor/StreakParity.shader");
        if (edgeShader == null || UnityEditor.ShaderUtil.ShaderHasError(edgeShader))
            throw new Exception("MSAA coverage fixture shader failed compilation");
        var edge = new Material(edgeShader);
        try
        {
            string report = "streak built-bundle GPU parity (" + SystemInfo.graphicsDeviceType + "):\n";
            bool passed = true;
            foreach (RenderTextureFormat format in new[] { RenderTextureFormat.ARGBHalf, RenderTextureFormat.RGB111110Float })
                foreach (int samples in new[] { 1, 8 })
                    foreach (int width in new[] { 256, 260 })
                        foreach (float strength in new[] { 0f, 0.5f })
                            try { report += Compare(material, edge, width, 68, samples, strength, format) + "\n"; }
                            catch (Exception error) { passed = false; report += "FAIL: " + error.Message + "\n"; }
            foreach (RenderTextureFormat format in new[] { RenderTextureFormat.ARGBHalf, RenderTextureFormat.RGB111110Float })
                try { report += Compare(material, edge, 1920, 1080, 8, 0.5f, format) + "\n"; }
                catch (Exception error) { passed = false; report += "FAIL: " + error.Message + "\n"; }
            System.IO.File.WriteAllText("streak-parity-measurements.txt", report);
            if (!passed) throw new Exception(report);
            return report;
        }
        finally { UnityEngine.Object.DestroyImmediate(edge); }
    }

    private static string Compare(Material material, Material edge, int width, int height, int samples, float strength, RenderTextureFormat format)
    {
        var descriptor = new RenderTextureDescriptor(width, height, format, 0)
        { msaaSamples = samples, bindMS = false, sRGB = false };
        if (SystemInfo.GetRenderTextureSupportedMSAASampleCount(descriptor) < samples)
            throw new Exception("GPU cannot validate requested MSAA " + samples);
        var baseline = new RenderTexture(descriptor) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        var optimized = new RenderTexture(descriptor) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        baseline.Create(); optimized.Create();
        if (baseline.antiAliasing != samples || optimized.antiAliasing != samples)
            throw new Exception("Requested MSAA was silently reduced");
        descriptor.msaaSamples = 1;
        var frame = RenderTexture.GetTemporary(descriptor);
        var readTarget = RenderTexture.GetTemporary(descriptor);
        descriptor.width = (width + 3) / 4; descriptor.height = (height + 3) / 4;
        var extract = RenderTexture.GetTemporary(descriptor);
        var blur = RenderTexture.GetTemporary(descriptor);
        frame.filterMode = extract.filterMode = blur.filterMode = FilterMode.Bilinear;
        var source = new Texture2D(width, height, TextureFormat.RGBAFloat, false, true);
        source.filterMode = FilterMode.Bilinear;
        var input = new Color[width * height];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                // Asymmetric RGB and alpha detect vertical inversion and lost destination data.
                input[y * width + x] = new Color(0.15f + x / (float)width,
                    0.2f + y / (float)height, 0.3f, 0.1f + 0.8f * y / height);
                if (x > width * 3 / 4 && x < width * 3 / 4 + 5 && y > 8 && y < 15)
                    input[y * width + x] = new Color(8f, 16f, 32f, 0.63f);
            }
        source.SetPixels(input); source.Apply();
        var read = new Texture2D(width, height, TextureFormat.RGBAFloat, false, true);
        RenderTexture savedTarget = RenderTexture.active;
        bool savedSrgb = GL.sRGBWrite;
        var cmd = new CommandBuffer { name = "Streak parity" };
        try
        {
            GL.sRGBWrite = false;
            foreach (RenderTexture target in new[] { baseline, optimized })
            {
                cmd.Blit(source, target);
                cmd.SetRenderTarget(target, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store);
                cmd.SetViewport(new Rect(0, 0, width, height));
                cmd.DrawProcedural(Matrix4x4.identity, edge, 0, MeshTopology.Triangles, 3);
            }
            Graphics.ExecuteCommandBuffer(cmd); cmd.Clear();
            Color[] original = Read(optimized, readTarget, read, cmd);
            material.SetVector("_StreakParams", new Vector4(4f, 32f, strength, 1.5f));
            // Existing path, including its full-resolution HDR copy.
            cmd.Blit(baseline, frame);
            Graphics.ExecuteCommandBuffer(cmd); cmd.Clear();
            Color[] copied = Read(frame, readTarget, read, cmd);
            float copyError = 0;
            for (int i = 0; i < copied.Length; i++)
                for (int channel = 0; channel < 3; channel++)
                    copyError = Mathf.Max(copyError, Mathf.Abs(copied[i][channel] - original[i][channel]));
            cmd.Blit(frame, extract, material, 0);
            Graphics.ExecuteCommandBuffer(cmd); cmd.Clear();
            Color[] oldExtract = Read(extract, readTarget, read, cmd);
            cmd.Blit(extract, blur, material, 1);
            cmd.SetGlobalTexture("_BoscaliStreakTex", blur);
            cmd.Blit(frame, baseline, material, 2);
            Graphics.ExecuteCommandBuffer(cmd); cmd.Clear();
            Color[] expected = Read(baseline, readTarget, read, cmd);
            // Same commands as CinematicStreakPass's optional direct branch.
            if (optimized.antiAliasing > 1) cmd.ResolveAntiAliasedSurface(optimized);
            cmd.Blit(optimized, extract, material, 0);
            Graphics.ExecuteCommandBuffer(cmd); cmd.Clear();
            Color[] newExtract = Read(extract, readTarget, read, cmd);
            float extractError = 0;
            for (int i = 0; i < newExtract.Length; i++)
                for (int channel = 0; channel < 3; channel++)
                    extractError = Mathf.Max(extractError, Mathf.Abs(newExtract[i][channel] - oldExtract[i][channel]));
            cmd.Blit(extract, blur, material, 1);
            cmd.SetGlobalTexture("_BoscaliStreakTex", blur);
            cmd.SetRenderTarget(optimized, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store);
            cmd.SetViewport(new Rect(0, 0, width, height));
            cmd.DrawProcedural(Matrix4x4.identity, material, 3, MeshTopology.Triangles, 3);
            Graphics.ExecuteCommandBuffer(cmd); cmd.Clear();
            Color[] actual = Read(optimized, readTarget, read, cmd);
            double sum = 0;
            float maximum = 0, maxUlp = 0, alphaError = 0, added = 0;
            for (int i = 0; i < actual.Length; i++)
            {
                for (int channel = 0; channel < 4; channel++)
                {
                    float value = actual[i][channel];
                    if (float.IsNaN(value) || float.IsInfinity(value)) throw new Exception("Non-finite streak output");
                    float delta = Mathf.Abs(value - expected[i][channel]);
                    maximum = Mathf.Max(maximum, delta); sum += delta;
                    if (channel < 3)
                    {
                        int bits = format == RenderTextureFormat.ARGBHalf ? 10 : channel == 2 ? 5 : 6;
                        float ulp = Mathf.Pow(2f, Mathf.Floor(Mathf.Log(Mathf.Max(0.00006103515625f,
                            Mathf.Abs(expected[i][channel])), 2f)) - bits);
                        maxUlp = Mathf.Max(maxUlp, delta / ulp);
                    }
                }
                alphaError = Mathf.Max(alphaError, Mathf.Abs(actual[i].a - original[i].a));
                added = Mathf.Max(added, actual[i].r - original[i].r, actual[i].b - original[i].b);
            }
            double mean = sum / (actual.Length * 4);
            // Interpolation and blend/resolve round at different stages. Allow two
            // representable steps, never a spatially shifted or missing streak.
            if (maxUlp > 2.01f || alphaError > 0.001f ||
                (strength > 0f ? added < 0.05f : maximum > 0.001f))
                throw new Exception("Streak parity failed: format=" + format + " width=" + width + " samples=" + samples + " max=" + maximum +
                    " ULP=" + maxUlp + " mean=" + mean + " alpha=" + alphaError + " copy=" + copyError + " extract=" + extractError + " added=" + added);
            return format + " " + width + "x" + height + " MSAA" + samples + " strength=" + strength +
                " max=" + maximum.ToString("G6") + " mean=" + mean.ToString("G6") +
                " ULP=" + maxUlp.ToString("G6") + " alpha=" + alphaError.ToString("G6") +
                " copy=" + copyError.ToString("G6") + " extract=" + extractError.ToString("G6") + " added=" + added.ToString("G6");
        }
        finally
        {
            cmd.Release(); RenderTexture.active = savedTarget; GL.sRGBWrite = savedSrgb;
            UnityEngine.Object.DestroyImmediate(source); UnityEngine.Object.DestroyImmediate(read);
            baseline.Release(); optimized.Release();
            UnityEngine.Object.DestroyImmediate(baseline); UnityEngine.Object.DestroyImmediate(optimized);
            RenderTexture.ReleaseTemporary(frame); RenderTexture.ReleaseTemporary(readTarget);
            RenderTexture.ReleaseTemporary(extract); RenderTexture.ReleaseTemporary(blur);
        }
    }

    private static Color[] Read(RenderTexture source, RenderTexture target, Texture2D read, CommandBuffer cmd)
    {
        if (source.antiAliasing > 1) cmd.ResolveAntiAliasedSurface(source);
        cmd.Blit(source, target);
        Graphics.ExecuteCommandBuffer(cmd); cmd.Clear();
        RenderTexture.active = target;
        read.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); read.Apply();
        return read.GetPixels();
    }
}
