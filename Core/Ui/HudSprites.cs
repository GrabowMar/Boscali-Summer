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
