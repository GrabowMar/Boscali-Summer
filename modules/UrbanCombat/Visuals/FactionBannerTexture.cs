using System;
using System.Collections.Generic;
using UnityEngine;

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
            if (cache.Count < MaximumCached) cache[identity] = texture;
            return texture;
        }

        private static Texture2D Build(string identity, Color field)
        {
            field.a = 1f;
            float luminance = field.r * 0.299f + field.g * 0.587f + field.b * 0.114f;
            Color device = luminance > 0.52f ? new Color(0.08f, 0.08f, 0.09f) : new Color(0.93f, 0.91f, 0.82f);
            Color border = Color.Lerp(Color.black, field, 0.3f);
            int seed = Seed(identity);
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

        /// <summary>FNV-1a over ordinal characters: stable across sessions and clients.</summary>
        private static int Seed(string identity)
        {
            uint hash = 2166136261u;
            for (int i = 0; i < identity.Length; i++) hash = (hash ^ identity[i]) * 16777619u;
            return (int)(hash & 0x7FFFFFFF);
        }
    }
}
