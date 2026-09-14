using System.Collections.Generic;
using BoscaliSummer.Features.HighCommand.Domain;
using UnityEngine;

namespace BoscaliSummer.Features.HighCommand.Presentation
{
    /// <summary>
    /// Draws the generated staff portrait as a small dithered bust in the avionics palette.
    /// The face is a pure function of the seed; the cache is bounded and explicitly cleared
    /// on scene reset, so repeated openings never grow texture memory.
    /// </summary>
    internal static class CommanderPortraitRenderer
    {
        internal enum Tone : byte
        {
            Friendly = 0,
            Hostile = 1,
            Kia = 2,
        }

        private const int Width = 64;
        private const int Height = 80;
        private const int MaximumCache = 64;

        private static readonly Dictionary<int, Sprite> cache = new Dictionary<int, Sprite>(MaximumCache);
        private static readonly List<int> order = new List<int>(MaximumCache);

        public static Sprite Get(int seed, Tone tone)
        {
            int key = unchecked(seed * 4 + (int)tone);
            if (cache.TryGetValue(key, out Sprite existing) && existing != null) return existing;
            Sprite sprite = Build(PortraitDesign.FromSeed(seed), tone);
            if (sprite == null) return null;
            if (cache.Count >= MaximumCache)
            {
                int oldest = order[0];
                order.RemoveAt(0);
                Destroy(cache[oldest]);
                cache.Remove(oldest);
            }
            cache[key] = sprite;
            order.Add(key);
            return sprite;
        }

        public static void Clear()
        {
            foreach (var pair in cache) Destroy(pair.Value);
            cache.Clear();
            order.Clear();
        }

        private static void Destroy(Sprite sprite)
        {
            if (sprite == null) return;
            if (sprite.texture != null) UnityEngine.Object.Destroy(sprite.texture);
            UnityEngine.Object.Destroy(sprite);
        }

