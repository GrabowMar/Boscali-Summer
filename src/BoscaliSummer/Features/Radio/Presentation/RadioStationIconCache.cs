using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace BoscaliSummer.Features.Radio.Presentation
{
    internal static class RadioStationIconCache
    {
        private sealed class Entry
        {
            public Texture2D Texture;
            public Sprite Sprite;
        }

        private static readonly Dictionary<string, Entry> Entries =
            new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);

        public static Sprite Get(string path)
        {
            if (string.IsNullOrWhiteSpace(path) ||
                !string.Equals(Path.GetExtension(path), ".png", StringComparison.OrdinalIgnoreCase))
                return null;

            if (Entries.TryGetValue(path, out Entry cached)) return cached?.Sprite;

            Entry entry = Load(path);
            Entries[path] = entry;
            return entry?.Sprite;
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

        private static Entry Load(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                var info = new FileInfo(path);
                if (info.Length <= 0 || info.Length > PngIconHeader.MaximumFileBytes) return null;

                byte[] data = File.ReadAllBytes(path);
                if (!PngIconHeader.IsSupported(data, out int width, out int height)) return null;

                var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false, false)
                {
                    name = "BoscaliRadio.Icon",
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
                sprite.name = "BoscaliRadio.IconSprite";
                return new Entry { Texture = texture, Sprite = sprite };
            }
            catch
            {
                return null;
            }
        }
    }
}
