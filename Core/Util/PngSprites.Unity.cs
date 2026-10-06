using UnityEngine;

namespace BoscaliSummer.Core.Util
{
    internal static partial class PngSprites
    {
        /// <summary>
        /// Header-checks then decodes a PNG into a bilinear, clamped RGBA32 texture and a centred 100 ppu
        /// sprite. The texture is named <paramref name="name"/>, the sprite <c>name + "Sprite"</c>. On failure
        /// nothing is left allocated.
        /// </summary>
        internal static bool TryLoad(byte[] data, int maxDimension, string name, out Texture2D texture, out Sprite sprite)
        {
            texture = null;
            sprite = null;
            if (!IsSupported(data, maxDimension, out int width, out int height)) return false;

            var decoded = new Texture2D(2, 2, TextureFormat.RGBA32, false, false)
            {
                name = name,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            if (!ImageConversion.LoadImage(decoded, data, true) || decoded.width != width || decoded.height != height)
            {
                Object.Destroy(decoded);
                return false;
            }

            Sprite created = Sprite.Create(decoded, new Rect(0f, 0f, decoded.width, decoded.height),
                new Vector2(0.5f, 0.5f), 100f);
            if (created == null)
            {
                Object.Destroy(decoded);
                return false;
            }
            created.name = name + "Sprite";
            texture = decoded;
            sprite = created;
            return true;
        }
    }
}
