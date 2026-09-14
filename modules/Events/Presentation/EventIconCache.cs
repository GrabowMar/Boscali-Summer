using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace BoscaliSummer.Features.Events.Presentation
{
    /// <summary>
    /// Catalog icons from embedded PNGs, keyed by <c>IconKey</c>. No asset is shipped
    /// yet, so a miss returns null and the card draws its vector category glyph instead;
    /// dropping <c>modules/Events/Assets/&lt;icon_key&gt;.png</c> in is the whole upgrade.
    /// Bounds mirror the radio icon cache: data and dimensions are capped before decode.
    /// </summary>
    internal static class EventIconCache
    {
        private const string EmbeddedPrefix = "BoscaliSummer.EventsAssets.";
        private const int MaximumFileBytes = 512 * 1024;
        private const int MaximumDimension = 512;

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

        public static Sprite Get(string iconKey)
        {
            if (string.IsNullOrWhiteSpace(iconKey)) return null;
            if (Entries.TryGetValue(iconKey, out Entry cached)) return cached?.Sprite;

            Entry entry = Load(iconKey);
            Entries[iconKey] = entry;
            return entry?.Sprite;
        }

        private static Entry Load(string iconKey)
        {
            try
            {
                byte[] data = ReadData(EmbeddedPrefix + iconKey + ".png");
                if (data == null) return null;
                if (!IsSupported(data, out int width, out int height)) return null;

                var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false, false)
                {
                    name = "BoscaliEvents.Icon",
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
                sprite.name = "BoscaliEvents.IconSprite";
                return new Entry { Texture = texture, Sprite = sprite };
            }
            catch
            {
                return null;
            }
        }

        private static byte[] ReadData(string resourceName)
        {
            Assembly assembly = typeof(EventIconCache).Assembly;
            using Stream stream = assembly.GetManifestResourceStream(resourceName);
            if (stream == null || stream.Length <= 0 || stream.Length > MaximumFileBytes) return null;

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
