using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Core.Ui
{
    /// <summary>
    /// The HUD's own white alpha masks, tinted by <c>Image.color</c> so theme/tone changes are a
    /// colour write, never a re-bake. Shapes are signed-distance rasterised with a one-pixel
    /// anti-aliased edge, the KaceyTronic-RWR technique, but baked once as small 9-sliced
    /// textures instead of one texture per panel size. Built lazily on the main thread.
    /// </summary>
    internal static class HudSprites
    {
        private const float Radius = 5f;

        private static Sprite plate, frame, dot, ring;

        /// <summary>Filled rounded rectangle, 9-sliced; apply with <see cref="Slice"/>.</summary>
        public static Sprite Plate => plate != null ? plate : (plate = Bake("BoscaliHud_Plate", 24, Radius, 0f, true));

        /// <summary>1.5 px rounded outline matching <see cref="Plate"/>, 9-sliced.</summary>
        public static Sprite Frame => frame != null ? frame : (frame = Bake("BoscaliHud_Frame", 24, Radius, 1.5f, true));

        /// <summary>Filled circle for status lamps; use <c>Image.Type.Simple</c>.</summary>
        public static Sprite Dot => dot != null ? dot : (dot = Bake("BoscaliHud_Dot", 16, 8f, 0f, false));

        /// <summary>Hollow circle — a lamp for something transient rather than held.</summary>
        public static Sprite Ring => ring != null ? ring : (ring = Bake("BoscaliHud_Ring", 16, 8f, 2.5f, false));

        private static Sprite chamfer;

        /// <summary>The base game's HUD panel shape: a dark slab with its lower-left corner cut at 45°.
        /// 9-sliced so the cut stays 14 px at any size.</summary>
        public static Sprite Chamfer => chamfer != null ? chamfer : (chamfer = BakeChamfer("BoscaliHud_Chamfer", 40, 14f));

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

        private static Sprite Bake(string name, int size, float radius, float stroke, bool sliced)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = name,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave,
            };
            float half = size * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float qx = Mathf.Abs(x + 0.5f - half) - (half - radius);
                    float qy = Mathf.Abs(y + 0.5f - half) - (half - radius);
                    float ox = Mathf.Max(qx, 0f), oy = Mathf.Max(qy, 0f);
                    float d = Mathf.Sqrt(ox * ox + oy * oy) + Mathf.Min(Mathf.Max(qx, qy), 0f) - radius;
                    // Fill: inside the contour. Outline: a band of `stroke` just inside it.
                    float alpha = stroke > 0f
                        ? Mathf.Clamp01(0.5f - (Mathf.Abs(d + stroke * 0.5f) - stroke * 0.5f))
                        : Mathf.Clamp01(0.5f - d);
                    texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                }
            }
            texture.Apply(false, true);
            float border = sliced ? radius + 1f : 0f;
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
