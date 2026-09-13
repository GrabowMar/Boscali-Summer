using UnityEngine;

namespace BoscaliSummer.Features.Support.Visuals
{
    /// <summary>
    /// Procedurally synthesizes and caches high-fidelity volumetric particle textures.
    /// Eliminates hard-edged square quad rendering in Unity URP when particle materials
    /// do not have pre-authored textures assigned.
    /// </summary>
    internal static class ProceduralVfxTextures
    {
        private static Texture2D softPuffTexture;

        /// <summary>
        /// A 64x64 RGBA32 texture with a smooth radial cosine-bell alpha curve.
        /// Turns flat particle billboard quads into soft, organic smoke/dust puffs.
        /// </summary>
        public static Texture2D SoftPuffTexture
        {
            get
            {
                if (softPuffTexture == null)
                {
                    const int res = 128;
                    softPuffTexture = new Texture2D(res, res, TextureFormat.RGBA32, false)
                    {
                        name = "BoscaliProceduralSoftPuff",
                        wrapMode = TextureWrapMode.Clamp,
                        filterMode = FilterMode.Bilinear
                    };

                    Color[] pixels = new Color[res * res];
                    float center = (res - 1) * 0.5f;
                    float maxDist = center;

                    for (int y = 0; y < res; y++)
                    {
                        for (int x = 0; x < res; x++)
                        {
                            float dist = Vector2.Distance(new Vector2(x, y), new Vector2(center, center));
                            float norm = Mathf.Clamp01(dist / maxDist);

                            // Smooth cosine / smoothstep bell-curve falloff with zero-alpha boundary
                            float alpha = norm >= 1f ? 0f : Mathf.Clamp01(0.5f * (1f + Mathf.Cos(norm * Mathf.PI)));
                            float noise = 0.55f + 0.3f * Mathf.PerlinNoise(x * 0.07f, y * 0.07f)
                                + 0.15f * Mathf.PerlinNoise(x * 0.19f + 7f, y * 0.19f);
                            alpha = alpha * alpha * noise; // Zero-alpha edges, irregular interior.

                            pixels[y * res + x] = new Color(1f, 1f, 1f, alpha);
                        }
                    }

                    softPuffTexture.SetPixels(pixels);
                    softPuffTexture.Apply(false, true);
                }

                return softPuffTexture;
            }
        }
    }
}
