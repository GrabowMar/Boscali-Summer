using UnityEngine;
using UnityEngine.UI;

namespace NOAvionics
{
    /// <summary>
    /// Frosted glass: the blurred world (<see cref="AvBlurSource"/>) laid over an opaque tint at a low alpha, so the
    /// panel reads as dark glass with the scene's colour and light bleeding through. Holds the blur only while
    /// active; draws nothing when blur is off or unavailable, which leaves the plain tint as before.
    /// Overlay canvases only (world corners == screen pixels).
    /// </summary>
    [RequireComponent(typeof(RawImage))]
    public sealed class AvFrost : MonoBehaviour
    {
        private RawImage img;
        private RawImage image => img != null ? img : img = GetComponent<RawImage>();
        private RectTransform rect => (RectTransform)transform;
        private readonly Vector3[] corners = new Vector3[4];
        private Image backdrop;
        private Color backdropTint;

        /// <summary>A frost layer as the next child of <paramref name="parent"/>. <paramref name="bleed"/> is how much
        /// world shows through, as perceived (linear-space corrected); the top is lit, the bottom falls off.</summary>
        public static AvFrost Add(RectTransform parent, string name, float bleed, float inset = 0f)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
            go.transform.SetParent(parent, false);
            var r = (RectTransform)go.transform;
            r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one;
            r.offsetMin = new Vector2(inset, inset); r.offsetMax = new Vector2(-inset, -inset);
            var frost = go.AddComponent<AvFrost>();
            frost.image.raycastTarget = false;
            frost.image.enabled = false;
            frost.Bleed = bleed;
            AvSurfaceGradient.Apply(frost.image, Color.white, new Color(.55f, .55f, .55f, 1f));
            return frost;
        }

        public RectTransform Rect => rect;

        /// <summary>Perceived share of the blurred world, 0..1.</summary>
        public float Bleed { set => image.color = Color.white.WithAlpha(AvBundle.OverlayAlpha(value)); }

        // The camera supplies its first sample after the map opens. Keep the sharp world visible at the
        // requested opacity until a blurred sample exists, then composite that sample over a solid tint.
        public void SetBackdrop(Image surface, Color tint)
        {
            backdrop = surface;
            backdropTint = tint;
            SyncBackdrop(image.enabled && AvBlurSource.Enabled && AvBlurSource.Texture != null);
        }

        private void SyncBackdrop(bool hasBlur)
        {
            if (backdrop != null) backdrop.color = hasBlur ? backdropTint.WithAlpha(1f) : backdropTint;
        }

        private void OnEnable() => AvBlurSource.Acquire();
        private void OnDisable() { AvBlurSource.Release(); image.enabled = false; SyncBackdrop(false); }

        // Touches the graphic only when the texture or the screen rect changed, so a still panel never rebatches.
        private void LateUpdate()
        {
            RenderTexture rt = AvBlurSource.Enabled ? AvBlurSource.Texture : null;
            if (image.texture != rt) image.texture = rt;
            if (image.enabled != (rt != null))
            {
                image.enabled = rt != null;
                SyncBackdrop(image.enabled);
            }
            if (rt == null || Screen.width < 1 || Screen.height < 1) return;
            rect.GetWorldCorners(corners);
            var uv = new Rect(corners[0].x / Screen.width, corners[0].y / Screen.height,
                (corners[2].x - corners[0].x) / Screen.width, (corners[2].y - corners[0].y) / Screen.height);
            if (uv != image.uvRect) image.uvRect = uv;
        }
    }
}