        private static Sprite Build(PortraitDesign design, Tone tone)
        {
            Color32 dark, mid, light, ink;
            switch (tone)
            {
                case Tone.Hostile:
                    dark = new Color32(20, 15, 6, 255);
                    mid = new Color32(126, 88, 26, 255);
                    light = new Color32(214, 164, 74, 255);
                    break;
                case Tone.Kia:
                    dark = new Color32(12, 14, 14, 255);
                    mid = new Color32(74, 82, 80, 255);
                    light = new Color32(138, 148, 144, 255);
                    break;
                default:
                    dark = new Color32(8, 18, 13, 255);
                    mid = new Color32(42, 112, 68, 255);
                    light = new Color32(122, 198, 140, 255);
                    break;
            }
            ink = new Color32(3, 6, 4, 255);

            var pixels = new Color32[Width * Height];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = dark;
            if (design.Background == 1) Rect(pixels, 6, 6, 58, 66, Blender(dark, mid, 0.25f));

            // Shoulders and collar.
            Rect(pixels, 5, 2, 59, 20, mid);
            Rect(pixels, 13, 20, 51, 25, Blender(mid, light, 0.35f));
            Rect(pixels, 29, 18, 35, 26, ink);
            if (design.Collar == 1)
            {
                Rect(pixels, 13, 24, 51, 26, light);
                Rect(pixels, 29, 18, 35, 24, mid);
            }

            int cx = 32;
            int cy = 47;
            int rx = 12 + design.Face;
            int ry = 15;

            // Neck and head, drawn rim-first so the face reads at 10px.
            Rect(pixels, cx - 5, 22, cx + 5, 32, mid);
            Ellipse(pixels, cx, cy, rx, ry, light, fill: true);
            Ellipse(pixels, cx, cy, rx - 1, ry - 1, mid, fill: true);
            Ellipse(pixels, cx - rx, cy - 1, 2, 3, mid, fill: true);
            Ellipse(pixels, cx + rx, cy - 1, 2, 3, mid, fill: true);

            // Hair and headgear.
            if (design.Cap >= 1)
            {
                Rect(pixels, cx - rx - 1, cy + ry - 6, cx + rx + 1, cy + ry - 3, light);
                Rect(pixels, cx - rx, cy + ry - 3, cx + rx, cy + ry, mid);
                if (design.Cap == 2)
                {
                    Ellipse(pixels, cx, cy + ry, rx - 1, 7, mid, fill: true);
                    Rect(pixels, cx - rx - 3, cy + ry + 3, cx + rx + 3, cy + ry + 5, light);
                }
            }
            else
            {
                switch (design.Hair)
                {
                    case 1:
                        Rect(pixels, cx - rx + 1, cy + ry - 6, cx + rx - 1, cy + ry - 1, ink);
                        break;
                    case 2:
                        Rect(pixels, cx - rx + 1, cy + ry - 5, cx + 2, cy + ry - 1, ink);
                        break;
                    case 3:
                        Rect(pixels, cx - rx - 1, cy - 4, cx - rx + 2, cy + ry - 1, ink);
                        Rect(pixels, cx + rx - 2, cy - 4, cx + rx + 1, cy + ry - 1, ink);
                        Rect(pixels, cx - rx + 1, cy + ry - 5, cx + rx - 1, cy + ry - 1, ink);
                        break;
                }
            }

            int eyeY = cy + 1;
            int browY = cy + 5 + design.Brow;
            Rect(pixels, cx - 7, browY, cx - 3, browY + 1, ink);
            Rect(pixels, cx + 3, browY, cx + 7, browY + 1, ink);
            Rect(pixels, cx - 7, eyeY, cx - 4, eyeY + 2, ink);
            Rect(pixels, cx + 4, eyeY, cx + 7, eyeY + 2, ink);
            if (design.Eyes == 2)
            {
                Rect(pixels, cx - 7, eyeY + 1, cx - 4, eyeY + 2, mid);
                Rect(pixels, cx + 4, eyeY + 1, cx + 7, eyeY + 2, mid);
            }
            if (design.Glasses == 1)
            {
                Rect(pixels, cx - 9, eyeY - 1, cx - 2, eyeY - 1, ink);
                Rect(pixels, cx + 2, eyeY - 1, cx + 9, eyeY - 1, ink);
                Rect(pixels, cx - 9, eyeY + 3, cx - 2, eyeY + 3, ink);
                Rect(pixels, cx + 2, eyeY + 3, cx + 9, eyeY + 3, ink);
                Rect(pixels, cx - 2, eyeY + 1, cx + 2, eyeY + 1, ink);
            }

            Rect(pixels, cx, cy - 4, cx + 1, cy, ink);
            Rect(pixels, cx - 3 - design.Mouth, cy - 7, cx + 3 + design.Mouth, cy - 7, ink);

            switch (design.FacialHair)
            {
                case 1:
                    Rect(pixels, cx - 5, cy - 6, cx + 5, cy - 5, ink);
                    break;
                case 2:
                    Ellipse(pixels, cx, cy - 5, rx - 2, 5, ink, fill: false);
                    break;
                case 3:
                    Rect(pixels, cx - 5, cy - 6, cx + 5, cy - 5, ink);
                    Ellipse(pixels, cx, cy - 5, rx - 2, 5, ink, fill: false);
                    break;
            }

            if (design.Scar == 1)
            {
                Rect(pixels, cx - 8, cy + 4, cx - 7, cy + 7, ink);
                Rect(pixels, cx - 7, cy + 2, cx - 6, cy + 4, ink);
            }

            var texture = new Texture2D(Width, Height, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return Sprite.Create(texture, new Rect(0f, 0f, Width, Height), new Vector2(0.5f, 0.5f), 100f);
        }

        private static void Rect(Color32[] pixels, int x0, int y0, int x1, int y1, Color32 color)
        {
            for (int y = y0; y <= y1; y++)
            {
                if (y < 0 || y >= Height) continue;
                for (int x = x0; x <= x1; x++)
                {
                    if (x < 0 || x >= Width) continue;
                    pixels[y * Width + x] = color;
                }
            }
        }

        private static void Ellipse(Color32[] pixels, int cx, int cy, int rx, int ry, Color32 color, bool fill)
        {
            if (rx <= 0 || ry <= 0) return;
            for (int y = -ry; y <= ry; y++)
            {
                int py = cy + y;
                if (py < 0 || py >= Height) continue;
                int half = (int)(rx * Mathf.Sqrt(Mathf.Max(0f, 1f - (y * y) / (float)(ry * ry))));
                if (fill)
                {
                    for (int x = cx - half; x <= cx + half; x++)
                    {
                        if (x < 0 || x >= Width) continue;
                        pixels[py * Width + x] = color;
                    }
                }
                else
                {
                    int x0 = cx - half, x1 = cx + half;
                    if (x0 >= 0 && x0 < Width) pixels[py * Width + x0] = color;
                    if (x1 >= 0 && x1 < Width) pixels[py * Width + x1] = color;
                }
            }
        }

        private static Color32 Blender(Color32 a, Color32 b, float t) => new Color32(
            (byte)(a.r + (b.r - a.r) * t),
            (byte)(a.g + (b.g - a.g) * t),
            (byte)(a.b + (b.b - a.b) * t),
            255);
    }
}
