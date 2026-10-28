using UnityEngine;

namespace NOAvionics
{
    /// <summary>
    /// Cached panel and control masks. Widget masks are white so the
    /// shared palette is applied once by Image.color, rather than multiplied into baked ink.
    /// </summary>
    public static class AvSprites
    {
        private static Sprite groundGradientSprite;
        private static Sprite displayGlassSprite;
        private static Sprite displayScreenSprite;
        private static Sprite whiteSprite;

        /// <summary>Untinted rectangle for Image.Filled, which requires an actual sprite.</summary>
        public static Sprite White => whiteSprite != null ? whiteSprite :
            (whiteSprite = Sprite.Create(Texture2D.whiteTexture,
                new Rect(0f, 0f, Texture2D.whiteTexture.width, Texture2D.whiteTexture.height),
                new Vector2(0.5f, 0.5f), 100f, 0u, SpriteMeshType.FullRect));

        /// <summary>
        /// The same top-to-bottom fade as the panel sprite, but flat and unframed — for
        /// grounds drawn behind content that already has its own frame (the game's own stock
        /// map panels), where a second chamfered border would double up.
        /// </summary>
        public static Sprite GroundGradient => groundGradientSprite != null ? groundGradientSprite : (groundGradientSprite = CreateGradientSprite("Avionics_GroundGradient", 64));

        /// <summary>One low-opacity glass finish for the entire MFD face. It is cached,
        /// so every screen shares the same tiny texture instead of baking a panel-sized one.</summary>
        public static Sprite DisplayGlass => displayGlassSprite != null ? displayGlassSprite :
            (displayGlassSprite = CreateDisplayGlassSprite());

        /// <summary>Fine, low-contrast finish for the complete maximized MFD canvas.</summary>
        public static Sprite DisplayScreen => displayScreenSprite != null ? displayScreenSprite :
            (displayScreenSprite = CreateDisplayScreenSprite());

        /// <summary>Fraction of the way up the panel (0 bottom, 1 top) below which the fill
        /// stays at full alpha; the fade is spent entirely on the remaining bottom slice, so
        /// most of a panel reads as solid and only its lower edge admits what's behind it.</summary>
        private const float GradientOpaqueUntil = 0.20f;
        private const float GradientFloorFactor = 0.96f;

        private static Color FillAt(int y, int size)
        {
            float t = size <= 1 ? 1f : y / (float)(size - 1);   // 0 at the bottom row, 1 at the top

            Color ground = AvTheme.Ground;

            float fade = t >= GradientOpaqueUntil
                ? 1f
                : Mathf.Clamp01(t / GradientOpaqueUntil);
            ground.a *= Mathf.Lerp(GradientFloorFactor, 1f, fade);
            return ground;
        }

        private static Texture2D NewTexture(string name, int width, int height) =>
            new Texture2D(width, height, TextureFormat.RGBA32, mipChain: false)
            {
                name = name,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave,
            };

        /// <summary>Bakes the texture for good and wraps it as a full-rect sprite named after it.</summary>
        private static Sprite Bake(Texture2D texture)
        {
            texture.Apply(updateMipmaps: false, makeNoLongerReadable: true);
            Sprite sprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height),
                new Vector2(0.5f, 0.5f), 100f, 0u, SpriteMeshType.FullRect);
            sprite.name = texture.name;
            sprite.hideFlags = HideFlags.HideAndDontSave;
            return sprite;
        }

        /// <summary>An unframed, unsliced vertical fade — the panel sprite's fill without
        /// its chamfered border, for content that draws its own frame.</summary>
        private static Sprite CreateGradientSprite(string name, int size)
        {
            Texture2D texture = NewTexture(name, 1, size);

            for (int y = 0; y < size; y++)
                texture.SetPixel(0, y, FillAt(y, size));

            return Bake(texture);
        }

        private static Sprite CreateDisplayGlassSprite()
        {
            const int size = 96;
            Texture2D texture = NewTexture("Avionics_DisplayGlass", size, size);

            for (int y = 0; y < size; y++)
            {
                float v = (y + 0.5f) / size;
                for (int x = 0; x < size; x++)
                {
                    float u = (x + 0.5f) / size;
                    float edgeDistance = Mathf.Min(Mathf.Min(u, 1f - u), Mathf.Min(v, 1f - v));
                    float edge = 1f - Mathf.SmoothStep(0f, 0.15f, edgeDistance);
                    float shade = 0.012f + edge * 0.036f + (1f - v) * 0.014f;

                    // A broad reflection and a softer diagonal lip, not scanlines over copy.
                    float dx = (u - 0.18f) / 0.58f;
                    float dy = (v - 0.94f) / 0.30f;
                    float reflection = 0.026f * Mathf.Exp(-2f * (dx * dx + dy * dy));
                    float diagonal = (v - (1.08f - u * 0.43f)) / 0.16f;
                    reflection += 0.018f * Mathf.Exp(-diagonal * diagonal);

                    float alpha = shade + reflection * (1f - shade);
                    float highlight = reflection / Mathf.Max(alpha, 0.001f);
                    texture.SetPixel(x, y, new Color(
                        highlight * 0.76f, highlight * 0.88f, highlight, alpha));
                }
            }

            return Bake(texture);
        }

        private static Sprite CreateDisplayScreenSprite()
        {
            // One cached half-megabyte texture and one UI quad. There is no screen copy,
            // animated noise, postprocess camera, or per-frame texture upload.
            const int width = 256;
            const int height = 512;
            Texture2D texture = NewTexture("Avionics_DisplayScreen", width, height);
            var pixels = new Color32[width * height];

            for (int y = 0; y < height; y++)
            {
                float v = (y + 0.5f) / height;
                for (int x = 0; x < width; x++)
                {
                    float u = (x + 0.5f) / width;
                    float edgeDistance = Mathf.Min(Mathf.Min(u, 1f - u), Mathf.Min(v, 1f - v));
                    float edgeT = Mathf.Clamp01(edgeDistance / 0.15f);
                    float edge = 1f - edgeT * edgeT * (3f - 2f * edgeT);
                    float scan = (y & 1) == 0 ? 0.006f : 0f;
                    float grain = ((x * 73 + y * 151) % 17) / 17f * 0.002f;
                    float shade = 0.018f + edge * 0.060f + (1f - v) * 0.012f + scan + grain;

                    float dx = (u - 0.25f) / 0.56f;
                    float dy = (v - 0.96f) / 0.24f;
                    float glare = 0.018f + 0.060f * Mathf.Exp(-2f * (dx * dx + dy * dy));
                    float diagonal = (v - (1.12f - u * 0.36f)) / 0.11f;
                    glare += 0.025f * Mathf.Exp(-diagonal * diagonal);

                    // A very narrow red/cyan prism at opposite edges and split glare.
                    // The tint is in this transparent finish, not a displaced copy of UI pixels.
                    float redLip = 0.014f * Mathf.Exp(-u * u / 0.000045f);
                    float cyanLip = 0.014f * Mathf.Exp(-(1f - u) * (1f - u) / 0.000045f);
                    float alpha = shade + glare + redLip + cyanLip;
                    pixels[y * width + x] = (Color32)new Color(
                        (glare * 0.77f + redLip) / alpha,
                        (glare * 0.87f + cyanLip * 0.82f) / alpha,
                        (glare + cyanLip) / alpha,
                        alpha);
                }
            }

            texture.SetPixels32(pixels);
            return Bake(texture);
        }
    }
}
