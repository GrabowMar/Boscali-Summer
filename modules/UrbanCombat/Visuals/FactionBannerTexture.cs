using System;
using System.Collections.Generic;
using UnityEngine;
using BoscaliSummer.Core.Math;

namespace BoscaliSummer.Garrisons
{
    /// <summary>
    /// Local, procedural two-colour banner for occupied-building flags: a solid field in the
    /// faction colour plus one deterministic geometric device and a dark hoist/edge border, so
    /// a hostile flag reads by shape as well as colour at distance (URBAN_COMBAT.md, legibility
    /// model: "color never carries meaning alone"; B4 faction identity). The device and its
    /// contrast are derived from the faction's own identity/colour, never bundled, downloaded
    /// or persisted, and cached per faction for the session (mirrors
    /// Progression/Presentation/EmblemRenderer's cache shape, kept local since UrbanCombat may
    /// not import a sibling feature).
    /// </summary>
    internal static class FactionBannerTexture
    {
        private const int Width = 64;
        private const int Height = 36;
        private const int DeviceCount = 5;
        private const int MaximumCached = 12;

        private static readonly Dictionary<string, Texture2D> cache =
            new Dictionary<string, Texture2D>(StringComparer.Ordinal);
        private static readonly Queue<string> insertionOrder = new Queue<string>(MaximumCached);

        /// <summary>Stable key for a banner: the faction's own name when the game provides
        /// one, otherwise its colour, so unlisted/modded factions still get a consistent
        /// design instead of falling back to a blank flag.</summary>
        public static string Identity(FactionHQ owner)
        {
            string name = owner != null && owner.faction != null ? owner.faction.factionName : null;
            if (!string.IsNullOrEmpty(name)) return name;
            Color color = owner != null && owner.faction != null ? owner.faction.color : Color.gray;
            return "#" + ColorUtility.ToHtmlStringRGB(color);
        }

        public static Texture2D Get(string identity, Color field)
        {
            if (string.IsNullOrEmpty(identity)) identity = "#" + ColorUtility.ToHtmlStringRGB(field);
            if (cache.TryGetValue(identity, out Texture2D cached) && cached != null) return cached;
            Texture2D texture = Build(identity, field);
            if (cache.Count >= MaximumCached && insertionOrder.Count > 0)
            {
                string oldest = insertionOrder.Dequeue();
                if (cache.TryGetValue(oldest, out Texture2D evicted))
                {
                    if (evicted != null) UnityEngine.Object.Destroy(evicted);
                    cache.Remove(oldest);
                }
            }
            cache[identity] = texture;
            insertionOrder.Enqueue(identity);
            return texture;
        }

        private static Texture2D Build(string identity, Color field)
        {
            if (Contains(identity, "Boscali") || Contains(identity, "BDF")) return Painted(identity, Bdf);
            if (Contains(identity, "Primeva") || Contains(identity, "PALA")) return Painted(identity, Pala);
            field.a = 1f;
            float luminance = field.r * 0.299f + field.g * 0.587f + field.b * 0.114f;
            Color device = luminance > 0.52f ? new Color(0.08f, 0.08f, 0.09f) : new Color(0.93f, 0.91f, 0.82f);
            Color border = Color.Lerp(Color.black, field, 0.3f);
            int seed = (int)(Deterministic.HashString(identity) & 0x7FFFFFFF);
            int pattern = ((seed >> 3) & 0x7FFFFFFF) % DeviceCount;

            var pixels = new Color[Width * Height];
            for (int y = 0; y < Height; y++)
            {
                float v = (y + 0.5f) / Height;
                for (int x = 0; x < Width; x++)
                {
                    float u = (x + 0.5f) / Width;
                    bool onBorder = u < 0.03f || u > 0.97f || v < 0.045f || v > 0.955f;
                    Color pixel = onBorder ? border : InDevice(pattern, u, v) ? device : field;
                    pixels[y * Width + x] = pixel;
                }
            }

            var texture = new Texture2D(Width, Height, TextureFormat.RGBA32, mipChain: false)
            {
                name = "BoscaliSummer.FactionBanner_" + identity,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave,
            };
            texture.SetPixels(pixels);
            texture.Apply(updateMipmaps: false, makeNoLongerReadable: true);
            return texture;
        }

