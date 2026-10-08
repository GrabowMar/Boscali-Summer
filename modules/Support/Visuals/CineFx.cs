using UnityEngine;
using UnityEngine.Rendering;

namespace BoscaliSummer.Modules.Support.Visuals
{
    /// <summary>
    /// The shared kit behind the rod, EMP and flare effects: procedural billowy smoke / glow / ring textures, the four particle materials built on them, a layer builder with
    /// sane defaults and the little world helpers (wind, daylight, camera, origin-safe positions). Every layer is a Local-space particle system under a root that hangs on
    /// <c>Datum.origin</c>, so a floating-origin shift carries the whole effect along; callers emit with <see cref="ToSim"/> positions. Nothing here allocates per frame.
    /// </summary>
    internal static class CineFx
    {
        private static Texture2D smokeSheet, glowTexture, ringTexture;
        private static Material smoke, glow, ringAlpha, ringAdd;

        /// <summary>Billowy alpha-blended smoke / dust (2 x 2 sheet of four puffs; layers pick a random tile).</summary>
        public static Material Smoke => smoke != null ? smoke : smoke = Make("Cine.Smoke", false, SmokeSheet, 1f);
        /// <summary>Additive hot core glow (fire, flares, sparks, flashes).</summary>
        public static Material Glow => glow != null ? glow : glow = Make("Cine.Glow", true, GlowTexture, 1.6f);
        /// <summary>Soft expanding ring, alpha-blended (dust / pressure fronts).</summary>
        public static Material RingAlpha => ringAlpha != null ? ringAlpha : ringAlpha = Make("Cine.RingAlpha", false, RingTexture, 1f);
        /// <summary>Soft expanding ring, additive (ionisation shells, condensation shimmer).</summary>
        public static Material RingAdd => ringAdd != null ? ringAdd : ringAdd = Make("Cine.RingAdd", true, RingTexture, 1.3f);

        public static void Reset()
        {
            Destroy(ref smoke); Destroy(ref glow); Destroy(ref ringAlpha); Destroy(ref ringAdd);
            Destroy(ref smokeSheet); Destroy(ref glowTexture); Destroy(ref ringTexture);
        }

        private static void Destroy<T>(ref T o) where T : Object { if (o != null) Object.Destroy(o); o = null; }

        private static Material Make(string name, bool additive, Texture2D texture, float gain)
        {
            Material m = new Material(SupportParticles.Material(additive)) { name = name };
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", texture);
            if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", texture);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", new Color(gain, gain, gain, 1f));
            if (m.HasProperty("_Color")) m.SetColor("_Color", new Color(gain, gain, gain, 1f));
            return m;
        }

        // ---- procedural textures -------------------------------------------------------------------------------------------

        private static Texture2D SmokeSheet
        {
            get
            {
                if (smokeSheet != null) return smokeSheet;
                const int tile = 128, res = tile * 2;
                smokeSheet = NewTexture("Cine.SmokeSheet", res);
                var px = new Color[res * res];
                for (int k = 0; k < 4; k++)
                {
                    int ox = (k % 2) * tile, oy = (k / 2) * tile;
                    float seed = k * 37.17f + 3.1f;
                    for (int y = 0; y < tile; y++)
                        for (int x = 0; x < tile; x++)
                        {
                            float u = (x + 0.5f) / tile * 2f - 1f, v = (y + 0.5f) / tile * 2f - 1f;
                            float r = Mathf.Sqrt(u * u + v * v);
                            float mask = 1f - Mathf.SmoothStep(0.3f, 1f, r);
                            float n = Fbm(u * 1.9f + seed, v * 1.9f + seed * 0.7f);
                            float a = Mathf.Clamp01((n - 0.22f) * 2.3f) * mask;
                            a = Mathf.Pow(a, 0.85f);
                            // baked lobe shading: brighter towards the upper left, darker in the folds
                            float lit = Fbm(u * 1.9f + seed - 0.12f, v * 1.9f + seed * 0.7f + 0.12f);
                            float shade = Mathf.Clamp01(0.62f + (n - lit) * 3.2f + 0.2f * (1f - r));
                            px[(oy + y) * res + ox + x] = new Color(shade, shade, shade, a);
                        }
                }
                smokeSheet.SetPixels(px);
                smokeSheet.Apply(false, true);
                return smokeSheet;
            }
        }

