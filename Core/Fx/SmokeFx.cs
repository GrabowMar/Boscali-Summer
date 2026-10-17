using UnityEngine;
using UnityEngine.Rendering;

namespace BoscaliSummer.Core.Fx
{
    /// <summary>
    /// Shared soft smoke puff: one cached texture and one cached alpha-blended particle material for
    /// every smoke-like effect (landing smoke, HALO trails). The procedural soft disc replaces the
    /// default white square, so puffs read as smoke rather than hard quads, and all of them batch
    /// through one material.
    /// </summary>
    internal static class SmokeFx
    {
        private static Texture2D puff;
        private static Material material;

        /// <summary>Alpha-blended, unlit, camera-facing puff material. Callers copy before tinting; never mutate the shared instance.</summary>
        public static Material Puff => material != null ? material : material = Make();

        private static Material Make()
        {
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find("Sprites/Default");
            if (shader == null) return null;
            var m = new Material(shader) { name = "BoscaliSummer.SmokePuff", renderQueue = (int)RenderQueue.Transparent };
            m.SetOverrideTag("RenderType", "Transparent");
            Set(m, "_Surface", 1f);
            Set(m, "_Blend", 0f);
            Set(m, "_SrcBlend", (float)BlendMode.SrcAlpha);
            Set(m, "_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            Set(m, "_ZWrite", 0f);
            Set(m, "_Cull", (float)CullMode.Off);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.EnableKeyword("_ALPHABLEND_ON");
            m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", Texture);
            if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", Texture);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", Color.white);
            if (m.HasProperty("_Color")) m.SetColor("_Color", Color.white);
            return m;
        }

        private static void Set(Material m, string key, float value) { if (m.HasProperty(key)) m.SetFloat(key, value); }

        private static Texture2D Texture
        {
            get
            {
                if (puff != null) return puff;
                const int res = 64;
                puff = new Texture2D(res, res, TextureFormat.RGBA32, false)
                    { name = "BoscaliSummer.SmokePuffTex", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
                var px = new Color[res * res];
                for (int y = 0; y < res; y++)
                    for (int x = 0; x < res; x++)
                    {
                        float u = (x + 0.5f) / res * 2f - 1f, v = (y + 0.5f) / res * 2f - 1f;
                        float r = Mathf.Sqrt(u * u + v * v);
                        // Soft rim to zero alpha, with a little interior grain so puffs are not perfect discs.
                        float rim = 1f - Mathf.SmoothStep(0.35f, 1f, r);
                        float grain = 0.8f + 0.2f * Mathf.PerlinNoise(x * 0.31f, y * 0.31f);
                        px[y * res + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(rim * rim * grain));
                    }
                puff.SetPixels(px);
                puff.Apply(false, true);
                return puff;
            }
        }
    }
}
