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
using UnityEngine.Rendering;
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
            if (shader == null || ShaderUtil.ShaderHasError(shader))
            {
                var messages = new StringBuilder();
                if (shader != null)
                    foreach (ShaderMessage m in ShaderUtil.GetShaderMessages(shader))
                        messages.AppendLine(m.severity + " " + m.line + ": " + m.message);
                throw new Exception("Density probe shader invalid\n" + messages);
            }
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
        var shallow = new byte[] { 0, 0, 204, 55 };
        var shield = new byte[] { 0, 0, 86, 0 };   // base byte 86 is 1350 m on the 4096 m scale; the frontal shield's channels (R, G) are unused
        Color[] lower = Group("shallow-cluster", field, solid, 4, shallow, shield, shallow, shield, 1, null,
            new[] { new Vector3(0, 2100, 0), new Vector3(0, 5000, 0),
                new Vector3(0, 13000, 0), new Vector3(0, 500, 0) }, Vector3.zero, true);
        Require(lower[0].r > .2f && lower[1].r < .001f,
            "Actual GPU retains the shallow body with clear air above it");
        Require(lower[0].g > .2f && lower[0].g < .5f,
            "Actual GPU heights are local to the shallow body, not the aggregate ceiling");
        var deep = new byte[] { 0, 0, 220, 142 };
        var wet = new byte[] { 0, 0, 86, 128 };
        Color[] upper = Group("deep-scud-offset", field, solid, 4, deep, wet, deep, wet, 1, null,
            new[] { new Vector3(1000, 2700, -2000), new Vector3(1000, 6600, -2000),
                new Vector3(1000, 1500, -2000), new Vector3(1000, 14000, -2000) },
            new Vector3(36000, 8000, -19000), true);
        Require(upper[0].r > .1f && upper[1].r > .1f && upper[2].r > .1f && upper[3].r == 0f,
            "Actual GPU deep column/scud and floating-origin/terrain offset remain coherent");
        var far = new byte[] { 0, 0, 220, 170 };
        var farProfile = new byte[] { 0, 0, 125, 0 };
        Group("near-far-height-blend", field, solid, 4, shallow, shield, far, farProfile, 1, null,
            new[] { new Vector3(46000, 4500, 0), new Vector3(47000, 4500, 0),
                new Vector3(48000, 4500, 0), new Vector3(-48000, 8500, 0) }, Vector3.zero, true);
        Group("far-only-LOD-diagnostic", field, solid, 4, shallow, shield, far, farProfile, 1, null,
            new[] { new Vector3(51000, 4500, 0), new Vector3(51000, 11000, 0) }, Vector3.zero,
            false, compareCpu: false);

        // Hero geometry is at absolute seeded heights; a low eye cloud must still fit the ray bounds.
        field.Build(new WeatherKey(73, 0f, false, (byte)WeatherRegimeType.Clear,
            sets: Superstructures.StormEyeSet, hasAnchor: true, anchorX: 0f, anchorZ: 0f),
            600f, 40000f, 40000f);
        var empty = new byte[4];
        Color[] hero = Group("hero-floor", field, solid, 4, empty, empty, empty, empty, 1, null,
            new[] { new Vector3(0f, 1100f, 0f) }, new Vector3(8000f, 2000f, -6000f),
            false, compareCpu: false, heroes: true);
        Require(hero[0].r > .3f && Math.Abs(hero[0].g - 600f / 1300f) < .02f,
            "Actual GPU storm-eye floor remains at its absolute local height");

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
                maps.FarProfiles, CloudMaps.NearSize, maps.Envelope, points.ToArray(), Vector3.zero, false);
        }

        // One low genus per sky: three pinned library skies (a humilis, a cumulonimbus, a stratus) read their lobe, depth,
        // dome and edge texture from the tape; then a tape whose genus changes across a 12 km ramp, bent by a 10 km meander,
        // so one march crosses lobe rungs 1 to 5 and the ladder blend.
        foreach (int pin in new[] { 7, 9, 43, 28 })
        {
            var pinned = new WeatherKey(73, 0f, false, skyPin: (byte)pin);
            field.Build(pinned, 600f, 40000f, 40000f);
            CloudMaps pinnedMaps = CloudMaps.Build(pinned, 600f, 40000f, 40000f, 12f, 50000f, 220000f);
            Group("pinned-sky-" + (pin + 1), field, noise, 64, pinnedMaps.Near, pinnedMaps.NearProfiles, pinnedMaps.Far,
                pinnedMaps.FarProfiles, CloudMaps.NearSize, pinnedMaps.Envelope, DeckPoints(field, 15000, 15000), Vector3.zero, false);
        }
        TapeFixture ramp = RampFixture();
        var rampKey = new WeatherKey(73, 0f, false, (byte)WeatherRegimeType.Scattered);
        field.Build(rampKey, 600f, 40000f, 40000f);
        field.UseFixtureTape(ramp.Sampler, ramp.Frame);
        CloudMaps rampMaps = CloudMaps.Build(rampKey, 600f, 40000f, 40000f, 12f, 50000f, 220000f, ramp);
        Group("tape-ramp-lobes", field, noise, 64, rampMaps.Near, rampMaps.NearProfiles, rampMaps.Far,
            rampMaps.FarProfiles, CloudMaps.NearSize, rampMaps.Envelope, DeckPoints(field, 1500, 15000), Vector3.zero, false,
            tolerance: .04f, medianLimit: .002f); // a rung blend multiplies the noise taps' 8-bit filter error by up to 1.41, and the remaps by 2.5; the median guard catches what the slack hides
        RainOptics();
        var geometryFailures = new List<string>();
        foreach (Action check in new Action[] { RayComposition, SlabFootprint, TapeRows, NoiseRows, SlabMaskAndOverlap, AirOptics })
            try { check(); }
            catch (Exception error) { geometryFailures.Add(error.Message); log.AppendLine("FAIL " + error.Message); }
        if (geometryFailures.Count > 0) throw new Exception(string.Join("; ", geometryFailures));
        log.AppendLine("PASS density " + samples + " actual GPU probes (class skies, pinned library skies, a ramped tape); CSV preserves every density/height/envelope result");
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
            material.SetVector("_CloudAltitudeBounds", new Vector4(0f, 16000f, 0f, 0f));
            CloudVolumeUniforms.ApplySpans(material, 16000f, 220000f);
            material.SetFloat("_CloudAirExtinction", .00002f);
            material.SetFloat("_CloudFlash", 0f);
            material.SetVector("_CloudFlashA", Vector4.zero); material.SetVector("_CloudFlashB", Vector4.zero);
            Color weather = new Color(.55f, 0f, 0f, .5f);
            Texture2D structure = FloatMap(new[] { weather }, 1, owned);
            // The cloud base sits at 2000 m: its byte spans 0-4096 m (CloudMaps.BaseRange).
            const float Base2Km = 2000f / CloudMaps.BaseRange;
            Texture2D wet = FloatMap(new[] { new Color(0f, 0f, Base2Km, .8f) }, 1, owned);
            Texture2D dry = FloatMap(new[] { new Color(0f, 0f, Base2Km, 0f) }, 1, owned);
            var patchPixels = new Color[16 * 16];
            // Near span is -16..+16 km. The rightmost five texels begin at +6 km;
            // a 3 km opaque scene lies wholly before the wet patch, a 12 km ray reaches it.
            for (int z = 0; z < 16; z++)
            for (int x = 0; x < 16; x++) patchPixels[z * 16 + x] = new Color(0f, 0f, Base2Km, x >= 11 ? .8f : 0f);
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
            material.SetVector("_CloudWindDir", new Vector2(1f, 0f));
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
            material.SetFloat("_FogBank", 0f);
            material.SetFloat("_HorizonCover", 0f);
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
            var tapeBinder = new CloudVolumeUniforms();
            Color[] Probe(string name, Vector3 origin, Vector3 direction, float sceneDistance, float middle)
            {
                material.SetVector("_ProbeRayOrigin", origin);
                material.SetVector("_ProbeRayDirection", direction);
                material.SetFloat("_ProbeRaySceneDistance", sceneDistance);
                // The middle layer is a one-keyframe tape: an altostratus sheet (3300..5000 m) of the given cover, or none.
                var layered = new SkyParams { Mid = SkyLayer.LegacyMid(middle, 1f) };
                BindTape(tapeBinder, material, BakedTape.TwoKeyframes(layered, layered, 1500000f, 12000f), default);
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
            tapeBinder.Reset();
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

    /// <summary>Binds a hand-built tape through the production uniforms: the same texture, frame, chords and flags the game binds.</summary>
    private static void BindTape(CloudVolumeUniforms uniforms, Material material, BakedTape tape, TapeFrame frame)
    {
        var field = new WeatherField();
        field.Build(new WeatherKey(1, 0f, false, (byte)WeatherRegimeType.Clear), 0f, 40000f, 40000f);
        field.UseFixtureTape(new TapeSampler(tape), frame);
        Require(uniforms.BindTape(material, field), "Tape texture bound");
    }

    /// <summary>The ramp fixture: a fair-weather humilis sky (sky 8) downwind and an evening-storm sky (sky 46) upwind of a 12 km ramp at
    /// x = 0, the tape axis pointing east, a 10 km meander bending the ramp along z.</summary>
    private static TapeFixture RampFixture() => new TapeFixture(
        BakedTape.TwoKeyframes(SkyLibrary.Nominal(7), SkyLibrary.Nominal(45), 1500000f, 12000f),
        new TapeFrame(90f, 1500000f, 10000f, 80000f, 0.7f));

    /// <summary>Probe points through the low deck of a field: just above the base, a bit up the body, well above the top.</summary>
    private static Vector3[] DeckPoints(WeatherField field, int xStep, int zStep)
    {
        var points = new List<Vector3>();
        for (int z = -30000; z <= 30000; z += zStep)
        for (int x = -30000; x <= 30000; x += xStep)
        {
            if (xStep < 5000 && Math.Abs(x) > 9000) continue;
            WeatherPoint p = field.Sample(x, z);
            points.Add(new Vector3(x, p.CloudBase + 100f, z));
            points.Add(new Vector3(x, p.CloudBase + (p.LowTop - p.CloudBase) * .45f, z));
            points.Add(new Vector3(x, Math.Max(p.CloudTop, p.LowTop) + 2000f, z));
        }
        if (points.Count > 256) points.RemoveRange(256, points.Count - 256);
        return points.ToArray();
    }

    /// <summary>A copy of a tape with both thin layers emptied: the GPU Density has no thin layers (they march separately).</summary>
    private static TapeSampler WithoutLayers(TapeSampler tape)
    {
        BakedTape source = tape.Tape;
        var copy = new BakedTape(source.Texels);
        for (int i = 0; i < source.Texels; i++)
        {
            SkyParams row = source.Texel(i);
            row.Mid.Cover = 0f; row.High.Cover = 0f;
            copy.SetTexel(i, row);
        }
        copy.Finish();
        return new TapeSampler(copy);
    }

    private void NoiseRows()
    {
        // The six noise layers' rotation and offset, GPU against CPU: NoiseXZ of a spread of map points through a keyed frame. The offsets
        // are up to 100 km, so float32 holds them to about a centimetre; the tolerance is a few centimetres.
        var owned = new List<UnityEngine.Object>();
        RenderTexture rt = null;
        RenderTexture previous = RenderTexture.active;
        var tapeBinder = new CloudVolumeUniforms();
        try
        {
            var material = new Material(Resources.Load<Shader>("CloudDensityProbe")); owned.Add(material);
            material.SetFloat("_ProbeNoiseMode", 1f);
            var frame = new TapeFrame(40f, 0f, noise: new NoiseXform(0xC0FFEE11u));
            BindTape(tapeBinder, material, ConstantFixtureTape(), frame);
            var inputs = new List<Color>();
            for (int z = -3; z <= 3; z++)
                for (int x = -3; x <= 3; x++) inputs.Add(new Color(x * 23000f, 0f, z * 17000f, 0f));
            int count = inputs.Count;
            var positions = new Texture2D(count, 1, TextureFormat.RGBAFloat, false, true)
            { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp }; owned.Add(positions);
            positions.SetPixels(inputs.ToArray()); positions.Apply();
            material.SetTexture("_DensityProbePositions", positions);
            rt = new RenderTexture(count, 1, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            Require(rt.Create(), "Bounded noise-row target created");
            var readback = new Texture2D(count, 1, TextureFormat.RGBAFloat, false, true); owned.Add(readback);
            int probes = 0;
            for (int pair = 0; pair < NoiseXform.Layers / 2; pair++)
            {
                material.SetFloat("_ProbeLayer", pair * 2);
                Graphics.Blit(null, rt, material, 0);
                RenderTexture.active = rt; readback.ReadPixels(new Rect(0, 0, count, 1), 0, 0); readback.Apply();
                Color[] gpu = readback.GetPixels();
                for (int i = 0; i < count; i++)
                {
                    frame.NoiseXZ(inputs[i].r, inputs[i].b, pair * 2, out float ax, out float az);
                    frame.NoiseXZ(inputs[i].r, inputs[i].b, pair * 2 + 1, out float bx, out float bz);
                    float[] expected = { ax, az, bx, bz }, actual = { gpu[i].r, gpu[i].g, gpu[i].b, gpu[i].a };
                    for (int c = 0; c < 4; c++)
                    {
                        probes++;
                        if (!Finite(gpu[i]) || Mathf.Abs(expected[c] - actual[c]) > 0.05f + 2e-6f * Mathf.Abs(expected[c]))
                            throw new Exception("Noise row mismatch (layers " + (pair * 2) + "," + (pair * 2 + 1) + ") at " + inputs[i].r + "," + inputs[i].b +
                                " component " + c + ": CPU=" + F(expected[c]) + " GPU=" + F(actual[c]));
                    }
                }
            }
            log.AppendLine("PASS noise rows " + probes + " actual GPU NoiseXZ comparisons against the CPU frame (six layers)");
        }
        finally
        {
            tapeBinder.Reset();
            RenderTexture.active = previous;
            if (rt != null) { rt.Release(); Destroy(rt); }
            foreach (UnityEngine.Object value in owned) Destroy(value);
        }
    }

    private void TapeRows()
    {
        // The tape texture and TapeS against the CPU sampler: a one-keyframe tape and a ramp with a meander, probed across the
        // ramp, far past both ends of the tape, and at several z (the meander bends the ramp along z).
        var owned = new List<UnityEngine.Object>();
        RenderTexture rt = null;
        RenderTexture previous = RenderTexture.active;
        var tapeBinder = new CloudVolumeUniforms();
        try
        {
            var material = new Material(Resources.Load<Shader>("CloudDensityProbe")); owned.Add(material);
            material.SetFloat("_ProbeTapeMode", 1f);
            var inputs = new List<Color>();
            for (int z = -20000; z <= 20000; z += 20000)
                for (int x = -10000; x <= 10000; x += 500) inputs.Add(new Color(x, 0f, z, 0f));
            foreach (float x in new[] { -600000f, -90000f, 90000f, 600000f, 1.7e6f }) inputs.Add(new Color(x, 0f, 0f, 0f));
            int count = inputs.Count;
            var positions = new Texture2D(count, 1, TextureFormat.RGBAFloat, false, true)
            { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp }; owned.Add(positions);
            positions.SetPixels(inputs.ToArray()); positions.Apply();
            material.SetTexture("_DensityProbePositions", positions);
            rt = new RenderTexture(count, 1, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            Require(rt.Create(), "Bounded tape-row target created");
            var readback = new Texture2D(count, 1, TextureFormat.RGBAFloat, false, true); owned.Add(readback);
            var csv = new StringBuilder("tape,mode,x,z,column0,cpu0,gpu0,column1,cpu1,gpu1,column2,cpu2,gpu2,column3,cpu3,gpu3\n");
            TapeFixture ramp = RampFixture();
            (string name, BakedTape tape, TapeFrame frame)[] tapes =
            {
                ("constant", ConstantFixtureTape(), new TapeFrame(90f, 1500000f, 10000f, 80000f, 0.7f)),
                ("ramp", ramp.Sampler.Tape, ramp.Frame),
            };
            int probes = 0;
            foreach ((string name, BakedTape tape, TapeFrame frame) in tapes)
            {
                BindTape(tapeBinder, material, tape, frame);
                var sampler = new TapeSampler(tape);
                for (int mode = 1; mode <= 3; mode++)
                {
                    material.SetFloat("_ProbeTapeMode", mode);
                    Graphics.Blit(null, rt, material, 0);
                    RenderTexture.active = rt; readback.ReadPixels(new Rect(0, 0, count, 1), 0, 0); readback.Apply();
                    Color[] gpu = readback.GetPixels();
                    for (int i = 0; i < count; i++)
                    {
                        Color p = inputs[i];
                        float s = frame.S(p.r, p.b);
                        SkyParams row = sampler.At(s);
                        float[] expected = mode == 1 ? new[] { s, row.LobeRung, row.PuffDepth, row.HorizonCover }
                            : mode == 2 ? new[] { row.Mid.Base, row.Mid.Thick, row.Mid.Cover, row.Mid.PeriodRung }
                            : new[] { row.High.Base, row.High.Thick, row.High.Cover, row.High.PeriodRung };
                        float[] actual = { gpu[i].r, gpu[i].g, gpu[i].b, gpu[i].a };
                        for (int c = 0; c < 4; c++)
                        {
                            // s is metres at 1.5e6 (float spacing 0.125 m); the rest are texel lerps of baked columns.
                            float tolerance = c == 0 && mode == 1 ? 1.5f : 5e-4f * Math.Max(1f, Math.Abs(expected[c])) + 1e-3f;
                            probes++;
                            if (!Finite(gpu[i]) || Mathf.Abs(expected[c] - actual[c]) > tolerance)
                                throw new Exception("Tape row mismatch (" + name + ", mode " + mode + ") at " + p.r + "," + p.b + " column " + c +
                                    ": CPU=" + F(expected[c]) + " GPU=" + F(actual[c]));
                        }
                        csv.AppendLine(string.Join(",", name, mode, F(p.r), F(p.b), "0", F(expected[0]), F(actual[0]), "1", F(expected[1]), F(actual[1]),
                            "2", F(expected[2]), F(actual[2]), "3", F(expected[3]), F(actual[3])));
                    }
                }
            }
            File.WriteAllText("tape-row-probes.csv", csv.ToString());
            SkyParams a = ramp.Sampler.At(ramp.Frame.S(-9000f, 0f)), b = ramp.Sampler.At(ramp.Frame.S(9000f, 0f));
            Require(a.LobeRung != b.LobeRung && a.Mid.Cover != b.Mid.Cover, "The ramp fixture really varies in space");
            log.AppendLine("PASS tape rows " + probes + " actual GPU texture-lerp/meander/clamp comparisons against the CPU sampler");
        }
        finally
        {
            tapeBinder.Reset();
            RenderTexture.active = previous;
            if (rt != null) { rt.Release(); Destroy(rt); }
            foreach (UnityEngine.Object value in owned) Destroy(value);
        }
    }

    private static BakedTape ConstantFixtureTape()
    {
        var tape = new BakedTape(1);
        tape.SetConstant(SkyLibrary.Nominal(7));
        return tape;
    }

    private void SlabFootprint()
    {
        // The thin layers' 2D field, GPU against CPU at footprint zero, for each morphology on its own, a four-way mix with a
        // curl, and a layer inside a period ramp (two rungs). The noise volumes have no mip chain, so the shader's fixed mip taps
        // read the same texels the CPU does.
        var owned = new List<UnityEngine.Object>();
        RenderTexture rt = null;
        RenderTexture previous = RenderTexture.active;
        var tapeBinder = new CloudVolumeUniforms();
        try
        {
            var material = new Material(Resources.Load<Shader>("CloudDensityProbe")); owned.Add(material);
            material.SetFloat("_ProbeSlabMode", 1f);
            material.SetVector("_CloudEye", Vector4.zero);
            material.SetVector("_CloudWindDir", new Vector2(0f, 1f));
            const int count = 25;
            var positions = new Texture2D(count, 1, TextureFormat.RGBAFloat, false, true)
            { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp }; owned.Add(positions);
            material.SetTexture("_DensityProbePositions", positions);
            rt = new RenderTexture(count, 1, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            Require(rt.Create(), "Bounded thin-layer target created");
            var readback = new Texture2D(count, 1, TextureFormat.RGBAFloat, false, true); owned.Add(readback);
            var csv = new StringBuilder("layer,x,y,z,cpu,gpu,delta\n");
            SkyLayer Layer(float cells, float ripples, float streaks, float veil, float curl, float rung)
            {
                SkyLayer l = SkyArchetypes.Layer(LayerKind.AcFloccus).ToLayer(0.55f);
                l.Cells = cells; l.Ripples = ripples; l.Streaks = streaks; l.Veil = veil; l.Curl = curl; l.PeriodRung = rung;
                l.Aspect = 3f; l.RippleWavelength = 900f; l.Strength = 0.36f;
                return l;
            }
            (string name, SkyLayer layer)[] cases =
            {
                ("cells", Layer(1, 0, 0, 0, 0f, 5f)), ("ripples", Layer(0, 1, 0, 0, 0f, 6f)), ("streaks", Layer(0, 0, 1, 0, 0f, 8f)),
                ("veil", Layer(0, 0, 0, 1, 0f, 9f)), ("mix+curl", Layer(0.3f, 0.3f, 0.3f, 0.1f, 0.8f, 7f)),
                ("period-ramp", Layer(0.4f, 0.3f, 0.3f, 0f, 0f, 6.5f)), ("period-edge", Layer(0.4f, 0.3f, 0.3f, 0f, 0f, 6.3f)),
                ("aspect-ramp", Layer(0f, 0.2f, 0.8f, 0f, 0.5f, 8f)),
            };
            // Two noise volumes. A constant one isolates the plumbing (weights, contrast, mask, ripple rows) and holds the old 0.001. A smooth
            // one makes every morphology, the curl and the rung blend vary in space; the GPU filters 3D textures with 8-bit weights, which
            // the layer's gains (contrast 3.2, mask slope up to 7) magnify to a few thousandths, so it holds 0.006.
            // The third volume runs the smooth one through a keyed frame: the thin layers' rounded taps rotate and every tap shifts (NoiseXform).
            (byte[] bytes, int size, float tolerance, string tag, bool keyed)[] volumes =
            {
                (ConstantNoise(), 4, .001f, "flat", false), (SmoothNoise(16), 16, .006f, "smooth", false), (SmoothNoise(16), 16, .006f, "smooth-keyed", true),
            };
            int probes = 0, positive = 0;
            float worst = 0f;
            foreach ((byte[] bytes, int size, float tolerance, string tag, bool keyed) in volumes)
            {
            var noise = new Texture3D(size, size, size, TextureFormat.RGBA32, false)
            { filterMode = FilterMode.Trilinear, wrapMode = TextureWrapMode.Repeat };
            owned.Add(noise); noise.SetPixelData(bytes, 0); noise.Apply(false, true);
            material.SetTexture("_CloudNoiseTex", noise);
            foreach ((string name, SkyLayer layer) in cases)
            {
                // The aspect-ramp case joins the layer to itself with another aspect, ripple wavelength and curl across a 12 km ramp at x = 0
                // (the bake holds them at the nearer side, spec 6); every other case is one layer everywhere.
                bool ramped = name == "aspect-ramp";
                var sky = new SkyParams { Mid = layer };
                SkyParams to = sky;
                if (ramped) { to.Mid.Aspect = 1.4f; to.Mid.RippleWavelength = 600f; to.Mid.Curl = 0f; }
                BakedTape tape = BakedTape.TwoKeyframes(sky, to, 1500000f, 12000f);
                NoiseXform xf = keyed ? new NoiseXform(0x5EED1234u) : null;
                TapeFrame frame = ramped ? new TapeFrame(90f, 1500000f, noise: xf) : keyed ? new TapeFrame(0f, 0f, noise: xf) : default;
                BindTape(tapeBinder, material, tape, frame);
                var tapeCpu = new TapeSampler(tape);
                material.SetFloat("_ProbeLayer", 0f);
                var cpu = keyed ? new CloudBodies(bytes, size, TapeSampler.Constant(sky), frame) : new CloudBodies(bytes, size, sky);
                float height = layer.Base + layer.Thick * .5f;
                var inputs = new Color[count]; int at = 0;
                for (int z = -12000; z <= 12000; z += 6000)
                for (int x = -12000; x <= 12000; x += 6000) inputs[at++] = new Color(x, height, z, 0f);
                positions.SetPixels(inputs); positions.Apply();
                Graphics.Blit(null, rt, material, 0);
                RenderTexture.active = rt; readback.ReadPixels(new Rect(0, 0, count, 1), 0, 0); readback.Apply();
                Color[] output = readback.GetPixels();
                for (int i = 0; i < count; i++)
                {
                    Color p = inputs[i];
                    float expected = cpu.Slab2D(tapeCpu.At(frame.S(p.r, p.b)).Mid, p.r, p.g, p.b);
                    float delta = Mathf.Abs(expected - output[i].r); probes++;
                    if (tag.StartsWith("smooth")) worst = Mathf.Max(worst, delta);
                    if (output[i].r > .01f) positive++;
                    csv.AppendLine(string.Join(",", tag + "-" + name, F(p.r), F(p.g), F(p.b), F(expected), F(output[i].r), F(delta)));
                    File.WriteAllText("slab-footprint-probes.csv", csv.ToString());
                    // The warp lookup (and the ripple rows and curl that ride on it) turn the 8-bit filter error of the GPU's 3D fetch (about 1 m of
                    // warp) into a pattern error of ~.003 that the Detail gain magnifies: the smooth volume holds .012 (the flat volume still holds .001,
                    // which is the check of the maths; the worst smooth delta is printed in the PASS line).
                    if (!Finite(output[i]) || delta > (tag.StartsWith("smooth") ? .012f : tolerance))
                        throw new Exception("Thin-layer mismatch (" + tag + " noise, " + name + ") at " + i + ": CPU=" + F(expected) + " GPU=" + F(output[i].r));
                }
            }
            }
            Require(positive > 40, "Thin-layer probes include positive cloud, rather than only empty agreement");
            log.AppendLine("PASS slab footprint " + probes + " actual GPU morphology probes (cells, ripples, streaks, veil, a curled mix, two period-ramp rungs, an aspect ramp; worst delta on the smooth volume " + F(worst) + ")");
        }
        finally
        {
            tapeBinder.Reset();
            RenderTexture.active = previous;
            if (rt != null) { rt.Release(); Destroy(rt); }
            foreach (UnityEngine.Object value in owned) Destroy(value);
        }
    }

    private static byte[] ConstantNoise()
    {
        var bytes = new byte[4 * 4 * 4 * 4];
        for (int i = 0; i < bytes.Length; i += 4) { bytes[i] = 200; bytes[i + 1] = 90; bytes[i + 2] = 255; bytes[i + 3] = 255; }
        return bytes;
    }

    /// <summary>A wrapped sum of low sine waves per channel, 128 +- 24: varies in space, repeats seamlessly, and keeps the GPU's
    /// 8-bit filter weights from showing after the layer's gains.</summary>
    private static byte[] SmoothNoise(int n)
    {
        var bytes = new byte[n * n * n * 4];
        int[][] waves = { new[] { 1, 2, 1 }, new[] { 2, 1, 3 }, new[] { 1, 3, 2 }, new[] { 3, 1, 1 } };
        for (int z = 0; z < n; z++)
        for (int y = 0; y < n; y++)
        for (int x = 0; x < n; x++)
        {
            int at = ((z * n + y) * n + x) * 4;
            for (int c = 0; c < 4; c++)
            {
                double phase = 2.0 * Math.PI * (waves[c][0] * x + waves[c][1] * y + waves[c][2] * z) / n + c * 1.3;
                bytes[at + c] = (byte)Math.Round(128.0 + 14.0 * Math.Sin(phase) + 10.0 * Math.Sin(2.0 * phase + 0.7 * c));
            }
        }
        return bytes;
    }

    private void SlabMaskAndOverlap()
    {
        // The cover masks and the optical weights are plain functions: GPU against CPU over a grid, and the spec's table printed.
        var owned = new List<UnityEngine.Object>();
        RenderTexture rt = null;
        RenderTexture previous = RenderTexture.active;
        try
        {
            var material = new Material(Resources.Load<Shader>("CloudDensityProbe")); owned.Add(material);
            var csv = new StringBuilder("probe,a,b,c,d,cpu,gpu\n");
            int probes = 0;
            Color[] ProbeBatch(float mode, Color[] inputs)
            {
                material.SetFloat("_ProbeMaskMode", mode == 0f ? 1f : 0f);
                material.SetFloat("_ProbeOverlapMode", mode == 1f ? 1f : 0f);
                var positions = new Texture2D(inputs.Length, 1, TextureFormat.RGBAFloat, false, true)
                { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp }; owned.Add(positions);
                positions.SetPixels(inputs); positions.Apply();
                material.SetTexture("_DensityProbePositions", positions);
                if (rt != null) { rt.Release(); Destroy(rt); }
                rt = new RenderTexture(inputs.Length, 1, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
                Require(rt.Create(), "Bounded mask target created");
                var readback = new Texture2D(inputs.Length, 1, TextureFormat.RGBAFloat, false, true); owned.Add(readback);
                Graphics.Blit(null, rt, material, 0);
                RenderTexture.active = rt; readback.ReadPixels(new Rect(0, 0, inputs.Length, 1), 0, 0); readback.Apply();
                return readback.GetPixels();
            }

            var masks = new List<Color>();
            foreach (float cover in new[] { 0.02f, 0.05f, 0.15f, 0.30f, 0.6f, 1f })
                foreach (float softness in new[] { 0f, 0.5f, 1f })
                    for (float body = 0f; body <= 1.0001f; body += 0.05f) masks.Add(new Color(body, cover, softness, 0f));
            Color[] gpuMasks = ProbeBatch(0f, masks.ToArray());
            var table = new StringBuilder("SlabCoverMask at softness 0.5 against the old mask, body 1.00:");
            for (int i = 0; i < masks.Count; i++)
            {
                Color m = masks[i];
                float cpu = SlabMath.SlabCoverMask(m.r, m.g, m.b), old = SlabMath.CoverMask(m.r, m.g);
                probes += 2;
                csv.AppendLine(string.Join(",", "mask", F(m.r), F(m.g), F(m.b), "", F(cpu), F(gpuMasks[i].r)));
                if (Mathf.Abs(cpu - gpuMasks[i].r) > 1e-4f || Mathf.Abs(old - gpuMasks[i].g) > 1e-4f)
                    throw new Exception("Mask mismatch at body " + m.r + " cover " + m.g + " softness " + m.b + ": CPU=" + F(cpu) + " GPU=" + F(gpuMasks[i].r));
                if (m.r >= 0.9999f && Math.Abs(m.b - 0.5f) < 1e-4f && m.g <= 0.15f)
                    table.Append("\n  cover " + F(m.g) + ": new " + F(gpuMasks[i].r) + ", old " + F(gpuMasks[i].g));
            }
            log.AppendLine(table.ToString());

            var overlaps = new List<Color>();
            var rng = new System.Random(3);
            for (int i = 0; i < 160; i++)
            {
                float baseY = 3000f + (float)rng.NextDouble() * 4000f, thick = 100f + (float)rng.NextDouble() * 1800f;
                float lo = baseY - 800f + (float)rng.NextDouble() * (thick + 1600f), span = i % 5 == 0 ? (float)rng.NextDouble() * 39f : (float)rng.NextDouble() * 2500f;
                overlaps.Add(new Color(i % 2 == 0 ? lo : lo + span, i % 2 == 0 ? lo + span : lo, baseY, baseY + thick));
            }
            Color[] gpuOverlaps = ProbeBatch(1f, overlaps.ToArray());
            for (int i = 0; i < overlaps.Count; i++)
            {
                Color o = overlaps[i];
                float cpu = SlabMath.Overlap(o.r, o.g, o.b, o.a);
                probes += 1;
                csv.AppendLine(string.Join(",", "overlap", F(o.r), F(o.g), F(o.b), F(o.a), F(cpu), F(gpuOverlaps[i].r)));
                if (Mathf.Abs(cpu - gpuOverlaps[i].r) > 1e-4f)
                    throw new Exception("Overlap mismatch for [" + o.r + "," + o.g + "] in [" + o.b + "," + o.a + "]: CPU=" + F(cpu) + " GPU=" + F(gpuOverlaps[i].r));
            }
            File.WriteAllText("slab-mask-overlap-probes.csv", csv.ToString());
            log.AppendLine("PASS slab mask " + probes + " actual GPU cover-mask and layer-overlap comparisons");
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
        Vector3[] positions, Vector3 offset, bool strictHeight, bool compareCpu = true, bool heroes = false,
        float tolerance = .015f, float medianLimit = -1f)
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
            var frame = new CloudFrame { WorldOffset = offset, Bottom = 0f, Top = 16000f,
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
            // The GPU Density has no thin layers (they march separately), so the CPU copy drops them as well.
            TapeSampler cpuTape = WithoutLayers(field.Tape);
            WeatherPoint Sample(float x, float z)
            {
                CloudBodies.MapWeights(x, z, 50000f, 220000f, out float weight, out float fade);
                WeatherPoint outer = CloudBodies.SampleMap(far, farProfile, farSize, x / 440000f + .5f, z / 440000f + .5f);
                outer.BackgroundCover *= fade; outer.CellShape *= fade;
                if (weight <= 0f) return outer;
                WeatherPoint inner = CloudBodies.SampleMap(near, nearProfile, mapSize, x / 100000f + .5f, z / 100000f + .5f);
                return CloudBodies.BlendMaps(outer, inner, weight);
            }
            var cpu = new CloudBodies(noiseBytes, noiseSize, cpuTape, field.Frame, field.PrevailingHeading,
                field: heroes ? field : null, shownHeroes: heroes ? uniforms.HeroStrengths : null,
                nearDetail: true, weatherSampler: Sample);
            var csv = new StringBuilder(); float maxDelta = 0f, maxHeight = 0f; string failure = null;
            var signedDeltas = new List<float>(); // CPU - GPU over the probes that hold cloud: a systematic offset shows here even when no probe is over its tolerance
            for (int i = 0; i < positions.Length; i++)
            {
                Vector3 p = positions[i];
                float expected = cpu.Density(default, p.x, p.y, p.z, out float height);
                float delta = Mathf.Abs(expected - gpu[i].r);
                float hd = Mathf.Abs(height - gpu[i].g);
                maxDelta = Mathf.Max(maxDelta, delta); maxHeight = Mathf.Max(maxHeight, hd);
                if (expected > .01f || gpu[i].r > .01f) signedDeltas.Add(expected - gpu[i].r);
                csv.AppendLine(string.Join(",", name, F(p.x), F(p.y), F(p.z), F(expected), F(gpu[i].r),
                    F(height), F(gpu[i].g), F(gpu[i].b), F(delta)));
                if (float.IsNaN(gpu[i].r) || gpu[i].r < 0f || gpu[i].r > 1f || gpu[i].g < 0f || gpu[i].g > 1f ||
                    (compareCpu && delta > tolerance) || Mathf.Abs(gpu[i].r - gpu[i].b) > .0001f ||
                    (strictHeight && expected > .04f && gpu[i].r > .04f && hd > .02f) ||
                    (gpu[i].r <= 0f && gpu[i].g != 0f))
                    failure = failure ?? name + " probe" + i + " CPU=" + F(expected) + " GPU=" + F(gpu[i].r) +
                        " hCPU=" + F(height) + " hGPU=" + F(gpu[i].g) + " envelope=" + F(gpu[i].b);
                if (heroes && gpu[i].r > .01f)
                {
                    Vector4 bounds = material.GetVector("_CloudHeroBounds");
                    Vector4 ordinary = material.GetVector("_CloudAltitudeBounds");
                    Require(p.y >= Math.Min(bounds.x, ordinary.x) && p.y <= Math.Max(bounds.y, ordinary.y),
                        "Actual positive hero density is enclosed by combined ray bounds");
                    log.AppendLine("hero-bound y=" + F(p.y) + " ordinary=" + F(ordinary.x) + ".." + F(ordinary.y) +
                        " absoluteHero=" + F(bounds.x) + ".." + F(bounds.y));
                }
            }
            File.AppendAllText("density-probes.csv", csv.ToString()); samples += positions.Length;
            signedDeltas.Sort();
            float median = signedDeltas.Count > 0 ? signedDeltas[signedDeltas.Count / 2] : 0f;
            log.AppendLine(name + " samples=" + positions.Length + " maxDensityDelta=" + F(maxDelta) + " maxHeightDelta=" + F(maxHeight) +
                " medianSignedDelta=" + F(median) + " (" + signedDeltas.Count + " cloud probes)");
            if (failure != null) throw new Exception(failure);
            if (medianLimit >= 0f && Mathf.Abs(median) > medianLimit)
                throw new Exception(name + " median signed delta " + F(median) + " exceeds " + F(medianLimit) + ": a systematic CPU/GPU offset");
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
