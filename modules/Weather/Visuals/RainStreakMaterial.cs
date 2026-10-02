using UnityEngine;
using UnityEngine.Rendering;

namespace BoscaliSummer.Modules.Weather.Visuals
{
    /// <summary>
    /// Shared procedural rain-streak sprite: one 8x32 gradient texture plus a transparent
    /// particle material with URP camera fading. Instances are owned by the caller, which
    /// must destroy them. Used by both the falling-rain and canopy-droplet emitters.
    /// </summary>
    internal static class RainStreakMaterial
    {
        internal static Texture2D CreateTexture()
        {
            // Transparent borders prevent a bright rectangular edge on stretched billboards.
            const int width = 8, height = 32;
            var tex = new Texture2D(width, height, TextureFormat.RGBA32, true)
            {
                name = "ProceduralRainStreak",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Trilinear
            };

            Color32[] pixels = new Color32[width * height];
            for (int y = 0; y < height; y++)
            {
                float v = y / (float)(height - 1);
                float alpha = Mathf.Max(0f, Mathf.Sin(v * Mathf.PI));
                alpha = Mathf.Pow(alpha, 1.3f);
                byte a = (byte)(alpha * 255f);

                for (int x = 0; x < width; x++)
                {
                    float crossFade = Mathf.Max(0f, Mathf.Sin(x / (float)(width - 1) * Mathf.PI));
                    byte finalA = (byte)(a * crossFade);
                    pixels[y * width + x] = new Color32(240, 248, 255, finalA);
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply(true, true);
            return tex;
        }

        internal static Material CreateMaterial(Texture2D tex, bool additive = false, bool cameraFade = true)
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
            if (mat.HasProperty("_DstBlend")) mat.SetInt("_DstBlend",
                (int)(additive ? BlendMode.One : BlendMode.OneMinusSrcAlpha));
            if (mat.HasProperty("_ZWrite")) mat.SetInt("_ZWrite", 0);
            if (mat.HasProperty("_Cull")) mat.SetInt("_Cull", (int)CullMode.Off);

            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            // Runtime-created materials do not run Unity's particle material inspector.
            // The built-in fallback needs its alpha variant as well as the blend factors.
            if (shader.name == "Particles/Standard Unlit") mat.EnableKeyword("_ALPHABLEND_ON");
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", Color.white);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", Color.white);

            // URP particle camera fading: drops dissolve as they approach the near plane instead of
            // rendering as giant soft disks on the lens. Camera fade uses particle eye depth;
            // it does not require a scene-depth texture or an extra screen-space pass.
            if (mat.HasProperty("_CameraFadingEnabled")) mat.SetFloat("_CameraFadingEnabled", cameraFade ? 1f : 0f);
            if (mat.HasProperty("_CameraNearFadeDistance")) mat.SetFloat("_CameraNearFadeDistance", 0.7f);
            if (mat.HasProperty("_CameraFarFadeDistance")) mat.SetFloat("_CameraFarFadeDistance", 2.5f);
            if (mat.HasProperty("_CameraFadeParams")) mat.SetVector("_CameraFadeParams", new Vector4(0.7f, 1f / 1.8f, 0f, 0f));
            if (cameraFade) mat.EnableKeyword("_FADING_ON");

            if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", tex);
            if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", tex);
            return mat;
        }
    }
}
