using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Immersion.Visuals
{
    /// <summary>
    /// Canopy frost (2026-11-06, idea from NoAtmoFX's wet glass, written from scratch): flying through cloud or
    /// precipitation below freezing rimes the canopy from the frame inward; it melts above +1 C and slowly sublimes
    /// in dry freezing air. One Screen Space Overlay image under the vanilla HUD, cockpit view only. The texture
    /// holds a fixed "reach" map (frame distance plus crystal noise); <see cref="Level"/> sets how far the rime has
    /// crept, re-baked only when it moves by a 25th.
    /// </summary>
    internal sealed class CanopyFrost
    {
        private const int Width = 384, Height = 216, Steps = 25;
        private GameObject root;
        private RawImage image;
        private Texture2D texture;
        private float[] reach;
        private byte[] crystal;
        private Color32[] pixels;
        private int baked = -1;

        internal float Level { get; private set; }
        internal bool Visible => image != null && image.enabled;

        /// <summary>One step of the rime: builds in freezing cloud or precipitation, melts warm, sublimes slowly in dry cold air.</summary>
        internal static float Step(float frost, float temperatureC, float moisture01, float dt)
        {
            if (temperatureC > 1f) return Mathf.Max(0f, frost - dt / 12f * Mathf.InverseLerp(1f, 6f, temperatureC) - dt / 40f);
            float freezing = Mathf.InverseLerp(0f, -10f, temperatureC);
            if (moisture01 > .1f) return Mathf.Min(1f, frost + dt / 25f * freezing * moisture01);
            return Mathf.Max(0f, frost - dt / 150f);
        }

        internal void Tick(bool enabled, bool cockpit, float temperatureC, float moisture01, float light01, float dt)
        {
            if (!enabled) { Level = 0f; Hide(); return; }
            Level = Step(Level, temperatureC, moisture01, dt);
            if (!cockpit || Level < .02f) { Hide(); return; }
            if (image == null && !Create()) return;
            int step = Mathf.RoundToInt(Level * Steps);
            if (step != baked) Bake(step / (float)Steps);
            image.color = new Color(light01, light01, light01, 1f);
            image.enabled = true;
        }

        private bool Create()
        {
            // A scene change destroys the canvas with the scene; the texture and reach map survive and are reused.
            baked = -1;
            if (texture != null) { UnderHudCanvas(); return true; }
            texture = new Texture2D(Width, Height, TextureFormat.RGBA32, false)
                { name = "Boscali.CanopyFrost", wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
            reach = new float[Width * Height];
            crystal = new byte[Width * Height];
            pixels = new Color32[Width * Height];
            for (int y = 0; y < Height; y++)
                for (int x = 0; x < Width; x++)
                {
                    float u = x / (float)(Width - 1), v = y / (float)(Height - 1);
                    // Distance from the canopy frame: the top and both sides. The lower part of the view is the
                    // instrument panel, inside the canopy, so the rime never grows there (v = 0 is the bottom).
                    float frame = Mathf.Min(Mathf.Min(u, 1f - u) * 2f, (1f - v) * 2.2f);
                    float noise = Mathf.PerlinNoise(u * 6f, v * 3.4f) * .6f + Mathf.PerlinNoise(u * 23f + 7f, v * 13f) * .4f;
                    // Feathery crystals: radial streaks from the centre.
                    float angle = Mathf.Atan2(v - .5f, (u - .5f) * 1.78f);
                    float feather = Mathf.PerlinNoise(angle * 9f + 3f, frame * 2f) * .35f;
                    int i = y * Width + x;
                    reach[i] = Mathf.Lerp(1f, Mathf.Clamp01(frame * .9f + noise * .25f - feather * .35f), Edge(.28f, .46f, v));
                    // Fine ice grain, high contrast, so it reads as crystals and not as a grey haze.
                    float grain = Mathf.PerlinNoise(u * 70f + 3f, v * 40f) * .6f + Mathf.PerlinNoise(u * 160f, v * 90f + 5f) * .4f;
                    crystal[i] = (byte)(Mathf.Clamp01((grain - .3f) * 1.8f) * 255f);
                }
            UnderHudCanvas();
            return true;
        }

        /// <summary>Shader-style smoothstep (Unity's Mathf.SmoothStep interpolates between its first two arguments instead).</summary>
        private static float Edge(float from, float to, float x)
        {
            float t = Mathf.Clamp01((x - from) / (to - from));
            return t * t * (3f - 2f * t);
        }

        private void UnderHudCanvas()
        {
            int order = VisorOverlay.UnderHud(out int layer, out _);
            root = new GameObject("Boscali.CanopyFrost");
            Canvas canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = order - 1;
            canvas.sortingLayerID = layer;
            var node = new GameObject("Frost", typeof(RectTransform));
            node.transform.SetParent(root.transform, false);
            var rect = (RectTransform)node.transform;
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
            image = node.AddComponent<RawImage>();
            image.raycastTarget = false;
            image.texture = texture;
            image.enabled = false;
        }

        private void Bake(float level)
        {
            baked = Mathf.RoundToInt(level * Steps);
            // Full rime reaches about two thirds of the way in: the middle of the windscreen stays clear.
            float edge = level * .72f;
            for (int i = 0; i < pixels.Length; i++)
            {
                float a = Mathf.Clamp01((edge - reach[i]) * 6f);
                // Thick and white at the frame, glassy grain where it is still creeping.
                float body = Mathf.Lerp(.25f + .75f * crystal[i] / 255f, 1f, Mathf.Clamp01((edge - reach[i]) * 2.5f));
                pixels[i] = new Color32(235, 242, 255, (byte)(a * body * 235f));
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, false);
        }

        internal void Hide()
        {
            if (image != null && image.enabled) image.enabled = false;
        }

        internal void Release()
        {
            if (root != null) Object.Destroy(root);
            if (texture != null) Object.Destroy(texture);
            root = null; image = null; texture = null; reach = null; crystal = null; pixels = null;
            baked = -1;
            Level = 0f;
        }
    }
}
