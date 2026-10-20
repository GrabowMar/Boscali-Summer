using BoscaliSummer.Modules.Weather.Domain;
using UnityEngine;
using UnityEngine.Rendering;

namespace BoscaliSummer.Modules.Weather.Visuals
{
    /// <summary>
    /// Night-sky phenomena (2026-10-18): aurora curtains low in the northern sky on clear nights, and red sprites and blue jets over
    /// distant night storms, fired by <c>LightningDirector</c>'s strikes. Two small native particle systems on one root that follows the
    /// camera like vanilla's starfield, so everything sits on an 18 km shell inside the far clip plane: direction and angular size are
    /// what the eye reads. At most 160 + 48 particles, no collisions, lights or shadows; nothing is built until a night needs it.
    /// Drawn additively without fog at queue 2996, after the stars and before the cloud composite, which hides them behind cloud.
    /// Client-local and cosmetic.
    /// </summary>
    internal sealed class SkyPhenomena
    {
        private const float Shell = 18000f;
        private const int AuroraMax = 160, SpriteMax = 48;
        private static readonly int IntensityId = Shader.PropertyToID("_Intensity");

        private GameObject root;
        private ParticleSystem aurora, sprites;
        private Material auroraMaterial, spriteMaterial;
        private Texture2D auroraTexture, spriteTexture;
        private bool failed;
        private float night;
        private uint seed = 1u;
        private int emitted;

        internal float AuroraLevel { get; private set; }
        internal int SpritesFired { get; private set; }
        internal int AuroraParticles => aurora != null ? aurora.particleCount : 0;

        /// <param name="sunUp">The sun direction's height, -1..1 (below the horizon is negative).</param>
        /// <param name="clearSky">0..1, how open the sky is around the camera.</param>
        /// <param name="auroraNight">0..1, this mission's aurora strength (0: none tonight).</param>
        internal void Tick(Camera camera, float sunUp, float clearSky, float auroraNight, uint missionSeed, float realTime)
        {
            if (Application.isBatchMode || camera == null) { Reset(); return; }
            seed = missionSeed;
            // 0 by day, 1 once the sun is ~8 degrees down (civil dusk keeps the sky too bright for either).
            night = WeatherMath.Smoothstep(-0.02f, -0.14f, sunUp);
            float target = night * Mathf.Clamp01(auroraNight) * WeatherMath.Smoothstep(0.25f, 0.75f, clearSky);
            AuroraLevel = Mathf.MoveTowards(AuroraLevel, target, Time.deltaTime * 0.05f);
            bool spritesLive = sprites != null && sprites.particleCount > 0;
            if (AuroraLevel <= 0.001f && !spritesLive && (aurora == null || aurora.particleCount == 0))
            {
                if (root != null && root.activeSelf) root.SetActive(false);
                return;
            }
            if (!Ensure()) return;
            if (!root.activeSelf) root.SetActive(true);
            root.transform.SetPositionAndRotation(camera.transform.position, Quaternion.identity);
            // A slow shimmer of the whole display (two incommensurate waves) on top of each curtain's own fade.
            float shimmer = 0.82f + 0.18f * Mathf.Sin(realTime * 0.7f) * Mathf.Sin(realTime * 1.9f + 1.3f);
            // 0.9 (first live run at 2.2 drew fat glowing columns and a bloom glow under them; real aurora is thin and translucent).
            auroraMaterial.SetFloat(IntensityId, 0.9f * AuroraLevel * shimmer);
            EmitAurora(Mathf.RoundToInt(AuroraMax * AuroraLevel));
        }

