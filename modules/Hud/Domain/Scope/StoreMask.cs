using System;

namespace BoscaliSummer.Modules.Hud.Domain
{
    /// <summary>
    /// The game's weapon pictures as an alpha mask (LAYOUT.md 1 "line art"), pure. A picture is light lines on an OPAQUE black ground, made for the game's additive material; drawn on the ordinary UI material it would be a black
    /// plate. So the board bakes each once into a white texture whose alpha is the brightness of the source (<see cref="Alpha"/>): the ground goes clear, the lines stay, and a tint colour does the rest, like every other ink.
    /// <see cref="Convert"/> does that to a texture's pixels and measures the ink box the picture is cropped to on the way.
    /// </summary>
    internal static class StoreMask
    {
        /// <summary>A pixel is ink, for the crop, above this share of full alpha.</summary>
        public const float InkShare = 0.1f;

        /// <summary>The biggest picture the board bakes (a side, in pixels): the game's are 512 x 256; anything bigger is left to the additive material.</summary>
        public const int MaxSide = 1024;

        /// <summary>The mask's alpha for a source pixel: its brightest channel times its own alpha (light lines on black, or on nothing).</summary>
        public static byte Alpha(byte r, byte g, byte b, byte a) => (byte)((Math.Max(r, Math.Max(g, b)) * a + 127) / 255);

        /// <summary>
        /// Rewrite RGBA32 pixels (<paramref name="width"/> x <paramref name="height"/>, four bytes each, the bottom row first as Unity keeps them) into the mask in place: white with the alpha of <see cref="Alpha"/>.
        /// <paramref name="ink"/> is the box of the pixels above <see cref="InkShare"/> as fractions of the picture (left, top, right, bottom; y down, the way <see cref="ArtBounds"/> reads); false, and the whole picture,
        /// when there are none.
        /// </summary>
        public static bool Convert(Span<byte> rgba, int width, int height, out ArtBounds ink)
        {
            int limit = (int)(InkShare * 255f), x0 = width, y0 = height, x1 = -1, y1 = -1;
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    int i = (y * width + x) * 4;
                    byte a = Alpha(rgba[i], rgba[i + 1], rgba[i + 2], rgba[i + 3]);
                    rgba[i] = rgba[i + 1] = rgba[i + 2] = 255;
                    rgba[i + 3] = a;
                    if (a <= limit) continue;
                    if (x < x0) x0 = x;
                    if (x > x1) x1 = x;
                    if (y < y0) y0 = y;
                    if (y > y1) y1 = y;
                }
            if (x1 < 0) { ink = ArtBounds.Whole; return false; }
            ink = new ArtBounds(x0 / (float)width, 1f - (y1 + 1f) / height, (x1 + 1f) / width, 1f - y0 / (float)height);
            return true;
        }
    }

    /// <summary>
    /// A small cache of at most <see cref="Capacity"/> things by integer key (a sprite's instance id), pure: a lookup marks the entry as just used, and adding to a full cache pushes the one used longest ago out and hands it back
    /// so its owner can release it. Nothing allocates after construction.
    /// </summary>
    internal sealed class StoreMaskCache<T> where T : class
    {
        public const int Capacity = 32;

        private readonly int[] keys = new int[Capacity];
        private readonly T[] values = new T[Capacity];
        private readonly long[] used = new long[Capacity];
        private long clock;

        public int Count { get; private set; }

        public bool TryGet(int key, out T value)
        {
            for (int i = 0; i < Count; i++)
                if (keys[i] == key) { used[i] = ++clock; value = values[i]; return true; }
            value = null;
            return false;
        }

        /// <summary>Put <paramref name="value"/> in under <paramref name="key"/> (an entry that has the key is replaced); returns what was pushed out to make room, or what it replaced, else null.</summary>
        public T Add(int key, T value)
        {
            int at = -1;
            for (int i = 0; i < Count && at < 0; i++) if (keys[i] == key) at = i;
            if (at < 0 && Count < Capacity) at = Count++;
            else if (at < 0)
            {
                at = 0;
                for (int i = 1; i < Count; i++) if (used[i] < used[at]) at = i;
            }
            T pushed = values[at];      // null in a slot not used yet
            keys[at] = key;
            values[at] = value;
            used[at] = ++clock;
            return pushed;
        }

        /// <summary>Empty the cache, handing every entry to <paramref name="release"/>.</summary>
        public void Clear(Action<T> release)
        {
            for (int i = 0; i < Count; i++)
            {
                release?.Invoke(values[i]);
                values[i] = null;
            }
            Count = 0;
        }
    }
}
