using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using BoscaliSummer.Core.Util;
using UnityEngine;

namespace BoscaliSummer.Modules.Events.Presentation
{
    /// <summary>
    /// Event art, loaded first from loose PNGs a player drops into
    /// <c>BepInEx/plugins/BoscaliSummer/Events/</c>, then from a distinct bundled image for each event.
    /// Everything still renders without art
    /// (the vector glyph is the fallback), so this is decoration, never a dependency: a
    /// missing file, an oversized poster or a bad header just means the glyph shows.
    ///
    /// <para>Bounded and cached like the radio icon cache: PNG signature plus header
    /// dimensions checked before decode, one texture and sprite per key, hard byte and
    /// pixel ceilings, cleared on scene reset. Bundled images are loaded on demand.</para>
    /// </summary>
    internal static class EventArtCache
    {
        internal const int MaximumFileBytes = 2 * 1024 * 1024;
        internal const int MaximumDimension = 1024;

        private const int MaximumEntries = 64;

        private sealed class Entry
        {
            public Texture2D Texture;
            public Sprite Sprite;
        }

        private static readonly Dictionary<string, Entry> Entries =
            new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, Texture2D> Thumbs =
            new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);

        private static string root;
        private static bool prepared;

        /// <summary>
        /// The poster for the first key that has one, falling back to <c>default.png</c>.
        /// Returns null when neither an override nor bundled art loads, and then the caller draws the
        /// vector mark instead.
        /// </summary>
        public static Sprite Get(string artKey, string fallbackKey)
        {
            Sprite sprite = Load(artKey);
            if (sprite != null) return sprite;
            sprite = Load(fallbackKey);
            return sprite != null ? sprite : Load("default");
        }

        /// <summary>
        /// A small square, centre-cropped copy of the poster for <see cref="Get"/>, for row badges
        /// Null when there is no art
        /// or the GPU copy is unavailable; bounded and cleared with the rest of the cache.
        /// </summary>
        public static Texture Thumb(string artKey, string fallbackKey)
        {
            string id = artKey + "|" + fallbackKey;
            if (Thumbs.TryGetValue(id, out Texture2D cached)) return cached;
            if (Thumbs.Count >= MaximumEntries) return null;

            Texture2D thumb = null;
            try
            {
                Sprite sprite = Get(artKey, fallbackKey);
                Texture2D source = sprite != null ? sprite.texture : null;
                if (source != null && source.format == TextureFormat.RGBA32 &&
                    (SystemInfo.copyTextureSupport & UnityEngine.Rendering.CopyTextureSupport.Basic) != 0)
                {
                    Rect area = sprite.textureRect;
                    int side = Mathf.FloorToInt(Mathf.Min(area.width, area.height));
                    if (side > 0)
                    {
                        int x = Mathf.RoundToInt(area.x + (area.width - side) * 0.5f);
                        int y = Mathf.RoundToInt(area.y + (area.height - side) * 0.5f);
                        thumb = new Texture2D(side, side, TextureFormat.RGBA32, false, false)
                        {
                            name = "BoscaliEvents.Thumb",
                            filterMode = FilterMode.Bilinear,
                            wrapMode = TextureWrapMode.Clamp
                        };
                        Graphics.CopyTexture(source, 0, 0, x, y, side, side, thumb, 0, 0, 0, 0);
                    }
                }
            }
            catch
            {
                if (thumb != null) UnityEngine.Object.Destroy(thumb);
                thumb = null;
            }
            Thumbs[id] = thumb;
            return thumb;
        }

        public static void Clear()
        {
            foreach (Texture2D thumb in Thumbs.Values)
                if (thumb != null) UnityEngine.Object.Destroy(thumb);
            Thumbs.Clear();
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
                try
                {
                    PrepareFolder();
                    if (!string.IsNullOrEmpty(root))
                    {
                        string path = Path.Combine(root, key + ".png");
                        if (File.Exists(path))
                        {
                            var info = new FileInfo(path);
                            if (info.Length > 0 && info.Length <= MaximumFileBytes)
                            {
                                Entry loose = Decode(File.ReadAllBytes(path));
                                if (loose != null) return loose;
                            }
                        }
                    }
                }
                catch { /* An unreadable override must not hide bundled art. */ }

                byte[] data = EmbeddedResources.ReadAll(
                    typeof(EventArtCache).Assembly, "BoscaliSummer.EventsArt." + key + ".png", MaximumFileBytes);
                return data == null ? null : Decode(data);
            }
            catch
            {
                return null;
            }
        }

        private static Entry Decode(byte[] data)
        {
            try
            {
                return PngSprites.TryLoad(data, MaximumDimension, "BoscaliEvents.Art",
                    out Texture2D texture, out Sprite sprite) ? new Entry { Texture = texture, Sprite = sprite } : null;
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
    }
}
