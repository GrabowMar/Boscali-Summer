using System.Collections.Generic;
using BoscaliSummer.Core.Fx;
using UnityEngine;
using UnityEngine.Rendering;

namespace BoscaliSummer.Modules.Immersion.Visuals
{
    /// <summary>
    /// Fixed points of light (airfield and city lights, 2026-11-06): one additive glow material shared by every
    /// set and one paused particle system per set, in Datum space with global coordinates, so a set costs one draw
    /// and keeps a small minimum size on screen far away. A set never moves; its colours can be rewritten
    /// (flashers, PAPI). <see cref="Night"/> fades every set in at dusk and out at dawn.
    /// </summary>
    internal static class LightPoints
    {
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor"), ColorId = Shader.PropertyToID("_Color");
        private static Material glow;
        private static ParticleSystem.Particle[] buffer = new ParticleSystem.Particle[64];

        /// <summary>Additive soft dot, copied from the shared smoke puff so it batches with the same shader.</summary>
        internal static Material Glow
        {
            get
            {
                if (glow != null) return glow;
                Material puff = SmokeFx.Puff;
                if (puff == null) return null;
                glow = new Material(puff) { name = "BoscaliLightPoint", hideFlags = HideFlags.HideAndDontSave };
                if (glow.HasProperty("_Blend")) glow.SetFloat("_Blend", 2f);
                if (glow.HasProperty("_DstBlend")) glow.SetFloat("_DstBlend", (float)BlendMode.One);
                const int res = 32;
                var dot = new Texture2D(res, res, TextureFormat.RGBA32, false)
                    { name = "BoscaliLightPointTex", wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
                var px = new Color32[res * res];
                for (int y = 0; y < res; y++)
                    for (int x = 0; x < res; x++)
                    {
                        float u = (x + .5f) / res * 2f - 1f, v = (y + .5f) / res * 2f - 1f, r2 = u * u + v * v;
                        // A hot core and a faint halo: reads as a lamp, not a disc.
                        float a = Mathf.Clamp01(Mathf.Exp(-r2 * 14f) + .35f * Mathf.Exp(-r2 * 3f) - .02f);
                        px[y * res + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                    }
                dot.SetPixels32(px);
                dot.Apply(false, true);
                if (glow.HasProperty("_BaseMap")) glow.SetTexture("_BaseMap", dot);
                if (glow.HasProperty("_MainTex")) glow.SetTexture("_MainTex", dot);
                Night(0f);
                return glow;
            }
        }

        /// <summary>0 day (lights off) to 1 full night; values above 1 overdrive into bloom.</summary>
        internal static void Night(float level)
        {
            if (glow == null) return;
            var c = new Color(2.6f * level, 2.6f * level, 2.6f * level, Mathf.Clamp01(level));
            if (glow.HasProperty(BaseColor)) glow.SetColor(BaseColor, c);
            if (glow.HasProperty(ColorId)) glow.SetColor(ColorId, c);
        }

        /// <summary>0 by day, 1 once the sun is 4 degrees under the horizon (lights come on in the dusk, as in a city).</summary>
        internal static float NightLevel()
        {
            Light sun = RenderSettings.sun;
            if (sun == null) return 0f;
            float elevation = Vector3.Dot(-sun.transform.forward, Vector3.up);
            return Mathf.InverseLerp(.1f, -.07f, elevation);
        }

        /// <summary>
        /// A paused set of points under <paramref name="parent"/> (Datum space). <paramref name="size"/> is metres;
        /// min/max are fractions of the screen height. Null when the shader is missing or the list is empty.
        /// </summary>
        internal static ParticleSystem Build(Transform parent, string name, List<Vector3> points, List<Color32> colors,
            float size, float minScreen, float maxScreen)
        {
            Material mat = Glow;
            if (mat == null || points.Count == 0) return null;
            var go = new GameObject(name) { hideFlags = HideFlags.DontSave };
            go.transform.SetParent(parent, false);
            ParticleSystem ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.playOnAwake = false;
            main.loop = false;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.startSpeed = 0f;
            main.startLifetime = 1e6f;
            main.maxParticles = points.Count;
            var emission = ps.emission;
            emission.enabled = false;
            var shape = ps.shape;
            shape.enabled = false;
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = mat;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.minParticleSize = minScreen;
            renderer.maxParticleSize = maxScreen;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            ps.Play();
            for (int i = 0; i < points.Count; i++)
                ps.Emit(new ParticleSystem.EmitParams
                {
                    position = points[i], startColor = colors[i], startSize = size, startLifetime = 1e6f,
                    applyShapeToPosition = false,
                }, 1);
            // One tiny step computes the bounds; without it the set is culled to the emitter's default box.
            ps.Simulate(.001f, false, false, true);
            ps.Pause();
            return ps;
        }

        /// <summary>Rewrites the colours of a set built by <see cref="Build"/>, in emission order.</summary>
        internal static void Recolor(ParticleSystem ps, Color32[] colors)
        {
            if (ps == null) return;
            if (buffer.Length < colors.Length) buffer = new ParticleSystem.Particle[colors.Length];
            int n = ps.GetParticles(buffer);
            for (int i = 0; i < n && i < colors.Length; i++) buffer[i].startColor = colors[i];
            ps.SetParticles(buffer, n);
        }

        internal static void Release()
        {
            if (glow != null) Object.Destroy(glow);
            glow = null;
        }
    }
}
