using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace NOAvionics
{
    /// <summary>A single, non-interactive glass finish above an MFD's complete UI tree.</summary>
    public sealed class AvDisplayGlass : MonoBehaviour
    {
        private Image image;
        private float nextLightSample;
        private static bool effects = true, autoLight = true;
        private static float reflection = .35f, scanStrength, edgeStrength;
        private static int revision;
        private static Texture2D scanTexture, edgeTexture;
        private int appliedRevision = -1;
        private int appliedFxRevision = -1;
        private float appliedLight = -1f;
        private RawImage scanlines, vignette;
        private bool shaderMode;
        private static bool glitchVisible;
        private static readonly List<AvDisplayGlass> visible = new List<AvDisplayGlass>(4);
        private static readonly int ReflectionId = Shader.PropertyToID("_Reflection");
        private static readonly int ScanId = Shader.PropertyToID("_Scan");
        private static readonly int EdgeId = Shader.PropertyToID("_Edge");
        private static readonly int TintStrengthId = Shader.PropertyToID("_TintStrength");
        private static readonly int LightId = Shader.PropertyToID("_Light");
        private static readonly int LifeId = Shader.PropertyToID("_Life");
        private static readonly int BootId = Shader.PropertyToID("_NOA_Boot");
        private static float life = .6f;

        /// <summary>
        /// Live signal (0..1) of the full-display finish: the edge commissioning trace, the slow refresh sweep and how hard a
        /// jam or hit breaks the picture up. 0 is a still display. Needs the Glass2 shader; ignored without it.
        /// </summary>
        public static void ConfigureLife(float value)
        {
            value = Mathf.Clamp01(value);
            if (Mathf.Approximately(life, value)) return;
            life = value;
            revision++;
            RefreshVisible();
        }

        // A brief edge trace commissions the display without covering its content (shader side: _NOA_Boot).
        private void PowerOn()
        {
            if (shaderMode && effects && life > 0f && AvFxDriver.Tier == AvFxTier.Full && !AvFxDriver.ReducedMotion && Application.isPlaying)
                Shader.SetGlobalFloat(BootId, AvFxDriver.Now);
        }

        private void OnEnable()
        {
            if (!visible.Contains(this)) visible.Add(this);
            AvFxDriver.Changed += SettingsChanged;
            SettingsChanged();
            PowerOn();
        }

        private void OnDisable()
        {
            visible.Remove(this);
            AvFxDriver.Changed -= SettingsChanged;
        }

        private void SettingsChanged() { appliedRevision = -1; Update(); }

        private static void RefreshVisible()
        {
            for (int i = 0; i < visible.Count; i++) visible[i].SettingsChanged();
        }

        internal static bool HasLiveDisplay
        {
            get
            {
                if (!effects || AvFxDriver.Tier != AvFxTier.Full || AvFxDriver.ReducedMotion) return false;
                for (int i = 0; i < visible.Count; i++)
                {
                    AvDisplayGlass display = visible[i];
                    if (display.shaderMode && display.image != null && display.image.canvas != null && display.image.canvas.isActiveAndEnabled)
                        return true;
                }
                return false;
            }
        }

        internal static void SetGlitchVisible(bool value)
        {
            if (glitchVisible == value) return;
            glitchVisible = value;
            RefreshVisible();
        }

        // Presentation values only: the owning feature binds and persists its settings.
        public static void Configure(bool enabled, float glass, bool adaptToLight,
            float scan, float edge)
        {
            glass = Mathf.Clamp01(glass); scan = Mathf.Clamp01(scan); edge = Mathf.Clamp01(edge);
            if (effects == enabled && reflection == glass && autoLight == adaptToLight && scanStrength == scan && edgeStrength == edge) return;
            effects = enabled;
            reflection = glass;
            autoLight = adaptToLight;
            scanStrength = scan;
            edgeStrength = edge;
            revision++;
            RefreshVisible();
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
            Material glassMat = AvFxDriver.GlassMaterial;
            if (glassMat != null)
            {
                // One procedural quad replaces the scanline/vignette layers (kit v2, spec §7.2).
                glass.sprite = null;
                glass.material = glassMat;
                driver.shaderMode = true;
                AvFxDriver.EnsureRunning();
                driver.PowerOn();
            }
            else
            {
                EnsureTextures();
                driver.scanlines = Layer(glass.rectTransform, "CrtScanlines", scanTexture);
                driver.vignette = Layer(glass.rectTransform, "EdgeShading", edgeTexture);
            }
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
            AvLay.Fill(rect);
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
            bool changed = appliedRevision != revision || appliedFxRevision != AvFxDriver.Revision;
            bool sampleLight = effects && autoLight && reflection > 0f;
            if (image == null || (!changed && (!sampleLight || now < nextLightSample))) return;
            nextLightSample = now + 0.75f;
            appliedRevision = revision;
            appliedFxRevision = AvFxDriver.Revision;

            float level = 1f;
            if (sampleLight)
            {
                float ambient = Mathf.Clamp01(RenderSettings.ambientIntensity);
                Light sun = RenderSettings.sun;
                float daylight = sun != null && sun.enabled
                    ? Mathf.Clamp01(Vector3.Dot(-sun.transform.forward, Vector3.up) * sun.intensity) : 0f;
                level = 0.45f + 0.55f * Mathf.Clamp01(ambient * 0.55f + daylight * 0.45f);
            }
            if (shaderMode)
            {
                Material m = image.material;
                bool motion = AvFxDriver.Tier == AvFxTier.Full && !AvFxDriver.ReducedMotion;
                if (changed)
                {
                    m.SetFloat(ReflectionId, reflection * .45f);
                    m.SetFloat(ScanId, scanStrength);
                    m.SetFloat(EdgeId, edgeStrength);
                    // Older asset bundles still expose the retired colour-wash property.
                    if (m.HasProperty(TintStrengthId)) m.SetFloat(TintStrengthId, 0f);
                    m.SetFloat(LifeId, motion ? life : 0f);
                    image.color = Color.white;
                    // CLEAR has no transparent full-screen draw. A real jam wakes the same quad.
                    image.enabled = effects && (reflection > 0f || scanStrength > 0f || edgeStrength > 0f || (motion && (life > 0f || glitchVisible)));
                }
                if (changed || !Mathf.Approximately(appliedLight, level)) m.SetFloat(LightId, level);
                appliedLight = level;
                return;
            }
            image.color = new Color(1f, 1f, 1f, effects ? reflection * level * .45f : 0f);
            // Disable only the Graphic: this driver must stay awake so OFF can be reversed.
            image.enabled = effects && reflection > 0f;
            if (scanlines == null) return;
            scanlines.enabled = effects && scanStrength > 0f;
            scanlines.color = new Color(0f, 0f, 0f, scanStrength * .28f);
            scanlines.uvRect = new Rect(0f, 0f, 1f, Mathf.Max(1f, image.rectTransform.rect.height / 4f));
            vignette.enabled = effects && edgeStrength > 0f;
            vignette.color = new Color(0f, 0f, 0f, edgeStrength * .5f);
        }

        private static RawImage Layer(RectTransform parent, string name, Texture texture)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(RawImage));
            go.transform.SetParent(parent, false);
            AvLay.Fill((RectTransform)go.transform);
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
