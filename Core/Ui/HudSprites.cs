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
        private static Sprite chamfer, bandPlain, bandFaded, feather;

        /// <summary>The visor look's shade colour, rgb(2, 12, 8): what a soft patch behind symbology is tinted (the TAC BOARD's sun band and clouds are the same colour); its alpha is the caller's.</summary>
        public static readonly Color ShadeTint = new Color(2f / 255f, 12f / 255f, 8f / 255f, 1f);

        /// <summary>Texels of the soft edge of a <see cref="Feather"/> (and so canvas units, drawn 9-sliced at multiplier 1).</summary>
        public const int FeatherEdge = 40;

        /// <summary>
        /// A soft patch of shade for the visor look: a white alpha mask, full in the middle and falling smoothly to nothing over its last <see cref="FeatherEdge"/> texels all round (the product of two edge profiles, so the corners are
        /// round), 9-sliced so the fall-off keeps its size on any rectangle. Tint it with <see cref="ShadeTint"/> at the alpha the symbology needs; place the image that much larger than what it backs so the full part lies under the ink.
        /// There is no hard edge anywhere: ink is read through it, not on a plate.
        /// </summary>
        public static Sprite Feather => feather != null ? feather : (feather = BakeFeather("BoscaliHud_Feather"));

        private static Sprite BakeFeather(string name)
        {
            const int edge = FeatherEdge, size = edge * 2 + 4;
            Texture2D texture = NewMask(name, size, size);
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float ax = Profile(Mathf.Min(x + 0.5f, size - x - 0.5f) / edge), ay = Profile(Mathf.Min(y + 0.5f, size - y - 0.5f) / edge);
                    texture.SetPixel(x, y, new Color(1f, 1f, 1f, ax * ay));
                }
            return Finish(texture, new Vector4(edge, edge, edge, edge));
        }

        /// <summary>The mean of u and its smoothstep (the board's cloud field's profile: its steepest slope is 1.25), clamped to 0..1.</summary>
        private static float Profile(float u)
        {
            u = Mathf.Clamp01(u);
            return 0.5f * u + 0.5f * u * u * (3f - 2f * u);
        }

        /// <summary>The base game's HUD panel shape: a dark slab with its lower-left corner cut at 45°.
        /// 9-sliced so the cut stays 14 px at any size.</summary>
        public static Sprite Chamfer => chamfer != null ? chamfer : (chamfer = BakeChamfer("BoscaliHud_Chamfer", 40, 14f));

        /// <summary>Texels of the left (and right) ramp of a <see cref="SunBand"/>: the sprite carries no size of its own, the image's <c>pixelsPerUnitMultiplier</c> is texels / (design units x board unit).</summary>
        public const int BandRamp = 40;

        /// <summary>
        /// The TAC BOARD's sun band: a white alpha mask whose alpha falls linearly from full at its top to nothing at its bottom, ramps from nothing to full over the <see cref="BandRamp"/> texel border at its left and
        /// back over the one at its right (3-sliced across, stretched down). With a <paramref name="fadeShare"/> above 0 the top share of it also fades in from nothing, so a band that starts below the top of the screen has
        /// no edge there (the share is baked the first time that variant is asked for).
        /// </summary>
        public static Sprite SunBand(float fadeShare) => fadeShare > 0f ? (bandFaded != null ? bandFaded : (bandFaded = BakeBand("BoscaliHud_SunBandFaded", fadeShare))) : (bandPlain != null ? bandPlain : (bandPlain = BakeBand("BoscaliHud_SunBand", 0f)));

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