        private static Texture2D GlowTexture
        {
            get
            {
                if (glowTexture != null) return glowTexture;
                const int res = 128;
                glowTexture = NewTexture("Cine.Glow", res);
                var px = new Color[res * res];
                for (int y = 0; y < res; y++)
                    for (int x = 0; x < res; x++)
                    {
                        float u = (x + 0.5f) / res * 2f - 1f, v = (y + 0.5f) / res * 2f - 1f;
                        float r = Mathf.Sqrt(u * u + v * v);
                        float a = Mathf.Exp(-r * r * 6f) + 0.32f * Mathf.Exp(-r * 3.2f);
                        a *= 1f - Mathf.SmoothStep(0.8f, 1f, r);
                        px[y * res + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(a));
                    }
                glowTexture.SetPixels(px);
                glowTexture.Apply(false, true);
                return glowTexture;
            }
        }

        private static Texture2D RingTexture
        {
            get
            {
                if (ringTexture != null) return ringTexture;
                const int res = 256;
                ringTexture = NewTexture("Cine.Ring", res);
                var px = new Color[res * res];
                for (int y = 0; y < res; y++)
                    for (int x = 0; x < res; x++)
                    {
                        float u = (x + 0.5f) / res * 2f - 1f, v = (y + 0.5f) / res * 2f - 1f;
                        float r = Mathf.Sqrt(u * u + v * v);
                        float d = (r - 0.88f) / 0.06f;
                        float a = Mathf.Exp(-d * d) + 0.16f * Mathf.SmoothStep(0.15f, 0.88f, r) * (1f - Mathf.SmoothStep(0.88f, 0.97f, r));
                        a *= 1f - Mathf.SmoothStep(0.95f, 1f, r);
                        a *= 0.82f + 0.18f * Mathf.PerlinNoise(Mathf.Atan2(v, u) * 5f + 10f, r * 6f);
                        px[y * res + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(a));
                    }
                ringTexture.SetPixels(px);
                ringTexture.Apply(false, true);
                return ringTexture;
            }
        }

        private static Texture2D NewTexture(string name, int res) =>
            new Texture2D(res, res, TextureFormat.RGBA32, false) { name = name, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };

        private static float Fbm(float x, float y)
        {
            float sum = 0f, amp = 0.5f, f = 1f;
            for (int o = 0; o < 4; o++) { sum += amp * Mathf.PerlinNoise(x * f + 11f, y * f + 5f); amp *= 0.5f; f *= 2.03f; }
            return sum / 0.9375f;
        }

        // ---- layers --------------------------------------------------------------------------------------------------------

        /// <summary>
        /// A manually driven layer: no emission, no shape, Local space, fading colour over life. Call <c>Emit</c> with <see cref="ToSim"/> positions. The default fade is
        /// in, hold, out; override <c>colorOverLifetime</c> for anything specific.
        /// </summary>
        public static ParticleSystem Layer(Transform root, string name, Material material, int max,
            ParticleSystemRenderMode mode = ParticleSystemRenderMode.Billboard, bool sheet = false, float lengthScale = 2f, float velocityScale = 0.03f)
        {
            var go = new GameObject(name);
            go.SetActive(false);
            go.transform.SetParent(root, false);
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.playOnAwake = false; main.loop = false; main.duration = 1f;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.maxParticles = max; main.startLifetime = 1f; main.startSpeed = 0f; main.startSize = 1f; main.startColor = Color.white;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            var emission = ps.emission; emission.enabled = false;
            var shape = ps.shape; shape.enabled = false;
            var life = ps.colorOverLifetime; life.enabled = true; life.color = Fade();
            if (sheet)
            {
                var tsa = ps.textureSheetAnimation;
                tsa.enabled = true; tsa.mode = ParticleSystemAnimationMode.Grid;
                tsa.numTilesX = 2; tsa.numTilesY = 2; tsa.animation = ParticleSystemAnimationType.WholeSheet;
                tsa.frameOverTime = new ParticleSystem.MinMaxCurve(0f);
                tsa.startFrame = new ParticleSystem.MinMaxCurve(0f, 0.999f);
            }
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = material; r.renderMode = mode;
            r.maxParticleSize = 6f; r.minParticleSize = 0f;
            r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
            r.sortMode = ParticleSystemSortMode.None;
            if (mode == ParticleSystemRenderMode.Stretch) { r.lengthScale = lengthScale; r.velocityScale = velocityScale; }
            go.SetActive(true);
            return ps;
        }