        private static bool Contains(string text, string fragment) =>
            text.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0;

        // Painted banners for the two stock factions, laid out for the hanging facade banner:
        // texture x runs down the cloth (0 = top), texture y across it. Paint functions take
        // banner space in banner widths: across -0.5..0.5, down 0..BannerAspect.
        private const int PaintedLength = 192;
        private const int PaintedWidth = 72;
        private const float BannerAspect = 2.8f;
        private static readonly Color Ink = new Color(0.04f, 0.04f, 0.05f);
        private static readonly Color Paper = new Color(0.90f, 0.90f, 0.88f);

        private static Texture2D Painted(string identity, Func<float, float, Color> paint)
        {
            var pixels = new Color[PaintedLength * PaintedWidth];
            for (int y = 0; y < PaintedWidth; y++)
                for (int x = 0; x < PaintedLength; x++)
                {
                    // 2x2 supersampling keeps the emblem edges clean at banner scale.
                    Color sum = Color.clear;
                    for (int s = 0; s < 4; s++)
                    {
                        float across = (y + 0.25f + 0.5f * (s & 1)) / PaintedWidth - 0.5f;
                        float down = (x + 0.25f + 0.5f * (s >> 1)) / PaintedLength * BannerAspect;
                        sum += paint(across, down);
                    }
                    Color pixel = sum * 0.25f;
                    pixel.a = 1f;
                    pixels[y * PaintedLength + x] = pixel;
                }
            var texture = new Texture2D(PaintedLength, PaintedWidth, TextureFormat.RGBA32, mipChain: true)
            {
                name = "BoscaliSummer.FactionBanner_" + identity,
                filterMode = FilterMode.Trilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave,
            };
            texture.SetPixels(pixels);
            texture.Apply(updateMipmaps: true, makeNoLongerReadable: true);
            return texture;
        }

        private static bool OnBorder(float across, float down) =>
            Mathf.Abs(across) > 0.46f || down < 0.04f || down > BannerAspect - 0.04f;

        // BDF: white / green / blue bands (the badge's wings) behind its black-edged notched shield.
        private static readonly Color BdfBlue = new Color(0.04f, 0.22f, 0.52f);
        private static readonly Color BdfGreen = new Color(0.22f, 0.40f, 0.04f);

        private static Color Bdf(float across, float down)
        {
            if (OnBorder(across, down)) return Ink;
            const float top = 0.72f, height = 0.78f, halfWidth = 0.31f;
            float x = across / halfWidth, y = (down - top) / height;
            if (Shield(x, y, 1f))
            {
                if (!Shield(x, y, 0.84f)) return Ink;
                if (x > 0.28f && x < 0.72f && y > 0.17f && y < 0.17f + (x - 0.28f) * 0.32f) return Paper;
                if (y < 0.40f - 0.32f * x) return BdfBlue;
                if (y > 0.80f + 0.18f * x && y < 0.93f + 0.18f * x) return BdfBlue;
                return BdfGreen;
            }
            // Thin black seams between the bands, like the badge's outlined wings.
            float band = down / BannerAspect * 3f;
            if (band > 0.5f && band < 2.5f && Mathf.Abs(band - Mathf.Round(band)) < 0.025f) return Ink;
            return band < 1f ? Paper : band < 2f ? BdfGreen : BdfBlue;
        }

        // Shield in its own box (x -1..1, y 0..1 down) with a V notch on top and a point below;
        // scale shrinks it about its centre for the outline.
        private static bool Shield(float x, float y, float scale)
        {
            x /= scale;
            y = (y - 0.5f) / scale + 0.5f;
            if (y < 0.16f * (1f - Mathf.Abs(x)) || y > 1f) return false;
            float half = y < 0.6f ? 1f : (1f - y) / 0.4f;
            return Mathf.Abs(x) <= half;
        }

