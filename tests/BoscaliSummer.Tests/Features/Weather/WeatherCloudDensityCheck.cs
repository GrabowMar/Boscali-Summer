#if UNITY_2022_3_OR_NEWER
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using BoscaliSummer.Modules.Weather.Domain;
using BoscaliSummer.Modules.Weather.Visuals;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

// One tiny floating-point readback per bounded probe batch. Fixture only: no product pass,
// runtime readback, game types or persistent render-target allocation.
public sealed class WeatherCloudDensityCheck : MonoBehaviour
{
    private readonly StringBuilder log = new StringBuilder();
    private int samples;

#if UNITY_EDITOR
    public static void Build()
    {
        try
        {
            Shader shader = Resources.Load<Shader>("CloudDensityProbe");
            if (shader == null || ShaderUtil.ShaderHasError(shader)) throw new Exception("Density probe shader invalid");
            PlayerSettings.colorSpace = ColorSpace.Linear;
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            new GameObject("Cloud density check").AddComponent<WeatherCloudDensityCheck>();
            EditorSceneManager.SaveScene(scene, "Assets/check.unity");
            AssetDatabase.SaveAssets();
            var report = BuildPipeline.BuildPlayer(new[] { "Assets/check.unity" }, "Player/WeatherCloudDensityCheck.exe",
                BuildTarget.StandaloneWindows64, BuildOptions.None);
            if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
                throw new Exception("Player build failed: " + report.summary.result);
            File.WriteAllText("build-result.txt", "PASS fixture-only production Density wrapper");
            EditorApplication.Exit(0);
        }
        catch (Exception e) { File.WriteAllText("build-result.txt", e.ToString()); EditorApplication.Exit(1); }
    }
#endif

    private IEnumerator Start()
    {
        yield return null;
        int exitCode = 0;
        try { Run(); }
        catch (Exception e) { log.AppendLine("FAIL " + e); exitCode = 1; }
        File.WriteAllText("result.txt", log.ToString());
        Application.Quit(exitCode);
    }

    private void Run()
    {
        log.AppendLine("GPU " + SystemInfo.graphicsDeviceName + " | " + SystemInfo.graphicsDeviceType);
        if (!SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBFloat))
            throw new Exception("ARGBFloat readback unsupported");
        File.WriteAllText("density-probes.csv", "case,x,y,z,cpu,gpu,cpuHeight,gpuHeight,envelope,delta\n");
        var field = new WeatherField();
        field.Build(new WeatherKey(73, 0f, false, (byte)WeatherRegimeType.Storm), 600f, 40000f, 40000f);
        // Rounded body channel and broad R are fixed at one. G near its mean keeps
        // the signed cavity from overwhelming the independent topology probes.
        byte[] solid = new byte[4 * 4 * 4 * 4]; Array.Fill(solid, (byte)255);
        for (int i = 1; i < solid.Length; i += 4) solid[i] = 128;
        var shallow = new byte[] { 0, 166, 204, 55 };
        var shield = new byte[] { 104, 159, 22, 0 };
        Color[] lower = Group("shallow-under-shield", field, solid, 4, shallow, shield, shallow, shield, 1, null,
            new[] { new Vector3(0, 2100, 0), new Vector3(0, 5000, 0), new Vector3(0, 7500, 0),
                new Vector3(0, 13000, 0), new Vector3(0, 500, 0) }, 0f, Vector3.zero, true);
        Require(lower[0].r > .2f && lower[1].r < .001f && lower[2].r > .2f,
            "Actual GPU retains shallow body and high shield with clear air between them");
        Require(lower[0].g > .2f && lower[0].g < .5f && lower[2].g > .1f && lower[2].g < .5f,
            "Actual GPU heights are local to shallow body/front, not the aggregate ceiling");
        var deep = new byte[] { 0, 0, 220, 142 };
        var wet = new byte[] { 0, 0, 22, 128 };
        Color[] upper = Group("deep-scud-shifted", field, solid, 4, deep, wet, deep, wet, 1, null,
            new[] { new Vector3(1000, 2700, -2000), new Vector3(1000, 6600, -2000),
                new Vector3(1000, 1500, -2000), new Vector3(1000, 14000, -2000) }, 600f,
            new Vector3(36000, 8000, -19000), true);
        Require(upper[0].r > .1f && upper[1].r > .1f && upper[2].r > .1f && upper[3].r == 0f,
            "Actual GPU deep column/scud and floating-origin/terrain height shift remain coherent");
        var far = new byte[] { 0, 80, 220, 170 };
        var farProfile = new byte[] { 140, 220, 32, 0 };
        Group("near-far-height-blend", field, solid, 4, shallow, shield, far, farProfile, 1, null,
            new[] { new Vector3(46000, 4500, 0), new Vector3(47000, 4500, 0),
                new Vector3(48000, 4500, 0), new Vector3(-48000, 8500, 0) }, 0f, Vector3.zero, true);
        Group("far-only-LOD-diagnostic", field, solid, 4, shallow, shield, far, farProfile, 1, null,
            new[] { new Vector3(51000, 4500, 0), new Vector3(51000, 11000, 0) }, 0f, Vector3.zero,
            false, compareCpu: false);