        /// <summary>Fade in over 0 to <paramref name="rise"/>, hold, fade out from <paramref name="fall"/> (all alpha only, colour white).</summary>
        public static Gradient Fade(float rise = 0.06f, float fall = 0.55f, float peak = 1f)
        {
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(peak, rise), new GradientAlphaKey(peak, fall), new GradientAlphaKey(0f, 1f) });
            return g;
        }

        public static Gradient Ramp(Color a, float ta, Color b, float tb, Color c, float tc, Color d, float td)
        {
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(a, ta), new GradientColorKey(b, tb), new GradientColorKey(c, tc), new GradientColorKey(d, td) },
                new[] { new GradientAlphaKey(a.a, ta), new GradientAlphaKey(b.a, tb), new GradientAlphaKey(c.a, tc), new GradientAlphaKey(d.a, td) });
            return g;
        }

        public static void Tint(ParticleSystem ps, Gradient gradient)
        {
            var c = ps.colorOverLifetime; c.enabled = true; c.color = gradient;
        }

        public static void Grow(ParticleSystem ps, float from, float to, float knee = 0.35f)
        {
            var s = ps.sizeOverLifetime; s.enabled = true;
            s.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, from), new Keyframe(knee, Mathf.Lerp(from, to, 0.7f)), new Keyframe(1f, to)));
        }

        public static void Drag(ParticleSystem ps, float dampen, float limit = 0f)
        {
            var l = ps.limitVelocityOverLifetime; l.enabled = true;
            l.limit = limit; l.dampen = dampen;
        }

        public static void Turbulence(ParticleSystem ps, float strength, float frequency)
        {
            var n = ps.noise; n.enabled = true;
            n.strength = strength; n.frequency = frequency; n.scrollSpeed = 0.25f; n.damping = true;
            n.quality = ParticleSystemNoiseQuality.Low; n.octaveCount = 2;
        }

        public static void Spin(ParticleSystem ps, float degreesPerSecond)
        {
            var r = ps.rotationOverLifetime; r.enabled = true;
            r.z = new ParticleSystem.MinMaxCurve(-degreesPerSecond * Mathf.Deg2Rad, degreesPerSecond * Mathf.Deg2Rad);
        }

        public static void Drift(ParticleSystem ps, Vector3 velocity)
        {
            var v = ps.velocityOverLifetime; v.enabled = true; v.space = ParticleSystemSimulationSpace.Local;
            v.x = velocity.x; v.y = velocity.y; v.z = velocity.z;
        }

        // ---- world helpers -------------------------------------------------------------------------------------------------

        /// <summary>A world position in the local particle space of a layer under <c>Datum.origin</c>.</summary>
        public static Vector3 ToSim(Vector3 world) => Datum.origin != null ? Datum.origin.InverseTransformPoint(world) : world;

        public static Vector3 Wind()
        {
            LevelInfo level = NetworkSceneSingleton<LevelInfo>.i;
            Vector3 w = level != null ? level.GetWind() : Vector3.zero;
            w.y = 0f;
            return w.sqrMagnitude < 0.25f ? new Vector3(2.5f, 0f, 1f) : Vector3.ClampMagnitude(w, 14f);
        }

        /// <summary>0 at night, 1 in full daylight.</summary>
        public static float Day01()
        {
            LevelInfo level = NetworkSceneSingleton<LevelInfo>.i;
            return level != null ? Mathf.InverseLerp(0.03f, 0.35f, level.GetAmbientLight()) : 1f;
        }

        public static Camera Cam()
        {
            var csm = SceneSingleton<CameraStateManager>.i;
            return csm != null && csm.mainCamera != null ? csm.mainCamera : Camera.main;
        }

        public static float CameraDistance(Vector3 world)
        {
            Camera c = Cam();
            return c != null ? Vector3.Distance(c.transform.position, world) : 2000f;
        }

        /// <summary>Camera shake scaled by range: strong inside a few km, nothing past <paramref name="reach"/>.</summary>
        public static void Shake(Vector3 world, float reach, float strength)
        {
            var csm = SceneSingleton<CameraStateManager>.i;
            if (csm == null) return;
            float k = Mathf.Clamp01(1f - CameraDistance(world) / reach);
            if (k > 0.02f) csm.ShakeCamera(Mathf.Lerp(0.2f, 3.4f, k * k) * strength, Mathf.Lerp(0.3f, 4.2f, k) * strength);
        }

        // ---- audio synthesis ----------------------------------------------------------------------------------------------

        private const int SampleRate = 44100;

        public static AudioClip Clip(string name, float seconds, System.Func<float, float> sample)
        {
            int n = (int)(SampleRate * seconds);
            var data = new float[n];
            for (int i = 0; i < n; i++) data[i] = Mathf.Clamp(sample(i / (float)SampleRate), -1f, 1f);
            var clip = AudioClip.Create(name, n, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        public static float Noise() => Random.value * 2f - 1f;

        public static AudioSource Speaker(GameObject go, AudioClip clip, float min, float max, float volume)
        {
            var s = go.AddComponent<AudioSource>();
            s.clip = clip; s.spatialBlend = 1f; s.minDistance = min; s.maxDistance = max; s.volume = volume;
            s.rolloffMode = AudioRolloffMode.Logarithmic; s.playOnAwake = false;
            return s;
        }
    }
}
