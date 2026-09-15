using System;
using NOAvionics;
using NOAvionics.Ui;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Features.Radio.Presentation
{
    /// <summary>
    /// The receiver's spectrum display: a small scrolled texture, one row per refresh, warm
    /// colours for a strong carrier and near-black for the noise floor.
    ///
    /// <para>Rows are shifted inside one pixel buffer and uploaded once per refresh, so the
    /// whole fixture costs one <c>SetPixels32</c> per tick at 96×64 — a few thousand pixels,
    /// not a render pass. The spectrum itself is built by <see cref="Runtime.RadioSpectrum"/>.</para>
    /// </summary>
    internal sealed class RadioWaterfall
    {
        private readonly int bins;
        private readonly int rows;
        private readonly Color32[] pixels;
        private readonly Color32[] palette = new Color32[PaletteSteps];
        private Texture2D texture;
        private Image ground;

        private const int PaletteSteps = 32;

        public RadioWaterfall(RectTransform parent, Rect area, int bins, int rows)
        {
            this.bins = Mathf.Clamp(bins, 8, 256);
            this.rows = Mathf.Clamp(rows, 8, 256);
            pixels = new Color32[this.bins * this.rows];
            BuildPalette();

            ground = AvKit.Panel(parent, area, AvTheme.Unity(AvTokens.Ground));
            AvKit.Outline(parent, area, AvTheme.Frame);

            var viewObject = new GameObject("Waterfall", typeof(RectTransform), typeof(RawImage));
            var viewRect = (RectTransform)viewObject.transform;
            viewRect.SetParent(ground.rectTransform, false);
            AvKit.Place(viewRect, new Rect(1f, -1f, area.width - 2f, area.height - 2f));

            texture = new Texture2D(this.bins, this.rows, TextureFormat.RGBA32, false)
            {
                name = "BoscaliRadio.Waterfall",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };

            RawImage image = viewObject.GetComponent<RawImage>();
            image.texture = texture;
            image.raycastTarget = false;
            image.color = Color.white;
            Clear();
        }

        public bool Valid => texture != null && ground != null;

        /// <summary>Push the newest row at the top and scroll the history down by one.</summary>
        public void Push(float[] magnitudes)
        {
            if (texture == null || magnitudes == null || magnitudes.Length == 0) return;
            if (rows > 1)
                Array.Copy(pixels, bins, pixels, 0, bins * (rows - 1));

            int top = (rows - 1) * bins;
            int stride = Mathf.Max(1, magnitudes.Length / bins);
            for (int bin = 0; bin < bins; bin++)
            {
                float value = magnitudes[Mathf.Min(magnitudes.Length - 1, bin * stride)];
                int step = Mathf.Clamp((int)(value * (PaletteSteps - 1)), 0, PaletteSteps - 1);
                pixels[top + bin] = palette[step];
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, false);
        }

        public void Clear()
        {
            if (texture == null) return;
            for (int i = 0; i < pixels.Length; i++) pixels[i] = palette[0];
            texture.SetPixels32(pixels);
            texture.Apply(false, false);
        }

        public void Dispose()
        {
            if (texture != null) UnityEngine.Object.Destroy(texture);
            texture = null;
            ground = null;
        }

        /// <summary>
        /// A green-glass thermal ramp: noise floor reads as the panel ground, a carrier climbs
        /// through the accent and a saturated peak goes white-hot.
        /// </summary>
        private void BuildPalette()
        {
            Color floor = AvTheme.Ground;
            Color low = Color.Lerp(floor, AvTheme.Accent, 0.35f);
            Color mid = AvTheme.Accent;
            Color hot = Color.Lerp(AvTheme.Accent, Color.white, 0.85f);
            for (int i = 0; i < PaletteSteps; i++)
            {
                float t = i / (float)(PaletteSteps - 1);
                Color colour = t < 0.5f
                    ? Color.Lerp(floor, low, t * 2f)
                    : t < 0.8f ? Color.Lerp(low, mid, (t - 0.5f) / 0.3f)
                    : Color.Lerp(mid, hot, (t - 0.8f) / 0.2f);
                palette[i] = colour;
            }
        }
    }
}
