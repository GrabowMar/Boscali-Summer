using UnityEngine;

namespace BoscaliSummer.Modules.Autopilot.Runtime
{
    /// <summary>
    /// One procedurally drawn icon for Boscali's own entries in the native radial wheel, in
    /// the shared HUD's datalink style (a corner bracket with an inset chevron). Built once,
    /// cached, alpha-only so the native wedge's own hover tint still applies. Owned by this
    /// module only; the native wedge appearance (background, colours, layout) is untouched.
    /// </summary>
    internal static class RadialMenuIcons
    {
        private const int Size = 64;

        private static Texture2D texture;
        private static Sprite sprite;

        public static Sprite Datalink
        {
            get
            {
                if (sprite != null) return sprite;
                texture = Build(Size);
                sprite = Sprite.Create(texture, new Rect(0f, 0f, Size, Size), new Vector2(0.5f, 0.5f));
                sprite.name = "BoscaliRadialIcon";
                return sprite;
            }
        }

        internal static void Teardown()
        {
            if (sprite != null) Object.Destroy(sprite);
            if (texture != null) Object.Destroy(texture);
            sprite = null;
            texture = null;
        }

        private static Texture2D Build(int size)
        {
            var t = new Texture2D(size, size, TextureFormat.RGBA32, mipChain: false)
            {
                name = "BoscaliRadialIconTex",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave,
            };

            float margin = size * 0.14f;
            float arm = size * 0.22f;
            float stroke = size * 0.06f;

            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float a = BracketAlpha(x, y, size, margin, arm, stroke);
                    a = Mathf.Max(a, ChevronAlpha(x, y, size, stroke));
                    pixels[(y * size) + x] = new Color32(255, 255, 255, (byte)Mathf.Clamp(a * 255f, 0f, 255f));
                }
            }
            t.SetPixels32(pixels);
            t.Apply(false, true);
            return t;
        }

        /// <summary>Four L-shaped corner brackets, matching the HUD panel's own corner style.</summary>
        private static float BracketAlpha(int x, int y, int size, float margin, float arm, float stroke)
        {
            float a = 0f;
            a = Mathf.Max(a, NearCorner(x, y, margin, margin, arm, stroke, true, true));
            a = Mathf.Max(a, NearCorner(x, y, size - margin, margin, arm, stroke, false, true));
            a = Mathf.Max(a, NearCorner(x, y, margin, size - margin, arm, stroke, true, false));
            a = Mathf.Max(a, NearCorner(x, y, size - margin, size - margin, arm, stroke, false, false));
            return a;
        }

        private static float NearCorner(int x, int y, float cx, float cy, float arm, float stroke, bool right, bool up)
        {
            float dx = x + 0.5f - cx;
            float dy = y + 0.5f - cy;
            bool onHorizontal = Mathf.Abs(dy) <= stroke * 0.5f && dx * (right ? 1f : -1f) >= 0f && Mathf.Abs(dx) <= arm;
            bool onVertical = Mathf.Abs(dx) <= stroke * 0.5f && dy * (up ? 1f : -1f) >= 0f && Mathf.Abs(dy) <= arm;
            return onHorizontal || onVertical ? 1f : 0f;
        }

        /// <summary>A single chevron pointing inward, centred, marking this as an armed/linked action.</summary>
        private static float ChevronAlpha(int x, int y, int size, float stroke)
        {
            float half = size * 0.5f;
            float dx = x + 0.5f - half;
            float dy = y + 0.5f - half;
            float span = size * 0.16f;
            // Two diagonal strokes forming a '>' pointing right, inset near the centre.
            float d1 = DistanceToSegment(dx, dy, -span, span, 0f, 0f);
            float d2 = DistanceToSegment(dx, dy, -span, -span, 0f, 0f);
            float d = Mathf.Min(d1, d2);
            return d <= stroke * 0.5f ? 1f : 0f;
        }

        private static float DistanceToSegment(float px, float py, float ax, float ay, float bx, float by)
        {
            float abx = bx - ax, aby = by - ay;
            float apx = px - ax, apy = py - ay;
            float lenSq = (abx * abx) + (aby * aby);
            float t = lenSq > 0f ? Mathf.Clamp01(((apx * abx) + (apy * aby)) / lenSq) : 0f;
            float cx = ax + (abx * t), cy = ay + (aby * t);
            float dx = px - cx, dy = py - cy;
            return Mathf.Sqrt((dx * dx) + (dy * dy));
        }
    }
}
