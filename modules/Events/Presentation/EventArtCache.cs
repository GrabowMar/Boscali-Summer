using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using UnityEngine;

namespace BoscaliSummer.Features.Events.Presentation
{
    /// <summary>
    /// Optional per-event poster art, loaded from loose PNGs a player drops into
    /// <c>BepInEx/plugins/BoscaliSummer/Events/</c>. Everything still renders without art
    /// (the vector glyph is the fallback), so this is decoration, never a dependency: a
    /// missing file, an oversized poster or a bad header just means the glyph shows.
    ///
    /// <para>Bounded and cached like the radio icon cache: PNG signature plus header
    /// dimensions checked before decode, one texture and sprite per key, hard byte and
    /// pixel ceilings, cleared on scene reset. Nothing is downloaded or bundled.</para>
    /// </summary>
    internal static class EventArtCache
    {
        internal const int MaximumFileBytes = 2 * 1024 * 1024;
        internal const int MaximumDimension = 1024;

        private const int MaximumEntries = 64;

        private static readonly byte[] Signature =
        {
            0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a
        };

        private sealed class Entry
        {
            public Texture2D Texture;
            public Sprite Sprite;
        }

        private static readonly Dictionary<string, Entry> Entries =
            new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);

        private static string root;
        private static bool prepared;

        /// <summary>
        /// The poster for the first key that has one, falling back to <c>default.png</c>.
        /// Returns null when the player has drawn nothing, and then the caller draws the
        /// vector mark instead.
        /// </summary>
        public static Sprite Get(string artKey, string fallbackKey)
        {
            Sprite sprite = Load(artKey);
            if (sprite != null) return sprite;
            sprite = Load(fallbackKey);
            return sprite != null ? sprite : Load("default");
        }

        public static void Clear()
        {
            foreach (Entry entry in Entries.Values)
            {
                if (entry == null) continue;
                if (entry.Sprite != null) UnityEngine.Object.Destroy(entry.Sprite);
                if (entry.Texture != null) UnityEngine.Object.Destroy(entry.Texture);
            }
            Entries.Clear();
        }

        private static Sprite Load(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            if (Entries.TryGetValue(key, out Entry cached)) return cached?.Sprite;
            if (Entries.Count >= MaximumEntries) return null;

            Entry entry = Read(key);
            Entries[key] = entry;
            return entry?.Sprite;
        }

        private static Entry Read(string key)
        {
            try
            {
                PrepareFolder();
                string path = Path.Combine(root, key + ".png");
                if (!File.Exists(path)) return null;

                var info = new FileInfo(path);
                if (info.Length <= 0 || info.Length > MaximumFileBytes) return null;
                byte[] data = File.ReadAllBytes(path);
                if (!IsSupported(data, out int width, out int height)) return null;

                var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false, false)
                {
                    name = "BoscaliEvents.Art",
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp
                };
                if (!ImageConversion.LoadImage(texture, data, true) ||
                    texture.width != width || texture.height != height)
                {
                    UnityEngine.Object.Destroy(texture);
                    return null;
                }

                Sprite sprite = Sprite.Create(
                    texture, new Rect(0f, 0f, texture.width, texture.height),
                    new Vector2(0.5f, 0.5f), 100f);
                if (sprite == null)
                {
                    UnityEngine.Object.Destroy(texture);
                    return null;
                }
                sprite.name = "BoscaliEvents.ArtSprite";
                return new Entry { Texture = texture, Sprite = sprite };
            }
            catch
            {
                return null;
            }
        }

        private static void PrepareFolder()
        {
            if (prepared) return;
            prepared = true;
            root = Path.Combine(Paths.PluginPath, "BoscaliSummer", "Events");
            try
            {
                Directory.CreateDirectory(root);
            }
            catch
            {
                // A read-only plugins folder still renders glyphs; nothing to report.
            }
        }

        private static bool IsSupported(byte[] data, out int width, out int height)
        {
            width = 0;
            height = 0;
            if (data == null || data.Length < 24 || data.Length > MaximumFileBytes) return false;

            for (int i = 0; i < Signature.Length; i++)
                if (data[i] != Signature[i]) return false;

            if (data[12] != (byte)'I' || data[13] != (byte)'H' ||
                data[14] != (byte)'D' || data[15] != (byte)'R')
                return false;

            uint rawWidth = ReadBigEndianUInt32(data, 16);
            uint rawHeight = ReadBigEndianUInt32(data, 20);
            if (rawWidth == 0 || rawHeight == 0 ||
                rawWidth > MaximumDimension || rawHeight > MaximumDimension)
                return false;

            width = (int)rawWidth;
            height = (int)rawHeight;
            return true;
        }

        private static uint ReadBigEndianUInt32(byte[] data, int offset) =>
            ((uint)data[offset] << 24) |
            ((uint)data[offset + 1] << 16) |
            ((uint)data[offset + 2] << 8) |
            data[offset + 3];
    }
}
