using System;
using System.Collections.Generic;
using System.IO;
using BoscaliSummer.Core.Util;
using UnityEngine;

namespace BoscaliSummer.Modules.Radio.Presentation
{
    internal static class RadioStationIconCache
    {
        internal const string EmbeddedPrefix = "embedded:";
        private const int MaximumFileBytes = 256 * 1024;
        private const int MaximumDimension = 256;

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
                return data != null && PngSprites.TryLoad(data, MaximumDimension, "BoscaliRadio.Icon",
                    out Texture2D texture, out Sprite sprite) ? new Entry { Texture = texture, Sprite = sprite } : null;
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
                return EmbeddedResources.ReadAll(typeof(RadioStationIconCache).Assembly,
                    source.Substring(EmbeddedPrefix.Length), MaximumFileBytes);
            }

            if (!File.Exists(source)) return null;
            var info = new FileInfo(source);
            if (info.Length <= 0 || info.Length > MaximumFileBytes) return null;
            return File.ReadAllBytes(source);
        }
    }
}
