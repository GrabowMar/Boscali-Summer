// Authoring helpers for Assets/Fx particle prefabs. Editor-only, lives outside the Fx junction (never bundled).
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Rendering.Universal.ShaderGUI;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public static class FxKit
{
    public const string Tex = "Assets/Fx/Textures", Mats = "Assets/Fx/Materials", Prefabs = "Assets/Fx/Prefabs";
    public const string Previews = @"C:\Users\marci\dev\Boscali Summer\tools\ParticleFx\previews";

    // ---------- textures ----------
    static float Hash(int x, int y, int s) { unchecked { int h = x * 374761393 + y * 668265263 + s * 982451653; h = (h ^ (h >> 13)) * 1274126177; return ((h ^ (h >> 16)) & 0xffffff) / (float)0xffffff; } }
    static float VNoise(float x, float y, int s)
    {
        int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y); float fx = x - xi, fy = y - yi;
        fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy);
        return Mathf.Lerp(Mathf.Lerp(Hash(xi, yi, s), Hash(xi + 1, yi, s), fx), Mathf.Lerp(Hash(xi, yi + 1, s), Hash(xi + 1, yi + 1, s), fx), fy);
    }
    static float Fbm(float x, float y, int s) { float a = 0, amp = .5f; for (int o = 0; o < 5; o++) { a += amp * VNoise(x, y, s + o * 17); x *= 2.03f; y *= 2.03f; amp *= .5f; } return a; }

    delegate Color Px(float u, float v, int cell);
    static void Sheet(string name, int cw, int ch, int cols, int rows, Px f)
    {
        var t = new Texture2D(cw * cols, ch * rows, TextureFormat.RGBA32, false);
        var px = new Color[t.width * t.height];
        for (int r = 0; r < rows; r++) for (int c = 0; c < cols; c++)
            for (int y = 0; y < ch; y++) for (int x = 0; x < cw; x++)
            {
                // Unity sheet frame 0 is top-left; texture row 0 is bottom.
                int cell = (rows - 1 - r) * cols + c;
                var col = f((x + .5f) / cw, (y + .5f) / ch, cell);
                px[(r * ch + y) * t.width + c * cw + x] = col;
            }
        t.SetPixels(px); t.Apply();
        Directory.CreateDirectory(Tex);
        string path = Tex + "/" + name + ".png";
        File.WriteAllBytes(path, t.EncodeToPNG());
        Object.DestroyImmediate(t);
        AssetDatabase.ImportAsset(path);
        var imp = (TextureImporter)AssetImporter.GetAtPath(path);
        imp.alphaIsTransparency = true; imp.wrapMode = TextureWrapMode.Clamp; imp.mipmapEnabled = true;
        imp.textureCompression = TextureImporterCompression.Uncompressed; imp.sRGBTexture = true;
        imp.SaveAndReimport();
    }

    public static string MakeTextures()
    {
        // soft circle
        Sheet("T_SoftCircle", 128, 128, 1, 1, (u, v, c) => { float r = Mathf.Clamp01(new Vector2(u - .5f, v - .5f).magnitude * 2); float a = Mathf.Pow(1 - r, 2.2f); return new Color(1, 1, 1, a); });
        // spark: hot pinpoint core + soft halo
        Sheet("T_Spark", 64, 64, 1, 1, (u, v, c) => { float r = new Vector2(u - .5f, v - .5f).magnitude * 2; float a = Mathf.Clamp01(Mathf.Exp(-r * r * 18) + .35f * Mathf.Exp(-r * r * 4)) * Mathf.Clamp01((1 - r) * 4); return new Color(1, 1, 1, a); });
        // ember: irregular little blob
        Sheet("T_Ember", 64, 64, 1, 1, (u, v, c) => { var d = new Vector2(u - .5f, v - .5f); float r = d.magnitude * 2 * (0.8f + .4f * VNoise(Mathf.Atan2(d.y, d.x) * 2 + 9, 3, 5)); float a = Mathf.Clamp01(Mathf.Exp(-r * r * 9) * 1.2f) * Mathf.Clamp01((1 - r) * 3); return new Color(1, 1, 1, a); });
        // smoke: 2x2 noise puffs, shaded lighter towards the top
        Sheet("T_Smoke", 256, 256, 2, 2, (u, v, c) =>
        {
            float dx = u - .5f, dy = v - .5f; float r = Mathf.Sqrt(dx * dx + dy * dy) * 2;
            float n = Fbm(u * 4 + c * 7.3f, v * 4 + c * 3.1f, 11 + c);
            float edge = Mathf.Clamp01(1 - r + (n - .5f) * .9f);
            float a = Mathf.Clamp01(Mathf.Pow(edge, 1.1f) * (0.8f + 1.1f * n)) * Mathf.Clamp01((1 - r) * 5);
            float shade = Mathf.Clamp01(.72f + .35f * (v - .5f) + .25f * (n - .5f));
            return new Color(shade, shade, shade, a);
        });
        // debris: 2x2 irregular chunks, lit from top
        Sheet("T_Debris", 128, 128, 2, 2, (u, v, c) =>
        {
            var d = new Vector2(u - .5f, v - .5f) * 2; float ang = Mathf.Atan2(d.y, d.x); float r = d.magnitude;
            int k = 6 + c; float seg = (ang + Mathf.PI) / (2 * Mathf.PI) * k; int i0 = Mathf.FloorToInt(seg) % k; int i1 = (i0 + 1) % k;
            float a0 = (i0 + .3f * Hash(i0, c, 5)) / k * 2 * Mathf.PI - Mathf.PI, a1 = (i0 + 1 + .3f * Hash(i1, c, 5)) / k * 2 * Mathf.PI - Mathf.PI;
            var p0 = new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)) * (.4f + .55f * Hash(i0, c, 3)); var p1 = new Vector2(Mathf.Cos(a1), Mathf.Sin(a1)) * (.4f + .55f * Hash(i1, c, 3));
            var dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)); var e = p1 - p0; float den = dir.x * e.y - dir.y * e.x;
            float rad = Mathf.Abs(den) < 1e-5f ? p0.magnitude : (p0.x * e.y - p0.y * e.x) / den; rad = Mathf.Clamp(rad, .2f, .95f);
            float a = Mathf.Clamp01((rad - r) * 40);
            float sh = Mathf.Clamp01(.55f + .45f * (d.y / rad) + .2f * (VNoise(u * 12, v * 12, c) - .5f));
            return new Color(sh, sh * .95f, sh * .9f, a);
        });
        // shock ring
        Sheet("T_Ring", 256, 256, 1, 1, (u, v, c) => { float r = new Vector2(u - .5f, v - .5f).magnitude * 2; float a = Mathf.Exp(-Mathf.Pow((r - .78f) / .09f, 2)) + .18f * Mathf.Clamp01(r / .78f) * (r < .78f ? 1 : 0); a *= Mathf.Clamp01((1 - r) * 12); return new Color(1, 1, 1, Mathf.Clamp01(a)); });
        // bolts: 2x2 jagged vertical arcs with branches
        var bolts = new List<float[]>();
        Sheet("T_Bolt", 128, 512, 2, 2, (u, v, c) =>
        {
            float best = 9;
            float x = .5f + .22f * (Fbm(v * 3 + c * 5, c * 2.1f, 31) - .5f) * 2 + .06f * (VNoise(v * 40, c, 7) - .5f);
            best = Mathf.Abs(u - x) * 128;
            float bv = .3f + .25f * Hash(c, 1, 9); // branch from bv upward-ish
            if (v > bv && v < bv + .35f)
            {
                float t = (v - bv) / .35f; float bx = .5f + .22f * (Fbm(bv * 3 + c * 5, c * 2.1f, 31) - .5f) * 2 + (c % 2 == 0 ? 1 : -1) * t * .35f + .04f * (VNoise(v * 50, c + 3, 7) - .5f);
                best = Mathf.Min(best, Mathf.Abs(u - bx) * 128 * (1 + t));
            }
            float a = Mathf.Exp(-(best / 3.5f) * (best / 3.5f)) + .5f * Mathf.Exp(-best / 12f);
            a *= Mathf.Clamp01(Mathf.Min(v, 1 - v) * 10) * Mathf.Clamp01(Mathf.Min(u, 1 - u) * 12);
            return new Color(1, 1, 1, Mathf.Clamp01(a));
        });
        AssetDatabase.Refresh();
        return "textures ok";
    }

    // ---------- materials ----------
    public static Texture2D T(string n) => AssetDatabase.LoadAssetAtPath<Texture2D>(Tex + "/" + n + ".png");

    /// blend: 0 alpha, 2 additive
    public static Material Mat(string name, string tex, int blend, Color tint, bool lit = false)
    {
        Directory.CreateDirectory(Mats);
        string path = Mats + "/" + name + ".mat";
        var sh = Shader.Find(lit ? "Universal Render Pipeline/Particles/Lit" : "Universal Render Pipeline/Particles/Unlit");
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null) { m = new Material(sh); AssetDatabase.CreateAsset(m, path); }
        m.shader = sh;
        m.SetTexture("_BaseMap", T(tex));
        m.SetColor("_BaseColor", tint);
        m.SetFloat("_Surface", 1); m.SetFloat("_Blend", blend);
        m.SetFloat("_Cull", 0);
        m.SetFloat("_SoftParticlesEnabled", 0); m.SetFloat("_BlendModePreserveSpecular", 0); m.SetFloat("_QueueOffset", blend == 0 ? 0 : 1);
        BaseShaderGUI.SetMaterialKeywords(m, null, ParticleGUI.SetMaterialKeywords);
        m.renderQueue = blend == 0 ? 3000 : 3001;
        EditorUtility.SetDirty(m);
        return m;
    }

    // ---------- systems ----------
    public static GameObject Root(string id)
    {
        foreach (var go in Object.FindObjectsOfType<GameObject>()) if (go.name == id && go.transform.parent == null) Object.DestroyImmediate(go);
        var root = new GameObject(id);
        return root;
    }

    public static ParticleSystem Sys(GameObject parent, string name, Material mat, float duration = 1f)
    {
        var go = new GameObject(name); go.transform.SetParent(parent.transform, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var m = ps.main;
        m.duration = duration; m.loop = false; m.playOnAwake = true;
        m.simulationSpace = ParticleSystemSimulationSpace.Local; m.scalingMode = ParticleSystemScalingMode.Hierarchy;
        m.startSpeed = 0; m.startLifetime = 1; m.startSize = 1; m.maxParticles = 50;
        var e = ps.emission; e.rateOverTime = 0;
        var s = ps.shape; s.enabled = false;
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = mat; r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
        r.maxParticleSize = 10f; r.minParticleSize = 0f;
        return ps;
    }

    public static void Burst(ParticleSystem ps, float time, int min, int max, int cycles = 1, float interval = .05f)
    {
        var e = ps.emission; var list = new List<ParticleSystem.Burst>();
        var arr = new ParticleSystem.Burst[e.burstCount]; e.GetBursts(arr); list.AddRange(arr);
        list.Add(new ParticleSystem.Burst(time, (short)min, (short)max, cycles, interval));
        e.SetBursts(list.ToArray());
    }

    /// pairs of (time, value)
    public static ParticleSystem.MinMaxCurve Curve(float mult, params float[] tv)
    {
        var c = new AnimationCurve();
        for (int i = 0; i < tv.Length; i += 2) c.AddKey(tv[i], tv[i + 1]);
        for (int i = 0; i < c.length; i++) AnimationUtility.SetKeyLeftTangentMode(c, i, AnimationUtility.TangentMode.Auto);
        return new ParticleSystem.MinMaxCurve(mult, c);
    }

    public static Color Hex(string h) { ColorUtility.TryParseHtmlString(h, out var c); return c; }

    /// colors: "t:#rrggbb" entries; alphas: (t, a) pairs
    public static Gradient Grad(string[] cols, params float[] alphas)
    {
        var g = new Gradient();
        var ck = new GradientColorKey[cols.Length];
        for (int i = 0; i < cols.Length; i++) { var p = cols[i].Split(':'); ck[i] = new GradientColorKey(Hex(p[1]), float.Parse(p[0], System.Globalization.CultureInfo.InvariantCulture)); }
        var ak = new GradientAlphaKey[alphas.Length / 2];
        for (int i = 0; i < ak.Length; i++) ak[i] = new GradientAlphaKey(alphas[i * 2 + 1], alphas[i * 2]);
        g.SetKeys(ck, ak);
        return g;
    }

    public static void ColorLife(ParticleSystem ps, Gradient g) { var c = ps.colorOverLifetime; c.enabled = true; c.color = g; }
    public static void SizeLife(ParticleSystem ps, params float[] tv) { var s = ps.sizeOverLifetime; s.enabled = true; s.size = Curve(1, tv); }
    public static void Sheet2x2(ParticleSystem ps) { var t = ps.textureSheetAnimation; t.enabled = true; t.numTilesX = 2; t.numTilesY = 2; t.animation = ParticleSystemAnimationType.WholeSheet; t.frameOverTime = new ParticleSystem.MinMaxCurve(0); t.startFrame = new ParticleSystem.MinMaxCurve(0, 3.999f); t.cycleCount = 1; }
    public static void Drag(ParticleSystem ps, float drag, bool quadratic = false) { var l = ps.limitVelocityOverLifetime; l.enabled = true; l.limit = 100000; l.dampen = 0; l.drag = drag; l.multiplyDragByParticleSize = false; l.multiplyDragByParticleVelocity = quadratic; }
    public static void Noise(ParticleSystem ps, float strength, float freq, float scroll = .3f) { var n = ps.noise; n.enabled = true; n.strength = strength; n.frequency = freq; n.scrollSpeed = scroll; n.octaveCount = 2; n.quality = ParticleSystemNoiseQuality.Medium; }
    public static void Spin(ParticleSystem ps, float degMin, float degMax) { var m = ps.main; m.startRotation = new ParticleSystem.MinMaxCurve(0, Mathf.PI * 2); var r = ps.rotationOverLifetime; r.enabled = true; r.z = new ParticleSystem.MinMaxCurve(degMin * Mathf.Deg2Rad, degMax * Mathf.Deg2Rad); }

    public static void Trails(ParticleSystem ps, Material mat, float lifetime, float width, Gradient col, float minVertexDistance = 1f, float headW = 1f, float tailW = 0f)
    {
        var t = ps.trails; t.enabled = true; t.mode = ParticleSystemTrailMode.PerParticle; t.lifetime = lifetime; t.widthOverTrail = Curve(width, 0, headW, 1, tailW);
        t.colorOverTrail = col; t.inheritParticleColor = true; t.dieWithParticles = false; t.minVertexDistance = minVertexDistance; t.worldSpace = false; t.sizeAffectsWidth = false;
        t.textureMode = ParticleSystemTrailTextureMode.Stretch;
        ps.GetComponent<ParticleSystemRenderer>().trailMaterial = mat;
    }

    public static ParticleSystemRenderer R(ParticleSystem ps) => ps.GetComponent<ParticleSystemRenderer>();

    // ---------- preview ----------
    static GameObject Stage()
    {
        var stage = GameObject.Find("FxPreviewStage");
        if (stage != null) return stage;
        stage = new GameObject("FxPreviewStage");
        var ground = GameObject.CreatePrimitive(PrimitiveType.Plane); ground.name = "Ground"; ground.transform.SetParent(stage.transform); ground.transform.localScale = new Vector3(2000, 1, 2000);
        Object.DestroyImmediate(ground.GetComponent<Collider>());
        var gm = new Material(Shader.Find("Universal Render Pipeline/Lit")); gm.SetColor("_BaseColor", new Color(.26f, .28f, .22f)); gm.SetFloat("_Smoothness", 0);
        ground.GetComponent<Renderer>().sharedMaterial = gm;
        // scale posts: 10 m wide, 100 m tall markers every 100 m along +X
        for (int i = 1; i <= 3; i++) { var p = GameObject.CreatePrimitive(PrimitiveType.Cube); p.name = "Post" + i; Object.DestroyImmediate(p.GetComponent<Collider>()); p.transform.SetParent(stage.transform); p.transform.position = new Vector3(-i * 100, 25, 120); p.transform.localScale = new Vector3(6, 50, 6); p.GetComponent<Renderer>().sharedMaterial = gm; }
        var lg = new GameObject("Sun"); lg.transform.SetParent(stage.transform); var l = lg.AddComponent<Light>(); l.type = LightType.Directional; l.intensity = 1.0f; l.color = new Color(1, .95f, .88f); lg.transform.rotation = Quaternion.Euler(35, -40, 0);
        var cg = new GameObject("FxPreviewCam"); cg.transform.SetParent(stage.transform); var cam = cg.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(.17f, .2f, .26f); cam.farClipPlane = 20000; cam.nearClipPlane = 1; cam.fieldOfView = 40; cam.allowHDR = true;
        var ad = cg.AddComponent<UniversalAdditionalCameraData>(); ad.renderPostProcessing = true; ad.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
        var vg = new GameObject("Volume"); vg.transform.SetParent(stage.transform); var vol = vg.AddComponent<Volume>(); vol.isGlobal = true;
        var prof = ScriptableObject.CreateInstance<VolumeProfile>();
        var bloom = prof.Add<Bloom>(true); bloom.intensity.value = .6f; bloom.threshold.value = 1.1f; bloom.scatter.value = .65f;
        var tm = prof.Add<Tonemapping>(true); tm.mode.value = TonemappingMode.ACES;
        vol.sharedProfile = prof;
        RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = new Color(.35f, .38f, .45f);
        return stage;
    }

    /// Renders the root effect at the given times. dist/elev in metres, look at (0, lookY, 0). Returns written paths.
    public static string Preview(string id, float[] times, float dist, float elev, float lookY, float yaw = 0, string suffix = "", int w = 960, int h = 540)
    {
        Stage();
        var root = GameObject.Find(id);
        foreach (var o in Object.FindObjectsOfType<ParticleSystem>()) if (!o.transform.IsChildOf(root.transform)) o.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var cam = GameObject.Find("FxPreviewCam").GetComponent<Camera>();
        var dir = Quaternion.Euler(0, yaw, 0) * new Vector3(0, 0, -1);
        cam.transform.position = dir * dist + Vector3.up * elev;
        cam.transform.LookAt(new Vector3(0, lookY, 0));
        var rt = RenderTexture.GetTemporary(new RenderTextureDescriptor(w, h, RenderTextureFormat.ARGB32, 24) { sRGB = true, msaaSamples = 1 });
        cam.targetTexture = rt;
        var sb = new StringBuilder(); Directory.CreateDirectory(Previews);
        var tops = new List<ParticleSystem>(); foreach (var p in root.GetComponentsInChildren<ParticleSystem>()) tops.Add(p);
        foreach (var t in times)
        {
            foreach (var p in tops) { p.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear); p.useAutoRandomSeed = false; p.randomSeed = 1234; }
            foreach (var p in tops) if (p.transform.parent == root.transform && !IsSubEmitter(root, p)) p.Simulate(t, false, true, true);
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply();
            RenderTexture.active = null;
            string file = Path.Combine(Previews, id + suffix + "-t" + t.ToString("0.0#", System.Globalization.CultureInfo.InvariantCulture) + ".png");
            File.WriteAllBytes(file, tex.EncodeToPNG()); Object.DestroyImmediate(tex);
            int alive = 0; foreach (var p in tops) alive += p.particleCount;
            sb.AppendLine(file + " alive=" + alive);
        }
        foreach (var p in tops) p.useAutoRandomSeed = true;
        cam.targetTexture = null; RenderTexture.ReleaseTemporary(rt);
        return sb.ToString();
    }

    static bool IsSubEmitter(GameObject root, ParticleSystem p)
    {
        foreach (var q in root.GetComponentsInChildren<ParticleSystem>())
        { var se = q.subEmitters; if (!se.enabled) continue; for (int i = 0; i < se.subEmittersCount; i++) if (se.GetSubEmitterSystem(i) == p) return true; }
        return false;
    }

    public static string Stats(string id)
    {
        var root = GameObject.Find(id); var sb = new StringBuilder(); int total = 0; float life = 0;
        foreach (var p in root.GetComponentsInChildren<ParticleSystem>())
        {
            var m = p.main; total += m.maxParticles;
            float end = m.duration + m.startLifetime.constantMax + (p.trails.enabled ? p.trails.lifetime.constantMax : 0);
            if (m.startLifetime.mode == ParticleSystemCurveMode.Constant) end = m.duration + m.startLifetime.constant + (p.trails.enabled ? p.trails.lifetime.constantMax : 0);
            if (m.loop) sb.AppendLine("LOOPING! " + p.name);
            life = Mathf.Max(life, end);
            sb.AppendLine(p.name + " max=" + m.maxParticles + " dur=" + m.duration + " end<=" + end.ToString("0.0"));
        }
        sb.AppendLine("TOTAL max=" + total + " lifetime<=" + life.ToString("0.0"));
        return sb.ToString();
    }

    public static string Save(string id)
    {
        var root = GameObject.Find(id);
        root.transform.position = Vector3.zero; root.transform.rotation = Quaternion.identity; root.transform.localScale = Vector3.one;
        foreach (var p in root.GetComponentsInChildren<ParticleSystem>()) { p.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear); p.useAutoRandomSeed = true; }
        Directory.CreateDirectory(Prefabs);
        AssetDatabase.SaveAssets();
        string path = Prefabs + "/" + id + ".prefab";
        PrefabUtility.SaveAsPrefabAsset(root, path, out bool ok);
        return path + " saved=" + ok;
    }

    // ---------- pipeline for previews (project-level, outside Assets/Fx) ----------
    public static string EnsurePipeline()
    {
        if (GraphicsSettings.defaultRenderPipeline != null) return "pipeline already " + GraphicsSettings.defaultRenderPipeline.name;
        Directory.CreateDirectory("Assets/FxAuthoring/Pipeline");
        var rd = ScriptableObject.CreateInstance<UniversalRendererData>();
        rd.postProcessData = AssetDatabase.LoadAssetAtPath<PostProcessData>("Packages/com.unity.render-pipelines.universal/Runtime/Data/PostProcessData.asset");
        AssetDatabase.CreateAsset(rd, "Assets/FxAuthoring/Pipeline/PreviewRenderer.asset");
        var asset = UniversalRenderPipelineAsset.Create(rd);
        asset.supportsHDR = true;
        AssetDatabase.CreateAsset(asset, "Assets/FxAuthoring/Pipeline/PreviewURP.asset");
        GraphicsSettings.defaultRenderPipeline = asset;
        QualitySettings.renderPipeline = asset;
        AssetDatabase.SaveAssets();
        return "pipeline created";
    }
}
