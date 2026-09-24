using UnityEngine;
using UnityEngine.Rendering;

namespace BoscaliSummer.Features.Weather.Visuals
{
    /// <summary>
    /// Shared procedural rain-streak sprite: one 4x32 gradient texture plus a transparent
    /// particle material with URP camera fading. Instances are owned by the caller, which
    /// must destroy them. Used by both the falling-rain and canopy-droplet emitters.
    /// </summary>
    internal static class RainStreakMaterial
    {
        internal static Texture2D CreateTexture()
        {
            // 4x32 procedural gradient with soft edges and cosine-bell fade
            var tex = new Texture2D(4, 32, TextureFormat.RGBA32, false)
            {
                name = "ProceduralRainStreak",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };

            Color32[] pixels = new Color32[4 * 32];
            for (int y = 0; y < 32; y++)
            {
                float v = y / 31f;
                float alpha = Mathf.Sin(v * Mathf.PI);
                alpha = Mathf.Pow(alpha, 1.3f);
                byte a = (byte)(alpha * 255f);

                for (int x = 0; x < 4; x++)
                {
                    float uDist = Mathf.Abs(x - 1.5f) / 1.5f;
                    byte finalA = (byte)(a * (1f - uDist * 0.35f));
                    pixels[y * 4 + x] = new Color32(240, 248, 255, finalA);
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply(false, true);
            return tex;
        }

        internal static Material CreateMaterial(Texture2D tex)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit")
                ?? Shader.Find("Particles/Standard Unlit")
                ?? Shader.Find("Sprites/Default");

            if (shader == null || !shader.isSupported) return null;

            var mat = new Material(shader) { name = "ProceduralRainStreakMat" };
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.renderQueue = (int)RenderQueue.Transparent;

            // URP transparent setup
            if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1);
            if (mat.HasProperty("_Blend")) mat.SetFloat("_Blend", 0);
            if (mat.HasProperty("_SrcBlend")) mat.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            if (mat.HasProperty("_DstBlend")) mat.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            if (mat.HasProperty("_ZWrite")) mat.SetInt("_ZWrite", 0);
            if (mat.HasProperty("_Cull")) mat.SetInt("_Cull", (int)CullMode.Off);

            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");

            // URP particle camera fading: drops dissolve as they approach the near plane instead of
            // rendering as giant soft disks on the lens. Requires the URP depth texture; silently
            // inactive otherwise, with maxParticleSize still bounding them.
            if (mat.HasProperty("_CameraFadingEnabled")) mat.SetFloat("_CameraFadingEnabled", 1f);
            if (mat.HasProperty("_CameraNearFadeDistance")) mat.SetFloat("_CameraNearFadeDistance", 0.7f);
            if (mat.HasProperty("_CameraFarFadeDistance")) mat.SetFloat("_CameraFarFadeDistance", 2.5f);
            mat.EnableKeyword("_FADING_ON");

            if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", tex);
            if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", tex);
            return mat;
        }
    }
}
