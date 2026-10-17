using UnityEngine;
using UnityEngine.Rendering;

namespace BoscaliSummer.Core.Fx
{
    /// <summary>
    /// Shared additive glow material for particle cores (flare heads, flashes, burns): a procedural
    /// gaussian blob texture on the URP particle shader with additive blending. Lives in Core so any
    /// feature can build hot-core effects without importing a sibling feature's visual kit.
    /// </summary>
    internal static class GlowFx
    {
        private static Texture2D texture;
        private static Material glow;

        /// <summary>Additive hot core glow. Callers copy it before tinting; never mutate the shared instance.</summary>
        public static Material Glow => glow != null ? glow : glow = Make();

        private static Material Make()
        {
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit")
                ?? Shader.Find("Particles/Standard Unlit") ?? Shader.Find("Sprites/Default");
            if (shader == null) return null;
            var m = new Material(shader) { name = "BoscaliSummer.Glow" };
            m.SetOverrideTag("RenderType", "Transparent");
            m.renderQueue = (int)RenderQueue.Transparent;
            Set(m, "_Surface", 1f); Set(m, "_Blend", 2f); Set(m, "_Mode", 4f);
            Set(m, "_SrcBlend", (float)BlendMode.SrcAlpha);
            Set(m, "_DstBlend", (float)BlendMode.One);
            Set(m, "_ZWrite", 0f); Set(m, "_Cull", (float)CullMode.Off);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.EnableKeyword("_ALPHABLEND_ON");
            m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", Texture);
            if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", Texture);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", new Color(1.6f, 1.6f, 1.6f, 1f));
            if (m.HasProperty("_Color")) m.SetColor("_Color", new Color(1.6f, 1.6f, 1.6f, 1f));
            return m;
        }

        private static void Set(Material m, string key, float value) { if (m.HasProperty(key)) m.SetFloat(key, value); }

        private static Texture2D Texture
        {
            get
            {
                if (texture != null) return texture;
                const int res = 128;
                texture = new Texture2D(res, res, TextureFormat.RGBA32, false)
                    { name = "BoscaliSummer.GlowTex", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
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
                texture.SetPixels(px);
                texture.Apply(false, true);
                return texture;
            }
        }
    }
}
