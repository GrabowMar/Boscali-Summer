using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace BoscaliSummer.Features.Radio.Presentation
{
    internal static class RadioStationIconCache
    {
        internal const string EmbeddedPrefix = "embedded:";

        private sealed class Entry
        {
            public Texture2D Texture;
            public Sprite Sprite;
        }

        private static readonly Dictionary<string, Entry> Entries =
            new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);

        public static Sprite Get(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return null;
            bool embedded = path.StartsWith(EmbeddedPrefix, StringComparison.Ordinal);
            if (!embedded &&
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
                byte[] data = ReadData(path);
                if (data == null) return null;
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

        private static byte[] ReadData(string source)
        {
            if (source.StartsWith(EmbeddedPrefix, StringComparison.Ordinal))
            {
                string resourceName = source.Substring(EmbeddedPrefix.Length);
                Assembly assembly = typeof(RadioStationIconCache).Assembly;
                using Stream stream = assembly.GetManifestResourceStream(resourceName);
                if (stream == null || stream.Length <= 0 ||
                    stream.Length > PngIconHeader.MaximumFileBytes)
                    return null;
                var data = new byte[(int)stream.Length];
                int offset = 0;
                while (offset < data.Length)
                {
                    int read = stream.Read(data, offset, data.Length - offset);
                    if (read <= 0) return null;
                    offset += read;
                }
                return data;
            }

            if (!File.Exists(source)) return null;
            var info = new FileInfo(source);
            if (info.Length <= 0 || info.Length > PngIconHeader.MaximumFileBytes) return null;
            return File.ReadAllBytes(source);
        }
    }
}