        // PALA: crimson field; black-edged ring with three white points; inside it an inverted
        // triangle whose six apex-up cells are white; two black bars low on the cloth.
        private static readonly Color PalaRed = new Color(0.52f, 0.02f, 0.03f);

        private static Color Pala(float across, float down)
        {
            if (OnBorder(across, down)) return Ink;
            float bar = down / BannerAspect;
            if ((bar > 0.80f && bar < 0.83f) || (bar > 0.86f && bar < 0.89f)) return Ink;
            const float radius = 0.34f, centre = 1.0f;
            float x = across / radius, y = -(down - centre) / radius;
            for (int k = 0; k < 3; k++)
            {
                // Points at 90, 210 and 330 degrees, between the triangle's corners; their black
                // bases sit over the ring.
                float a = (90f + 120f * k) * Mathf.Deg2Rad;
                float along = x * Mathf.Cos(a) + y * Mathf.Sin(a);
                float side = Mathf.Abs(-x * Mathf.Sin(a) + y * Mathf.Cos(a));
                if (along > 0.9f && along < 1.4f)
                {
                    float halfWidth = Mathf.Min(0.2f, (1.4f - along) * 0.62f);
                    if (side < halfWidth)
                        return along > 1.06f && along < 1.33f && side < halfWidth - 0.06f ? Paper : Ink;
                }
            }
            float r = Mathf.Sqrt(x * x + y * y);
            if (r > 1.04f) return PalaRed;
            if (r > 0.98f || (r > 0.74f && r < 0.80f)) return Ink;
            if (r >= 0.80f) return PalaRed;
            // Inverted triangle inscribed in the ring's inner edge: corners at 30, 150, 270 degrees.
            const float inner = 0.74f;
            float t = (inner * 0.5f - y) / (inner * 1.5f);
            float s = 1f - t;
            float half = s * inner * 0.866f;
            if (t < 0f || t > 1f || Mathf.Abs(x) > half) return Paper;
            if (t < 0.04f || Mathf.Abs(x) > half - 0.07f) return Ink;
            // Triangular lattice from the bottom corner: cells with frac(a) + frac(b) >= 1 point
            // the other way from the parent, i.e. apex up (6 of 16 for a four-way split).
            const int n = 4;
            float p = s * n, q = x / (inner * 0.866f) * n;
            float la = (p + q) * 0.5f, lb = (p - q) * 0.5f;
            float fa = la - Mathf.Floor(la), fb = lb - Mathf.Floor(lb);
            return fa + fb >= 1f ? Paper : PalaRed;
        }

        /// <summary>Five simple heraldic devices (bend, chevron, canton cross, fess, saltire),
        /// picked deterministically from the faction identity so allies and late joiners all
        /// see the same flag and two factions almost never collide by shape.</summary>
        private static bool InDevice(int pattern, float u, float v)
        {
            switch (pattern)
            {
                case 0: // Bend: diagonal stripe from the hoist foot to the fly head.
                    return Mathf.Abs(u - v) < 0.16f;
                case 1: // Chevron: solid wedge pointing out from the hoist.
                    return u < (v >= 0.5f ? (v - 0.5f) * 2f : (0.5f - v) * 2f);
                case 2: // Canton with a cross cut out of it.
                    if (u > 0.42f || v < 0.52f) return false;
                    bool crossCut = Mathf.Abs(u - 0.21f) < 0.045f || Mathf.Abs(v - 0.76f) < 0.06f;
                    return !crossCut;
                case 3: // Fess: a broad horizontal band across the centre.
                    return v > 0.36f && v < 0.64f;
                default: // Saltire: a diagonal cross corner to corner.
                    return Mathf.Abs(u - v) < 0.13f || Mathf.Abs(u + v - 1f) < 0.13f;
            }
        }
    }
}