        /// <summary>A strike became visible. At night a tall storm 30-220 km away may throw a cluster of red sprites high above
        /// it, or a blue jet from its top. Pure of the strike's seed, so peers see the same ones.</summary>
        internal void Strike(Vector3 cameraGlobal, in LightningEvent strike)
        {
            if (night < 0.5f || strike.Top < 8000f) return;
            float dx = strike.X - cameraGlobal.x, dz = strike.Z - cameraGlobal.z;
            float range = Mathf.Sqrt(dx * dx + dz * dz);
            if (range < 30000f || range > 220000f) return;
            // Rare on purpose: a night storm line throws several strikes a second, and at 45 % (first live run) sprites flickered
            // constantly and stopped being an event. Now about one in fourteen eligible strikes, a jet one in seventy.
            float roll = WeatherMath.Hash01(strike.Seed, 3, 7);
            bool jet = roll < 0.015f;
            if (!jet && roll > 0.07f) return;
            if (!Ensure()) return;
            if (!root.activeSelf) root.SetActive(true);
            int count = jet ? 1 : 2 + (int)(WeatherMath.Hash01(strike.Seed, 4, 7) * 4f);
            for (int i = 0; i < count; i++)
            {
                float ox = WeatherMath.HashRange(strike.Seed, 5, i, 0, -9000f, 9000f);
                float oz = WeatherMath.HashRange(strike.Seed, 6, i, 0, -9000f, 9000f);
                // Sprites hang 50-85 km up and are ~30 km tall; a jet climbs ~20 km from the anvil.
                float altitude = jet ? strike.Top + 9000f : WeatherMath.HashRange(strike.Seed, 7, i, 0, 50000f, 85000f) - 15000f;
                float tall = jet ? 18000f : WeatherMath.HashRange(strike.Seed, 8, i, 0, 22000f, 34000f);
                Vector3 toward = new Vector3(dx + (jet ? 0f : ox), altitude - cameraGlobal.y, dz + (jet ? 0f : oz));
                float distance = toward.magnitude;
                if (distance < 1f) continue;
                float scale = Shell / distance;
                var p = new ParticleSystem.EmitParams
                {
                    position = toward * scale,
                    startSize3D = new Vector3(tall * (jet ? 0.22f : 0.33f) * scale, tall * scale, 1f),
                    startLifetime = WeatherMath.HashRange(strike.Seed, 9, i, 0, 0.14f, 0.32f),
                    // Deep red: with more green/blue (0.22/0.30, first live capture) the crown washed out to pinkish white.
                    startColor = jet ? new Color(0.35f, 0.55f, 1f, 1f) : new Color(1f, 0.12f, 0.18f, 1f),
                    applyShapeToPosition = false,
                };
                sprites.Emit(p, 1);
                SpritesFired++;
            }
        }

        private void EmitAurora(int target)
        {
            int missing = target - aurora.particleCount;
            if (missing <= 0) return;
            // A few per frame: the curtains build over seconds instead of popping in.
            int now = Mathf.Min(missing, 3);
            for (int i = 0; i < now; i++)
            {
                int n = emitted++;
                // Four folds of curtain across the northern sky, each a wavy arc; the folds are this mission's.
                int fold = n & 3;
                float foldAz = WeatherMath.HashRange(seed, 40, fold, 0, -65f, 65f);
                float u = WeatherMath.HashRange(seed, 41, n, 0, -1f, 1f);
                float az = (foldAz + u * 26f + 7f * Mathf.Sin(u * 5.1f + fold)) * Mathf.Deg2Rad;
                float baseEl = 7f + 5f * WeatherMath.Hash01(seed, 42, fold) + 2.5f * Mathf.Sin(u * 3.7f + fold * 1.7f);
                float tallDeg = 14f + 12f * WeatherMath.Hash01(seed, 43, n);
                float midEl = (baseEl + tallDeg * 0.5f) * Mathf.Deg2Rad;
                // North is +Z (sunset is due west, noon due south).
                Vector3 dir = new Vector3(Mathf.Sin(az) * Mathf.Cos(midEl), Mathf.Sin(midEl), Mathf.Cos(az) * Mathf.Cos(midEl));
                float height = 2f * Shell * Mathf.Tan(tallDeg * 0.5f * Mathf.Deg2Rad);
                float width = Shell * WeatherMath.HashRange(seed, 44, n, 0, 0.018f, 0.04f);
                float green = WeatherMath.HashRange(seed, 45, n, 0, 0.75f, 1f);
                var p = new ParticleSystem.EmitParams
                {
                    position = dir * Shell,
                    startSize3D = new Vector3(width, height, 1f),
                    startLifetime = WeatherMath.HashRange(seed, 46, n, 0, 14f, 26f),
                    // A slow sideways drift along the arc.
                    velocity = new Vector3(Mathf.Cos(az), 0f, -Mathf.Sin(az)) * WeatherMath.HashRange(seed, 47, n, 0, -45f, 45f),
                    startColor = new Color(0.55f + 0.45f * (1f - green), green, 0.7f + 0.3f * green, 1f),
                    applyShapeToPosition = false,
                };
                aurora.Emit(p, 1);
            }
        }

