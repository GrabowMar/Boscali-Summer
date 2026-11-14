using System.Collections.Generic;
using UnityEngine;

namespace BoscaliSummer.Core.Util
{
    /// <summary>
    /// The three doctrine emblems: embedded PNGs <c>BoscaliSummer.NationArt.&lt;key&gt;.png</c> (keys from <c>Doctrines.ArtKey</c>), decoded once
    /// and cached for the session. A missing key returns null and the caller keeps its vector glyph. Main thread only.
    /// </summary>
    internal static class NationArt
    {
        private const int MaximumBytes = 256 * 1024;
        private const int MaximumDimension = 256;
        private static readonly Dictionary<string, Sprite> Cache = new Dictionary<string, Sprite>(4);

        public static Sprite Get(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            if (Cache.TryGetValue(key, out Sprite known)) return known;
            Sprite sprite = null;
            byte[] data = EmbeddedResources.ReadAll(typeof(NationArt).Assembly, "BoscaliSummer.NationArt." + key + ".png", MaximumBytes);
            if (data != null && PngSprites.TryLoad(data, MaximumDimension, "BoscaliNation." + key, out Texture2D _, out Sprite made)) sprite = made;
            Cache[key] = sprite;
            return sprite;
        }
    }
}
