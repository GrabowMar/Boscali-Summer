using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Core.Ui
{
    /// <summary>
    /// The HUD's own white alpha mask, tinted by <c>Image.color</c> so theme/tone changes are a
    /// colour write, never a re-bake: the native panel's chamfered slab, signed-distance rasterised with a one-pixel
    /// anti-aliased edge (the KaceyTronic-RWR technique), baked once as a small 9-sliced texture. Square or chamfered
    /// shapes only: the HUD has no rounded rectangles or capsules. Built lazily on the main thread.
    /// </summary>
    internal static class HudSprites
    {
        private static Sprite chamfer, cloud, bandPlain, bandFaded;

        /// <summary>The base game's HUD panel shape: a dark slab with its lower-left corner cut at 45°.
        /// 9-sliced so the cut stays 14 px at any size.</summary>
        public static Sprite Chamfer => chamfer != null ? chamfer : (chamfer = BakeChamfer("BoscaliHud_Chamfer", 40, 14f));

        /// <summary>Texels of a <see cref="ShadeCloud"/>'s fall-off border, and of the left (and right) ramp of a <see cref="SunBand"/>: the sprites carry no size of their own, the image's <c>pixelsPerUnitMultiplier</c> is texels / (design units x board unit).</summary>
        public const int CloudBorder = 28, BandRamp = 40;

        /// <summary>
        /// The TAC BOARD's shade cloud: a white alpha mask, 64 x 64, whose alpha is nothing at the outer edge and full <see cref="CloudBorder"/> texels in, a smoothstep per axis (a corner is the product),
        /// 9-sliced on that border so the fall-off keeps its size at any block size. Tint it with <c>Image.color</c>; its alpha is the cloud's core alpha.
        /// </summary>
        public static Sprite ShadeCloud => cloud != null ? cloud : (cloud = BakeCloud());

        /// <summary>
        /// The TAC BOARD's sun band: a white alpha mask whose alpha falls linearly from full at its top to nothing at its bottom, ramps from nothing to full over the <see cref="BandRamp"/> texel border at its left and
        /// back over the one at its right (3-sliced across, stretched down). With a <paramref name="fadeShare"/> above 0 the top share of it also fades in from nothing, so a band that starts below the top of the screen has
        /// no edge there (the share is baked the first time that variant is asked for).
        /// </summary>
        public static Sprite SunBand(float fadeShare) => fadeShare > 0f ? (bandFaded != null ? bandFaded : (bandFaded = BakeBand("BoscaliHud_SunBandFaded", fadeShare))) : (bandPlain != null ? bandPlain : (bandPlain = BakeBand("BoscaliHud_SunBand", 0f)));

        private static Sprite BakeCloud()
        {
            const int size = 64;
            Texture2D texture = NewMask("BoscaliHud_ShadeCloud", size, size);
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++) texture.SetPixel(x, y, new Color(1f, 1f, 1f, Falloff(x, size) * Falloff(y, size)));
            return Finish(texture, new Vector4(CloudBorder, CloudBorder, CloudBorder, CloudBorder));
        }

        /// <summary>0 at the outer edge of texel row/column <paramref name="i"/> of <paramref name="n"/>, 1 from <see cref="CloudBorder"/> texels in (smoothstep).</summary>
        private static float Falloff(int i, int n)
        {
            float d = Mathf.Clamp01(Mathf.Min(i + 0.5f, n - i - 0.5f) / CloudBorder);
            return d * d * (3f - 2f * d);
        }

        private static Sprite BakeBand(string name, float fadeShare)
        {
            const int core = 8, height = 48;
            int width = BandRamp * 2 + core;
            Texture2D texture = NewMask(name, width, height);
            for (int y = 0; y < height; y++)
            {
                float fromTop = 1f - (y + 0.5f) / height, v = 1f - fromTop;      // texel row 0 is the bottom
                if (fadeShare > 0f) v *= Mathf.Clamp01(fromTop / fadeShare);
                for (int x = 0; x < width; x++)
                    texture.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Min(1f, Mathf.Min(x + 0.5f, width - x - 0.5f) / BandRamp) * v));
            }
            return Finish(texture, new Vector4(BandRamp, 0f, BandRamp, 0f));
        }

        private static Texture2D NewMask(string name, int width, int height) =>
            new Texture2D(width, height, TextureFormat.RGBA32, false) { name = name, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };

        /// <summary>The mask as a 100 ppu sprite with its slicing border (the texture stays readable: a few KB, and the offline check samples it).</summary>
        private static Sprite Finish(Texture2D texture, Vector4 border)
        {
            texture.Apply(false, false);
            Sprite sprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f), 100f, 0u, SpriteMeshType.FullRect, border);
            sprite.name = texture.name;
            sprite.hideFlags = HideFlags.HideAndDontSave;
            return sprite;
        }

        private static Sprite BakeChamfer(string name, int size, float cut)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = name, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave,
            };
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    // Signed distance (px) inside the diagonal x + y = cut, 1 px anti-aliased edge.
                    float d = ((x + 0.5f) + (y + 0.5f) - cut) * 0.70710678f;
                    texture.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(d + 0.5f)));
                }
            texture.Apply(false, true);
            float border = cut + 2f;
            Sprite sprite = Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f, 0u,
                SpriteMeshType.FullRect, new Vector4(border, border, border, border));
            sprite.name = name;
            sprite.hideFlags = HideFlags.HideAndDontSave;
            return sprite;
        }

        /// <summary>Applies a sliced HUD mask to <paramref name="image"/> at 1:1 canvas pixels.</summary>
        public static void Slice(Image image, Sprite sprite)
        {
            image.sprite = sprite;
            image.type = Image.Type.Sliced;
            image.pixelsPerUnitMultiplier = 1f;
        }

        /// <summary>A full-stretch, input-transparent child image (frames, tracks, lit backgrounds).</summary>
        public static Image Child(RectTransform parent, string name, Sprite sprite, bool sliced)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            Image image = go.GetComponent<Image>();
            image.raycastTarget = false;
            if (sliced) Slice(image, sprite);
            else image.sprite = sprite;
            return image;
        }
    }
}