        // Hero geometry is at absolute seeded heights. Native height shift moves
        // the ordinary volume only; a low eye cloud must still fit the ray bounds.
        field.Build(new WeatherKey(73, 0f, false, (byte)WeatherRegimeType.Clear,
            sets: Superstructures.StormEyeSet, hasAnchor: true, anchorX: 0f, anchorZ: 0f),
            600f, 40000f, 40000f);
        var empty = new byte[4];
        Color[] hero = Group("hero-floor-height-shift", field, solid, 4, empty, empty, empty, empty, 1, null,
            new[] { new Vector3(0f, 1100f, 0f) }, 4000f, new Vector3(8000f, 2000f, -6000f),
            false, compareCpu: false, heroes: true);
        Require(hero[0].r > .3f && Math.Abs(hero[0].g - 600f / 1300f) < .02f,
            "Actual GPU storm-eye floor remains at its absolute local height under native cloud shift");

        byte[] noise = CloudNoise3D.Generate(64, 47);
        foreach (WeatherRegimeType state in new[] { WeatherRegimeType.Clear, WeatherRegimeType.Scattered, WeatherRegimeType.Storm })
        {
            var key = new WeatherKey(73, 0f, false, (byte)state);
            field.Build(key, 600f, 40000f, 40000f);
            CloudMaps maps = CloudMaps.Build(key, 600f, 40000f, 40000f, 12f, 50000f, 220000f);
            var points = new List<Vector3>();
            for (int z = -30000; z <= 30000; z += 15000)
            for (int x = -30000; x <= 30000; x += 15000)
            {
                WeatherPoint p = field.Sample(x, z);
                points.Add(new Vector3(x, p.CloudBase + 100f, z));
                points.Add(new Vector3(x, p.CloudBase + (p.LowTop - p.CloudBase) * .45f, z));
                points.Add(new Vector3(x, Math.Max(p.CloudTop, p.LowTop) + 2000f, z));
            }
            Group("generated-" + state, field, noise, 64, maps.Near, maps.NearProfiles, maps.Far,
                maps.FarProfiles, CloudMaps.NearSize, maps.Envelope, points.ToArray(), 0f, Vector3.zero, false);
        }
        RainOptics();
        var geometryFailures = new List<string>();
        foreach (Action check in new Action[] { RayComposition, SlabFootprint, AirOptics })
            try { check(); }
            catch (Exception error) { geometryFailures.Add(error.Message); log.AppendLine("FAIL " + error.Message); }
        if (geometryFailures.Count > 0) throw new Exception(string.Join("; ", geometryFailures));
        log.AppendLine("PASS density " + samples + " actual GPU probes; CSV preserves every density/height/envelope result");
        log.AppendLine("Limits: exact comparisons cover ordinary near/overlap bodies at gFoot=0/gVert=0; far-only erosion and CPU hero envelopes are recorded diagnostics. No image/performance acceptance or high-slab parity claim.");
    }

    private void RainOptics()
    {
        var owned = new List<UnityEngine.Object>();
        RenderTexture rt = null;
        RenderTexture previous = RenderTexture.active;
        try
        {
            var material = new Material(Resources.Load<Shader>("CloudDensityProbe")); owned.Add(material);
            material.SetFloat("_ProbeRainMode", 1f);
            material.SetVector("_CloudWorldOffset", Vector4.zero);
            material.SetFloat("_CloudHeightShift", 0f);
            material.SetVector("_CloudAltitudeBounds", new Vector4(0f, 16000f, 0f, 0f));
            CloudVolumeUniforms.ApplySpans(material, 16000f, 220000f);
            material.SetFloat("_CloudAirExtinction", .00002f);
            material.SetFloat("_CloudFlash", 0f);
            material.SetVector("_CloudFlashA", Vector4.zero); material.SetVector("_CloudFlashB", Vector4.zero);
            Color weather = new Color(.55f, 0f, 0f, .5f);
            Texture2D structure = FloatMap(new[] { weather }, 1, owned);
            Texture2D wet = FloatMap(new[] { new Color(0f, 0f, .125f, .8f) }, 1, owned);
            Texture2D dry = FloatMap(new[] { new Color(0f, 0f, .125f, 0f) }, 1, owned);
            var patchPixels = new Color[16 * 16];
            // Near span is -16..+16 km. The rightmost five texels begin at +6 km;
            // a 3 km opaque scene lies wholly before the wet patch, a 12 km ray reaches it.
            for (int z = 0; z < 16; z++)
            for (int x = 0; x < 16; x++) patchPixels[z * 16 + x] = new Color(0f, 0f, .125f, x >= 11 ? .8f : 0f);
            Texture2D patch = FloatMap(patchPixels, 16, owned);
            material.SetTexture("_WeatherMapTex", structure); material.SetTexture("_WeatherFarMapTex", structure);
            material.SetTexture("_WeatherFarProfileTex", dry);
            material.SetVector("_ProbeRainRay", Vector3.right);
            rt = new RenderTexture(1, 2, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            Require(rt.Create(), "Bounded two-row rain probe target created");
            var readback = new Texture2D(1, 2, TextureFormat.RGBAFloat, false, true); owned.Add(readback);
            var csv = new StringBuilder("case,r,g,b,a,depth,rainOpacity,opaqueDelta,opaqueDepth\n");
            int probes = 0;
            Color seed = new Color(.07f, .13f, .19f, .25f);
            void Lighting(bool enabled)
            {
                material.SetColor("_CloudAmbientColor", enabled ? new Color(.2f, .23f, .27f) : Color.black);
                material.SetColor("_CloudGroundColor", enabled ? new Color(.04f, .05f, .06f) : Color.black);
                material.SetColor("_CloudSunColor", enabled ? new Color(.6f, .55f, .5f) : Color.black);
                material.SetColor("_CloudFogColor", enabled ? new Color(.08f, .09f, .10f) : Color.black);
            }
            Color[] Probe(string name, Texture2D profile, float setting, float height, float sceneDistance,
                Color cloud, float cloudDistance, bool lit)
            {
                material.SetTexture("_WeatherProfileTex", profile);
                material.SetFloat("_CloudRainVisuals", setting);
                material.SetVector("_ProbeRainOrigin", new Vector3(0f, height, 0f));
                material.SetVector("_ProbeRainSettings", new Vector4(sceneDistance, .5f, 1f, cloudDistance));
                material.SetColor("_ProbeRainCloud", cloud);
                Lighting(lit);
                Graphics.Blit(null, rt, material, 0);
                RenderTexture.active = rt; readback.ReadPixels(new Rect(0, 0, 1, 2), 0, 0); readback.Apply();
                Color[] result = readback.GetPixels(); probes++;
                // Readback row orientation varies by graphics backend. Diagnostic
                // red is a kilometre-scale depth; colour red is below one here.
                if (result[0].r >= 1000f && result[1].r < 1f) Array.Reverse(result);
                Color c = result[0], d = result[1];
                csv.AppendLine(string.Join(",", name, F(c.r), F(c.g), F(c.b), F(c.a), F(d.r), F(d.g), F(d.b), F(d.a)));
                // Retain evidence even when an independent expectation fails next.
                File.WriteAllText("rain-optics-probes.csv", csv.ToString());
                Require(Finite(c) && Finite(d) && c.a >= 0f && c.a <= 1f && c.r >= 0f && c.g >= 0f && c.b >= 0f,
                    name + " finite, bounded opacity and nonnegative radiance");
                return result;
            }
            void Identity(string name, Color[] result, Color cloud, float distance)
            {
                Require(ColourDelta(result[0], cloud) <= .000001f && Math.Abs(result[1].r - distance) <= .001f && result[1].g == 0f,
                    name + " preserves exact colour/opacity/depth with no rain");
            }
            Identity("disabled", Probe("disabled", wet, 0f, 1000f, 12000f, seed, 4000f, true), seed, 4000f);
            Identity("dry", Probe("dry", dry, 1f, 1000f, 12000f, seed, 4000f, true), seed, 4000f);
            Identity("horizontal-above-base", Probe("horizontal-above-base", wet, 1f, 3000f, 12000f, seed, 4000f, true), seed, 4000f);
            Identity("below-sea", Probe("below-sea", wet, 1f, -40f, 12000f, seed, 4000f, true), seed, 4000f);
            Identity("near-scene", Probe("near-scene", wet, 1f, 1000f, 1499f, seed, 4000f, true), seed, 4000f);
            Color[] dark = Probe("zero-illumination", wet, 1f, 1000f, 12000f, Color.clear, 1000f, false);
            Require(dark[0].a > .05f && dark[0].r == 0f && dark[0].g == 0f && dark[0].b == 0f,
                "Rain attenuates in darkness without emitting light when all illumination and flash are zero");
            Identity("wet-patch-behind-scene", Probe("wet-patch-behind-scene", patch, 1f, 1000f, 3000f, Color.clear, 1000f, true), Color.clear, 1000f);
            Color[] visible = Probe("unobstructed-wet-patch", patch, 1f, 1000f, 12000f, Color.clear, 1000f, true);
            Require(visible[0].a > .05f && visible[0].a < 1f && visible[0].r > 0f && visible[0].r < 1f &&
                visible[0].g > 0f && visible[0].g < 1f && visible[0].b > 0f && visible[0].b < 1f &&
                visible[1].r > 6000f && visible[1].r <= 12000f,
                "Unobstructed lit rain patch contributes bounded non-emissive colour and a depth inside the wet interval");
            Require(visible[1].b <= .000001f && Math.Abs(visible[1].a - 1000f) <= .001f,
                "Opaque cloud ahead of every rain interval hides rain behind it and preserves cloud depth");
            log.AppendLine("PASS rain optics " + probes + " independent actual GPU geometry/identity/radiance probes");
        }
        finally
        {
            RenderTexture.active = previous;
            if (rt != null) { rt.Release(); Destroy(rt); }
            foreach (UnityEngine.Object value in owned) Destroy(value);
        }
    }

    private void RayComposition()
    {
        var owned = new List<UnityEngine.Object>();
        RenderTexture rt = null;
        RenderTexture previous = RenderTexture.active;
        try
        {
            var material = new Material(Resources.Load<Shader>("CloudDensityProbe")); owned.Add(material);
            material.SetFloat("_ProbeRayMode", 1f);
            material.SetVector("_CloudWorldOffset", Vector4.zero);
            material.SetVector("_CloudAltitudeBounds", new Vector4(0f, 16000f, 0f, 0f));
            material.SetVector("_CloudHeroBounds", new Vector4(300f, 16500f, 0f, 0f));
            material.SetVector("_CloudEye", Vector4.zero);
            material.SetVector("_SplitA", new Vector4(1f, 0f, 0f, 0f));
            material.SetVector("_SplitB", new Vector4(0f, 0f, 80000f, 0f));
            material.SetVector("_CloudWindDir", new Vector2(1f, 0f));
            material.SetFloat("_CloudHeightShift", 0f);
            material.SetFloat("_WeatherEnvelopeOn", 0f);
            material.SetFloat("_CloudCheckerOn", 0f);
            material.SetFloat("_CloudSteps", 112f); material.SetFloat("_CloudFarSteps", 40f);
            material.SetFloat("_CloudPixelAngle", .0005f);
            material.SetFloat("_CloudAirExtinction", .000008f);
            material.SetVector("_CloudSunDirection", Vector3.up);
            material.SetColor("_CloudSunColor", Color.black);
            material.SetColor("_CloudAmbientColor", Color.white);
            material.SetColor("_CloudGroundColor", Color.black);
            material.SetColor("_CloudFogColor", Color.black);
            material.SetFloat("_CloudFlash", 0f); material.SetFloat("_CloudRainVisuals", 0f);
            material.SetFloat("_FogBank", 0f); material.SetFloat("_HighCover", 0f);
            material.SetFloat("_HorizonCover", 0f); material.SetFloat("_MidSheet", 1f);
            CloudVolumeUniforms.ApplySpans(material, 105000f, 315000f);
            byte[] solid = new byte[4 * 4 * 4 * 4]; Array.Fill(solid, (byte)255);
            for (int i = 1; i < solid.Length; i += 4) solid[i] = 128;
            var noise = new Texture3D(4, 4, 4, TextureFormat.RGBA32, false)
            { filterMode = FilterMode.Trilinear, wrapMode = TextureWrapMode.Repeat };
            owned.Add(noise); noise.SetPixelData(solid, 0); noise.Apply(false, true);
            material.SetTexture("_CloudNoiseTex", noise);
            Texture2D empty = FloatMap(new[] { Color.clear }, 1, owned);
            foreach (string id in new[] { "_WeatherMapTex", "_WeatherProfileTex", "_WeatherFarMapTex", "_WeatherFarProfileTex" })
                material.SetTexture(id, empty);
            rt = new RenderTexture(1, 2, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            Require(rt.Create(), "Bounded ray-composition target created");
            var readback = new Texture2D(1, 2, TextureFormat.RGBAFloat, false, true); owned.Add(readback);
            var csv = new StringBuilder("case,r,g,b,a,depth\n");
            int probes = 0;
            var failures = new List<string>();
            void Check(bool valid, string description) { if (!valid) failures.Add(description); }
            void Hero(bool present, float z, bool supercell)
            {
                var a = new Vector4[5]; var b = new Vector4[5];
                a[0] = new Vector4(0f, z, 0f, supercell ? 1f : 0f);
                b[0] = supercell ? new Vector4(6000f, 14000f, 1f, 18000f)
                    : new Vector4(45000f, 12500f, 1f, 18000f);
                material.SetVectorArray("_HeroA", a); material.SetVectorArray("_HeroB", b);
                material.SetFloat("_HeroCount", present ? 1f : 0f);
            }
            Color[] Probe(string name, Vector3 origin, Vector3 direction, float sceneDistance, float middle)
            {
                material.SetVector("_ProbeRayOrigin", origin);
                material.SetVector("_ProbeRayDirection", direction);
                material.SetFloat("_ProbeRaySceneDistance", sceneDistance);
                material.SetFloat("_MidCover", middle);
                Graphics.Blit(null, rt, material, 0);
                RenderTexture.active = rt; readback.ReadPixels(new Rect(0, 0, 1, 2), 0, 0); readback.Apply();
                Color[] result = readback.GetPixels(); probes++;
                if (result[0].r >= 1000f && result[1].r < 1f) Array.Reverse(result);
                Color c = result[0];
                csv.AppendLine(string.Join(",", name, F(c.r), F(c.g), F(c.b), F(c.a), F(result[1].r)));
                File.WriteAllText("ray-composition-probes.csv", csv.ToString());
                Require(Finite(c) && Finite(result[1]) && c.a >= 0f && c.a <= 1f &&
                    c.r >= 0f && c.g >= 0f && c.b >= 0f, name + " has finite, bounded opacity and nonnegative radiance");
                return result;
            }
            Vector3 oblique = new Vector3(0f, -.1f, 1f), above = new Vector3(0f, 8000f, 0f);
            Hero(true, 10000f, false);
            Color[] close = Probe("near-opaque-hero", above, oblique, 400000f, 0f);
            Color[] behind = Probe("middle-behind-near-hero", above, oblique, 400000f, 1f);
            Check(close[0].a > .94f && close[1].r < 25000f,
                "Near hero is opaque and precedes the 30-47 km middle-layer chord");
            float rgbDelta = Mathf.Max(Mathf.Abs(close[0].r - behind[0].r), Mathf.Abs(close[0].g - behind[0].g),
                Mathf.Abs(close[0].b - behind[0].b));
            Check(rgbDelta <= (1f - close[0].a) * .8f + .002f,
                "A middle layer behind an opaque storm cannot repaint its visible colour");
            Check(behind[0].a >= close[0].a - .000001f && behind[0].a - close[0].a <= 1f - close[0].a + .000001f,
                "The remaining visible middle layer adds only the cloud's remaining transmittance to opacity");
            Hero(false, 0f, false);
            Color[] middle = Probe("unobstructed-middle-layer", above, oblique, 400000f, 1f);
            Check(middle[0].a > .8f && middle[0].r > .4f,
                "The same middle layer is visibly present without the intervening storm");
            Hero(true, 50000f, false);
            Color[] far = Probe("hero-behind-middle-off", above, oblique, 400000f, 0f);
            Color[] front = Probe("middle-in-front-of-far-hero", above, oblique, 400000f, 1f);
            Check(far[0].a > .9f && far[1].r > 47000f && ColourDelta(far[0], front[0]) > .05f,
                "A middle layer encountered before a more distant storm contributes to the view");
            Hero(true, 260000f, true);
            Vector3 high = new Vector3(0f, 17000f, 0f), toward = new Vector3(0f, -11000f, 260000f);
            Color[] distant = Probe("hero-beyond-ordinary-220km", high, toward, 400000f, 0f);
            Check(distant[0].a > .1f && distant[1].r > 220000f && distant[1].r < 400000f,
                "Independent distant hero renders beyond the ordinary map march limit");
            Color[] occluded = Probe("far-hero-behind-opaque-scene", high, toward, 100000f, 0f);
            Color[] misses = Probe("ray-misses-every-hero", high, Vector3.right, 400000f, 0f);
            Check(ColourDelta(occluded[0], Color.clear) <= .000001f && ColourDelta(misses[0], Color.clear) <= .000001f,
                "Opaque terrain and rays missing the hero remain exact empty identities");
            // A near bounding circle crosses the ray, but its actual tilted tower/anvil
            // do not. Its union with the far hero creates a long empty interval that
            // exhausted the old 96-iteration adaptive walk before the distant body.
            var sparseA = new Vector4[5]; var sparseB = new Vector4[5];
            sparseA[0] = new Vector4(45000f, 20000f, 0f, 1f);
            sparseB[0] = new Vector4(6000f, 14000f, 1f, 18000f);
            sparseA[1] = new Vector4(0f, 260000f, 0f, 1f);
            sparseB[1] = new Vector4(6000f, 14000f, 1f, 18000f);
            material.SetVectorArray("_HeroA", sparseA); material.SetVectorArray("_HeroB", sparseB);
            material.SetFloat("_HeroCount", 1f);
            Color[] emptyCircle = Probe("near-circle-actual-body-miss", high, toward, 400000f, 0f);
            Check(ColourDelta(emptyCircle[0], Color.clear) <= .000001f,
                "The near circle is a genuine culling false positive with no cloud on the ray");
            material.SetFloat("_HeroCount", 2f);
            Color[] sparse = Probe("sparse-two-hero-union", high, toward, 400000f, 0f);
            Check(sparse[0].a >= distant[0].a * .5f && sparse[0].a > .1f &&
                sparse[1].r > 220000f && sparse[1].r < 400000f,
                "A sparse two-hero bounding union still visits its distant visible body within the fixed walk budget");
            if (failures.Count > 0) throw new Exception("Ray composition expectations: " + string.Join("; ", failures));
            log.AppendLine("PASS ray composition " + probes + " actual GPU oblique-layer/range/opaque-depth probes");
        }
        finally
        {
            RenderTexture.active = previous;
            if (rt != null) { rt.Release(); Destroy(rt); }
            foreach (UnityEngine.Object value in owned) Destroy(value);
        }
    }

    private void SlabFootprint()
    {
        var owned = new List<UnityEngine.Object>();
        RenderTexture rt = null;
        RenderTexture previous = RenderTexture.active;
        try
        {
            var material = new Material(Resources.Load<Shader>("CloudDensityProbe")); owned.Add(material);
            material.SetFloat("_ProbeSlabMode", 1f);
            material.SetVector("_CloudEye", Vector4.zero); material.SetVector("_SplitB", Vector4.zero);
            material.SetVector("_CloudWindDir", new Vector2(0f, 1f));
            // Constant channel values isolate footprint equations from mip filtering.
            // CPU has an altitude dome; the rendered slab uses its depth average, 0.77.
            byte[] bytes = new byte[4 * 4 * 4 * 4];
            for (int i = 0; i < bytes.Length; i += 4)
            { bytes[i] = 200; bytes[i + 1] = 90; bytes[i + 2] = 255; bytes[i + 3] = 255; }
            var noise = new Texture3D(4, 4, 4, TextureFormat.RGBA32, false)
            { filterMode = FilterMode.Trilinear, wrapMode = TextureWrapMode.Repeat };
            owned.Add(noise); noise.SetPixelData(bytes, 0); noise.Apply(false, true);
            material.SetTexture("_CloudNoiseTex", noise);
            const int count = 9;
            var positions = new Texture2D(count, 1, TextureFormat.RGBAFloat, false, true)
            { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp }; owned.Add(positions);
            material.SetTexture("_DensityProbePositions", positions);
            rt = new RenderTexture(count, 1, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            Require(rt.Create(), "Bounded middle-footprint target created");
            var readback = new Texture2D(count, 1, TextureFormat.RGBAFloat, false, true); owned.Add(readback);
            var csv = new StringBuilder("sheet,x,y,z,cpuDepthAverage,gpu,delta\n");
            int probes = 0, positive = 0;
            foreach (float sheet in new[] { 0f, 1f })
            {
                var sky = new StateParams { MidCover = .6f, MidSheet = sheet };
                var cpu = new CloudBodies(bytes, 4, sky);
                material.SetFloat("_MidCover", sky.MidCover); material.SetFloat("_MidSheet", sky.MidSheet);
                float height = Mathf.Lerp(4200f, 3300f, sheet) + Mathf.Lerp(400f, 1700f, sheet) * .5f;
                var inputs = new Color[count]; int at = 0;
                for (int z = -12000; z <= 12000; z += 12000)
                for (int x = -12000; x <= 12000; x += 12000) inputs[at++] = new Color(x, height, z, 0f);
                positions.SetPixels(inputs); positions.Apply();
                Graphics.Blit(null, rt, material, 0);
                RenderTexture.active = rt; readback.ReadPixels(new Rect(0, 0, count, 1), 0, 0); readback.Apply();
                Color[] output = readback.GetPixels();
                string failure = null;
                for (int i = 0; i < count; i++)
                {
                    Color p = inputs[i]; float expected = cpu.MidLayer(p.r, p.g, p.b) * .77f;
                    float delta = Mathf.Abs(expected - output[i].r); probes++;
                    if (output[i].r > .01f) positive++;
                    csv.AppendLine(string.Join(",", F(sheet), F(p.r), F(p.g), F(p.b), F(expected), F(output[i].r), F(delta)));
                    if (!Finite(output[i]) || delta > .001f)
                        failure = failure ?? "Middle-layer footprint mismatch at " + i + ": CPU=" + F(expected) + " GPU=" + F(output[i].r);
                }
                File.WriteAllText("slab-footprint-probes.csv", csv.ToString());
                if (failure != null) throw new Exception(failure);
            }
            Require(positive > 0, "Footprint probes include positive cloud, rather than only empty agreement");
            log.AppendLine("PASS slab footprint " + probes + " actual GPU patch/warp/street probes; constant channels isolate footprint from mip/vertical integration");
        }
        finally
        {
            RenderTexture.active = previous;
            if (rt != null) { rt.Release(); Destroy(rt); }
            foreach (UnityEngine.Object value in owned) Destroy(value);
        }
    }

    private void AirOptics()
    {
        var owned = new List<UnityEngine.Object>();
        RenderTexture rt = null;
        RenderTexture previous = RenderTexture.active;
        try
        {
            string[] names = { "zero-distance", "horizontal-sea", "horizontal-17km", "horizontal-35km",
                "upward-chord", "downward-chord", "near-horizontal-below-threshold", "near-horizontal-above-threshold",
                "nearly-equal-heights", "below-sea-clamped", "long-sea-baseline", "horizontal-45km" };
            var inputs = new[] { new Color(0f, 17000f, 35000f, 0f), new Color(100000f, 0f, 0f, 0f),
                new Color(100000f, 17000f, 17000f, 0f), new Color(100000f, 35000f, 35000f, 0f),
                new Color(100000f, 0f, 17000f, 0f), new Color(100000f, 17000f, 0f, 0f),
                new Color(100000f, 1000f, 1021.99f, 0f), new Color(100000f, 1000f, 1022.01f, 0f),
                new Color(100000f, 1000f, 1000.001f, 0f), new Color(100000f, -200f, -40f, 0f),
                new Color(400000f, 0f, 0f, 0f), new Color(100000f, 45000f, 45000f, 0f) };
            var material = new Material(Resources.Load<Shader>("CloudDensityProbe")); owned.Add(material);
            material.SetFloat("_ProbeAirMode", 1f); material.SetFloat("_CloudAirExtinction", .00004f);
            var positions = new Texture2D(inputs.Length, 1, TextureFormat.RGBAFloat, false, true)
            { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp }; owned.Add(positions);
            positions.SetPixels(inputs); positions.Apply(false, true);
            material.SetTexture("_DensityProbePositions", positions);
            rt = new RenderTexture(inputs.Length, 1, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            Require(rt.Create(), "Bounded aerial-perspective target created");
            var readback = new Texture2D(inputs.Length, 1, TextureFormat.RGBAFloat, false, true); owned.Add(readback);
            Graphics.Blit(null, rt, material, 0);
            RenderTexture.active = rt; readback.ReadPixels(new Rect(0, 0, inputs.Length, 1), 0, 0); readback.Apply();
            Color[] gpu = readback.GetPixels();
            var csv = new StringBuilder("case,distance,cameraHeight,sampleHeight,reference,gpu,delta\n");
            string failure = null;
            for (int i = 0; i < inputs.Length; i++)
            {
                Color p = inputs[i];
                double reference = IntegratedAir(p.r, p.g, p.b, .00004f);
                double delta = Math.Abs(reference - gpu[i].r);
                csv.AppendLine(string.Join(",", names[i], F(p.r), F(p.g), F(p.b),
                    reference.ToString("R", CultureInfo.InvariantCulture), F(gpu[i].r), delta.ToString("R", CultureInfo.InvariantCulture)));
                if (!Finite(gpu[i]) || gpu[i].r < 0f || gpu[i].r > 1f || delta > .00002)
                    failure = failure ?? names[i] + " aerial perspective mismatch " + delta;
            }
            File.WriteAllText("air-optics-probes.csv", csv.ToString());
            if (failure != null) throw new Exception(failure);
            Require(gpu[0].r == 1f, "Zero-length air chord is exact identity");
            Require(gpu[1].r < gpu[2].r && gpu[2].r < gpu[3].r && gpu[3].r < gpu[11].r,
                "Both density profiles thin with altitude rather than retaining a molecular fog floor");
            Require(Math.Abs(gpu[4].r - gpu[5].r) < .000002f,
                "Reciprocal rising/falling chords have the same optical attenuation");
            Require(Math.Abs(gpu[6].r - gpu[7].r) < .00002f && gpu[8].r > 0f,
                "The midpoint limit stays continuous across nearly horizontal profile chords");
            Require(Math.Abs(gpu[1].r - gpu[9].r) < .000001f &&
                Math.Abs(gpu[1].r - Math.Exp(-100000f * .00004f)) < .000001f,
                "Below-sea endpoints clamp finitely and the horizontal ground/sea baseline remains unchanged");
            log.AppendLine("PASS air optics " + inputs.Length + " actual GPU identity/height/reciprocity/continuity probes against independent double-precision integration");
        }
        finally
        {
            RenderTexture.active = previous;
            if (rt != null) { rt.Release(); Destroy(rt); }
            foreach (UnityEngine.Object value in owned) Destroy(value);
        }
    }

    private static double IntegratedAir(double distance, double cameraHeight, double sampleHeight, double seaExtinction)
    {
        // Independent Simpson integration of density along the chord, not the shader's
        // analytic exp-difference formula. Inputs below sea clamp before the chord,
        // matching the renderer's endpoint convention. This verifies its scalar model,
        // not the physical accuracy of a spectral/multiple-scattering atmosphere.
        const int intervals = 4096;
        double h0 = Math.Max(0d, cameraHeight), h1 = Math.Max(0d, sampleHeight);
        double integral = 0d;
        for (int i = 0; i <= intervals; i++)
        {
            double height = h0 + (h1 - h0) * i / intervals;
            double density = .000008d * Math.Exp(-height / 8000d) +
                Math.Max(0d, seaExtinction - .000008d) * Math.Exp(-height / 2200d);
            integral += density * (i == 0 || i == intervals ? 1d : (i & 1) == 0 ? 2d : 4d);
        }
        return Math.Exp(-distance * integral / (3d * intervals));
    }

    private Color[] Group(string name, WeatherField field, byte[] noiseBytes, int noiseSize,
        byte[] near, byte[] nearProfile, byte[] far, byte[] farProfile, int mapSize, byte[] envelope,
        Vector3[] positions, float shift, Vector3 offset, bool strictHeight, bool compareCpu = true, bool heroes = false)
    {
        if (positions.Length > 256) throw new Exception("Probe batch exceeds bounded256");
        var owned = new List<UnityEngine.Object>();
        RenderTexture rt = null;
        RenderTexture previous = RenderTexture.active;
        try
        {
            var material = new Material(Resources.Load<Shader>("CloudDensityProbe")); owned.Add(material);
            Require(material.shader.isSupported, "Fixture density shader supported");
            var noise = new Texture3D(noiseSize, noiseSize, noiseSize, TextureFormat.RGBA32, false)
            { filterMode = FilterMode.Trilinear, wrapMode = TextureWrapMode.Repeat };
            owned.Add(noise); noise.SetPixelData(noiseBytes, 0); noise.Apply(false, true);
            var uniforms = new CloudVolumeUniforms(); uniforms.Settle(field);
            var frame = new CloudFrame { WorldOffset = offset, CloudShift = shift, Bottom = 0f, Top = 16000f,
                FieldOfView = 60f, PixelHeight = 1080, SunDirection = Vector3.up };
            uniforms.Apply(material, field, frame, noise);
            CloudVolumeUniforms.ApplySpans(material, 50000f, 220000f);
            if (!heroes) { material.SetFloat("_HeroCount", 0f); material.SetVector("_CloudEye", Vector4.zero); }
            int farSize = far.Length == near.Length ? mapSize : CloudMaps.FarSize;
            material.SetTexture("_WeatherMapTex", Map(near, mapSize, owned));
            material.SetTexture("_WeatherProfileTex", Map(nearProfile, mapSize, owned));
            material.SetTexture("_WeatherFarMapTex", Map(far, farSize, owned));
            material.SetTexture("_WeatherFarProfileTex", Map(farProfile, farSize, owned));
            material.SetFloat("_WeatherEnvelopeOn", envelope == null ? 0f : 1f);
            if (envelope != null)
            {
                var e = Map(envelope, CloudMaps.EnvelopeSize, owned); e.filterMode = FilterMode.Point;
                material.SetTexture("_WeatherEnvelopeTex", e);
            }
            var probe = new Texture2D(positions.Length, 1, TextureFormat.RGBAFloat, false, true)
            { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            owned.Add(probe);
            var inputs = new Color[positions.Length];
            for (int i = 0; i < positions.Length; i++)
            {
                Vector3 local = positions[i] - offset;
                inputs[i] = new Color(local.x, local.y, local.z, 0f);
            }
            probe.SetPixels(inputs); probe.Apply(false, true);
            material.SetTexture("_DensityProbePositions", probe);
            rt = new RenderTexture(positions.Length, 1, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            Require(rt.Create(), "Small floating-point probe target created");
            Graphics.Blit(null, rt, material, 0);
            var readback = new Texture2D(positions.Length, 1, TextureFormat.RGBAFloat, false, true); owned.Add(readback);
            RenderTexture.active = rt; readback.ReadPixels(new Rect(0, 0, positions.Length, 1), 0, 0); readback.Apply();
            Color[] gpu = readback.GetPixels();
            StateParams sky = field.Params; sky.MidCover = 0f; sky.HighCover = 0f;
            WeatherPoint Sample(float x, float z)
            {
                CloudBodies.MapWeights(x, z, 50000f, 220000f, out float weight, out float fade);
                WeatherPoint outer = CloudBodies.SampleMap(far, farProfile, farSize, x / 440000f + .5f, z / 440000f + .5f);
                outer.BackgroundCover *= fade; outer.FrontCover *= fade; outer.CellShape *= fade;
                if (weight <= 0f) return outer;
                WeatherPoint inner = CloudBodies.SampleMap(near, nearProfile, mapSize, x / 100000f + .5f, z / 100000f + .5f);
                return CloudBodies.BlendMaps(outer, inner, weight);
            }
            var cpu = new CloudBodies(noiseBytes, noiseSize, sky, field.PrevailingHeading,
                field: heroes ? field : null, shownHeroes: heroes ? uniforms.HeroStrengths : null,
                nearDetail: true, weatherSampler: Sample);
            var csv = new StringBuilder(); float maxDelta = 0f, maxHeight = 0f; string failure = null;
            for (int i = 0; i < positions.Length; i++)
            {
                Vector3 p = positions[i];
                float expected = cpu.Density(default, p.x, p.y, p.z, shift, out float height);
                float delta = Mathf.Abs(expected - gpu[i].r);
                float hd = Mathf.Abs(height - gpu[i].g);
                maxDelta = Mathf.Max(maxDelta, delta); maxHeight = Mathf.Max(maxHeight, hd);
                csv.AppendLine(string.Join(",", name, F(p.x), F(p.y), F(p.z), F(expected), F(gpu[i].r),
                    F(height), F(gpu[i].g), F(gpu[i].b), F(delta)));
                if (float.IsNaN(gpu[i].r) || gpu[i].r < 0f || gpu[i].r > 1f || gpu[i].g < 0f || gpu[i].g > 1f ||
                    (compareCpu && delta > .015f) || Mathf.Abs(gpu[i].r - gpu[i].b) > .0001f ||
                    (strictHeight && expected > .04f && gpu[i].r > .04f && hd > .02f) ||
                    (gpu[i].r <= 0f && gpu[i].g != 0f))
                    failure = failure ?? name + " probe" + i + " CPU=" + F(expected) + " GPU=" + F(gpu[i].r) +
                        " hCPU=" + F(height) + " hGPU=" + F(gpu[i].g) + " envelope=" + F(gpu[i].b);
                if (heroes && gpu[i].r > .01f)
                {
                    Vector4 bounds = material.GetVector("_CloudHeroBounds");
                    Vector4 ordinary = material.GetVector("_CloudAltitudeBounds");
                    Require(p.y >= Math.Min(bounds.x, ordinary.x) && p.y <= Math.Max(bounds.y, ordinary.y),
                        "Actual positive hero density is enclosed by combined ray bounds after native height shift");
                    log.AppendLine("hero-bound y=" + F(p.y) + " ordinary=" + F(ordinary.x) + ".." + F(ordinary.y) +
                        " absoluteHero=" + F(bounds.x) + ".." + F(bounds.y));
                }
            }
            File.AppendAllText("density-probes.csv", csv.ToString()); samples += positions.Length;
            log.AppendLine(name + " samples=" + positions.Length + " maxDensityDelta=" + F(maxDelta) + " maxHeightDelta=" + F(maxHeight));
            if (failure != null) throw new Exception(failure);
            return gpu;
        }
        finally
        {
            RenderTexture.active = previous;
            if (rt != null) { rt.Release(); Destroy(rt); }
            foreach (UnityEngine.Object value in owned) Destroy(value);
        }
    }

    private static Texture2D Map(byte[] bytes, int size, List<UnityEngine.Object> owned)
    {
        var value = new Texture2D(size, size, TextureFormat.RGBA32, false, true)
        { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        owned.Add(value); value.LoadRawTextureData(bytes); value.Apply(false, true); return value;
    }
    private static Texture2D FloatMap(Color[] pixels, int size, List<UnityEngine.Object> owned)
    {
        var value = new Texture2D(size, size, TextureFormat.RGBAFloat, false, true)
        { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
        owned.Add(value); value.SetPixels(pixels); value.Apply(false, true); return value;
    }
    private static bool Finite(Color c) => !float.IsNaN(c.r) && !float.IsNaN(c.g) && !float.IsNaN(c.b) && !float.IsNaN(c.a) &&
        !float.IsInfinity(c.r) && !float.IsInfinity(c.g) && !float.IsInfinity(c.b) && !float.IsInfinity(c.a);
    private static float ColourDelta(Color a, Color b) => Mathf.Max(Mathf.Max(Mathf.Abs(a.r - b.r), Mathf.Abs(a.g - b.g)),
        Mathf.Max(Mathf.Abs(a.b - b.b), Mathf.Abs(a.a - b.a)));
    private static string F(float value) => value.ToString("R", CultureInfo.InvariantCulture);
    private static void Require(bool value, string message) { if (!value) throw new Exception(message); }
}
#endif
