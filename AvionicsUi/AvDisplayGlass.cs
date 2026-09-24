using UnityEngine;
using UnityEngine.UI;

namespace NOAvionics.Ui
{
    /// <summary>A single, non-interactive glass finish above an MFD's complete UI tree.</summary>
    public sealed class AvDisplayGlass : MonoBehaviour
    {
        private Image image;
        private float nextLightSample;
        private static bool effects = true, autoLight = true;
        private static float reflection = .6f, scanStrength, edgeStrength, tintStrength = .25f;
        private static int tintIndex, revision;
        private static Texture2D scanTexture, edgeTexture;
        private static readonly Color[] Tints = { Color.white,
            new Color(.22f, 1f, .48f), new Color(1f, .64f, .18f),
            new Color(.25f, .75f, 1f), new Color(1f, .38f, .57f) };
        private int appliedRevision = -1;
        private RawImage scanlines, vignette, tint;

        // Presentation values only: the owning feature binds and persists its settings.
        public static void Configure(bool enabled, float glass, bool adaptToLight,
            float scan, float edge, int color, float colorStrength)
        {
            effects = enabled;
            reflection = Mathf.Clamp01(glass);
            autoLight = adaptToLight;
            scanStrength = Mathf.Clamp01(scan);
            edgeStrength = Mathf.Clamp01(edge);
            tintIndex = Mathf.Clamp(color, 0, Tints.Length - 1);
            tintStrength = Mathf.Clamp01(colorStrength);
            revision++;
        }

        public static Image Attach(RectTransform content)
        {
            return Attach(content, "DisplayGlass", AvSprites.DisplayGlass, 6f);
        }

        /// <summary>One passive finish for the entire maximized display, including the map.</summary>
        public static Image AttachFullDisplay(RectTransform content)
        {
            Image glass = Attach(content, "DisplayScreenFinish", AvSprites.DisplayScreen, 0f);
            if (glass == null) return null;
            var driver = glass.GetComponent<AvDisplayGlass>();
            EnsureTextures();
            driver.tint = Layer(glass.rectTransform, "ColorTint", Texture2D.whiteTexture);
            driver.scanlines = Layer(glass.rectTransform, "CrtScanlines", scanTexture);
            driver.vignette = Layer(glass.rectTransform, "EdgeShading", edgeTexture);
            driver.appliedRevision = -1;
            driver.Update();
            return glass;
        }

        private static Image Attach(RectTransform content, string name, Sprite sprite, float inset)
        {
            if (content == null) return null;

            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            var rect = (RectTransform)go.transform;
            rect.SetParent(content, false);
            AvKit.Stretch(rect);
            // Panel glass stays inside its bezel; the display finish spans every column.
            rect.offsetMin = new Vector2(inset, inset);
            rect.offsetMax = new Vector2(-inset, -inset);

            Image glass = go.GetComponent<Image>();
            glass.sprite = sprite;
            glass.type = Image.Type.Simple;
            glass.raycastTarget = false;
            glass.color = new Color(1f, 1f, 1f, 0.72f);
            var driver = go.AddComponent<AvDisplayGlass>();
            driver.image = glass;
            driver.Update();
            return glass;
        }

        public void Awake() => image = GetComponent<Image>();

        public void Update()
        {
            float now = Time.unscaledTime;
            if (image == null || (appliedRevision == revision && now < nextLightSample)) return;
            nextLightSample = now + 0.75f;
            appliedRevision = revision;

            float ambient = Mathf.Clamp01(RenderSettings.ambientIntensity);
            Light sun = RenderSettings.sun;
            float daylight = sun != null && sun.enabled
                ? Mathf.Clamp01(Vector3.Dot(-sun.transform.forward, Vector3.up) * sun.intensity)
                : 0f;
            float level = autoLight
                ? 0.45f + 0.55f * Mathf.Clamp01(ambient * 0.55f + daylight * 0.45f) : 1f;
            image.color = new Color(1f, 1f, 1f, effects ? reflection * level : 0f);
            // Disable only the Graphic: this driver must stay awake so OFF can be reversed.
            image.enabled = effects && reflection > 0f;
            if (scanlines == null) return;
            scanlines.enabled = effects && scanStrength > 0f;
            scanlines.color = new Color(0f, 0f, 0f, scanStrength * .28f);
            scanlines.uvRect = new Rect(0f, 0f, 1f, Mathf.Max(1f, image.rectTransform.rect.height / 4f));
            vignette.enabled = effects && edgeStrength > 0f;
            vignette.color = new Color(0f, 0f, 0f, edgeStrength * .5f);
            tint.enabled = effects && tintIndex != 0 && tintStrength > 0f;
            Color wash = Tints[tintIndex];
            wash.a = tintStrength * .18f;
            tint.color = wash;
        }

        private static RawImage Layer(RectTransform parent, string name, Texture texture)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(RawImage));
            go.transform.SetParent(parent, false);
            AvKit.Stretch((RectTransform)go.transform);
            var graphic = go.GetComponent<RawImage>();
            graphic.texture = texture;
            graphic.raycastTarget = false;
            graphic.enabled = false;
            return graphic;
        }

        private static void EnsureTextures()
        {
            // Two shared, immutable textures. No per-frame pixels, materials or UI objects.
            if (scanTexture == null)
            {
                scanTexture = new Texture2D(1, 4, TextureFormat.RGBA32, false)
                { name = "AvCrtScanlines", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Point };
                scanTexture.SetPixels(new[] { Color.black, Color.clear, Color.clear, Color.clear });
                scanTexture.Apply(false, true);
            }
            if (edgeTexture != null) return;
            const int size = 128;
            edgeTexture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            { name = "AvDisplayVignette", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = Mathf.Abs(2f * x / (size - 1) - 1f);
                    float dy = Mathf.Abs(2f * y / (size - 1) - 1f);
                    float edge = Mathf.Max(dx, dy);
                    pixels[y * size + x] = new Color(0f, 0f, 0f, Mathf.SmoothStep(0f, 1f,
                        Mathf.Clamp01((edge - .55f) / .45f)));
                }
            edgeTexture.SetPixels(pixels);
            edgeTexture.Apply(false, true);
        }
    }
}