        private bool Ensure()
        {
            if (root != null) return true;
            if (failed) return false;
            Shader shader = CanopyShaderBundle.GetSkyGlowShader();
            if (shader == null) { failed = true; return false; }
            root = new GameObject("BoscaliSkyPhenomena") { hideFlags = HideFlags.DontSave };
            auroraTexture = AuroraTexture();
            spriteTexture = SpriteTexture();
            auroraMaterial = Glow(shader, auroraTexture, 0.9f);
            // 2.5 (live 2026-10-19: at 7 the crown blew out white with a wide bloom halo; sprites are dim and deep red).
            spriteMaterial = Glow(shader, spriteTexture, 2.5f);
            aurora = System("Aurora", AuroraMax, auroraMaterial, new[]
            {
                new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f), new GradientAlphaKey(0.7f, 0.55f),
                new GradientAlphaKey(1f, 0.7f), new GradientAlphaKey(0f, 1f),
            });
            sprites = System("Sprites", SpriteMax, spriteMaterial, new[]
            {
                new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.06f), new GradientAlphaKey(0.35f, 0.4f), new GradientAlphaKey(0f, 1f),
            });
            return true;
        }

        private ParticleSystem System(string name, int max, Material material, GradientAlphaKey[] alpha)
        {
            var obj = new GameObject(name);
            obj.transform.SetParent(root.transform, false);
            ParticleSystem ps = obj.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.playOnAwake = false;
            main.loop = true;
            main.maxParticles = max;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.startSize3D = true;
            main.startSpeed = 0f;
            main.gravityModifier = 0f;
            var emission = ps.emission;
            emission.rateOverTime = 0f;
            var shape = ps.shape;
            shape.enabled = false;
            var colour = ps.colorOverLifetime;
            colour.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) }, alpha);
            colour.color = gradient;
            var renderer = obj.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.VerticalBillboard;
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.maxParticleSize = 4f;
            ps.Play();
            return ps;
        }

        private static Material Glow(Shader shader, Texture2D texture, float intensity)
        {
            var material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave, renderQueue = 2996 };
            material.SetTexture("_MainTex", texture);
            material.SetFloat(IntensityId, intensity);
            return material;
        }

        /// <summary>One curtain strip: a sharp bright green lower edge, a body that fades upward into red-violet, fine vertical rays.</summary>
        private static Texture2D AuroraTexture()
        {
            const int w = 32, h = 128;
            var texture = new Texture2D(w, h, TextureFormat.RGBA32, true) { name = "BoscaliAurora", wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
            var pixels = new Color32[w * h];
            for (int y = 0; y < h; y++)
            {
                float v = (y + 0.5f) / h;
                float lower = WeatherMath.Smoothstep(0f, 0.07f, v);
                float body = Mathf.Exp(-v * 1.6f) * (1f - WeatherMath.Smoothstep(0.7f, 1f, v)) * 0.8f;
                float edge = Mathf.Exp(-Mathf.Pow((v - 0.06f) / 0.04f, 2f)) * 0.35f;
                float top = WeatherMath.Smoothstep(0.35f, 0.85f, v);
                Color tint = Color.Lerp(new Color(0.35f, 1f, 0.55f), new Color(0.85f, 0.25f, 0.6f), top);
                for (int x = 0; x < w; x++)
                {
                    float u = (x + 0.5f) / w;
                    float across = Mathf.Exp(-Mathf.Pow((u - 0.5f) / 0.28f, 2f));
                    float rays = 0.45f + 0.55f * WeatherMath.Hash01(17u, x, 0);
                    float a = Mathf.Clamp01(lower * (body + edge) * across * rays);
                    pixels[y * w + x] = new Color(tint.r, tint.g, tint.b, a);
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply(true, true);
            return texture;
        }

        /// <summary>A sprite: a diffuse red crown in the upper third, tendrils hanging below it that turn violet-blue at their tips.</summary>
        private static Texture2D SpriteTexture()
        {
            const int w = 32, h = 96;
            var texture = new Texture2D(w, h, TextureFormat.RGBA32, true) { name = "BoscaliSprite", wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
            var pixels = new Color32[w * h];
            for (int y = 0; y < h; y++)
            {
                float v = (y + 0.5f) / h;
                float crown = Mathf.Exp(-Mathf.Pow((v - 0.7f) / 0.12f, 2f));
                float hang = WeatherMath.Smoothstep(0.05f, 0.6f, v) * (1f - WeatherMath.Smoothstep(0.62f, 0.75f, v));
                Color tint = Color.Lerp(new Color(0.55f, 0.35f, 1f), Color.white, WeatherMath.Smoothstep(0.2f, 0.6f, v));
                for (int x = 0; x < w; x++)
                {
                    float u = (x + 0.5f) / w;
                    float head = crown * Mathf.Exp(-Mathf.Pow((u - 0.5f) / 0.3f, 2f));
                    float tendrils = 0f;
                    for (int k = 0; k < 5; k++)
                    {
                        float at = 0.3f + 0.1f * k + 0.04f * Mathf.Sin(v * 9f + k * 2.1f);
                        tendrils += Mathf.Exp(-Mathf.Pow((u - at) / 0.035f, 2f));
                    }
                    float a = Mathf.Clamp01(head + 0.6f * tendrils * hang);
                    pixels[y * w + x] = new Color(tint.r, tint.g, tint.b, a);
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply(true, true);
            return texture;
        }

        internal void Reset()
        {
            AuroraLevel = 0f;
            if (root != null) Object.Destroy(root);
            if (auroraMaterial != null) Object.Destroy(auroraMaterial);
            if (spriteMaterial != null) Object.Destroy(spriteMaterial);
            if (auroraTexture != null) Object.Destroy(auroraTexture);
            if (spriteTexture != null) Object.Destroy(spriteTexture);
            root = null; aurora = sprites = null;
            auroraMaterial = spriteMaterial = null;
            auroraTexture = spriteTexture = null;
        }
    }
}
