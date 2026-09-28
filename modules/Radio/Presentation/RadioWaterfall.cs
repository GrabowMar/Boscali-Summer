using System;
using NOAvionics;
using NOAvionics.Ui;
using TMPro;
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
        private AvFrame ground;

        private const int PaletteSteps = 32;

        public RadioWaterfall(RectTransform parent, Rect area, int bins, int rows)
        {
            this.bins = Mathf.Clamp(bins, 8, 256);
            this.rows = Mathf.Clamp(rows, 8, 256);
            pixels = new Color32[this.bins * this.rows];
            BuildPalette();

            Color hairline = AvStyleHost.FuiColor("hairline", AvTheme.Hairline);
            ground = AvFrame.Add(parent, "Waterfall Ground", AvChamfer.All(0f));
            PlaceAt(ground.rectTransform, area);
            ground.Paint(AvStyleHost.FuiColor("ground", AvTheme.Ground), hairline);

            var viewObject = new GameObject("Waterfall", typeof(RectTransform), typeof(RawImage));
            var viewRect = (RectTransform)viewObject.transform;
            viewRect.SetParent(ground.rectTransform, false);
            AvLay.Place(viewRect, 1f, 1f, area.width - 2f, area.height - 2f);

            // Reticle dB reference markers for 6th/7th-gen SIGINT scope
            for (int r = 1; r <= 3; r++)
            {
                float yFrac = r * 0.25f;
                Image rule = AvLay.Solid(ground.rectTransform, "Reference " + r, hairline.WithAlpha(0.28f));
                AvLay.Place(rule.rectTransform, 1f, area.height * yFrac, area.width - 2f, 1f);
            }

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

            BuildLegend(parent, area);
        }

        /// <summary>
        /// A three-swatch key for the ramp, labelled in words: the display's meaning is never
        /// carried by its colours alone. It sits inside the waterfall's lower-left corner,
        /// over the oldest rows, and is built once.
        /// </summary>
        private void BuildLegend(RectTransform parent, Rect area)
        {
            const float swatch = 7f;
            const float step = 10f;
            float x = area.x + AvTokens.Space2;
            float y = area.y - area.height + 16f;
            Image plate = AvLay.Solid(parent, "Legend Plate", AvStyleHost.FuiColor("ground", AvTheme.Ground).WithAlpha(.82f));
            PlaceAt(plate.rectTransform, new Rect(x - 4f, y + 2f, 148f, 16f));

            LegendLabel(parent, "Noise", "NOISE", new Rect(x, y, 50f, 12f));
            x += 50f;
            for (int i = 0; i < 3; i++)
            {
                Image chip = AvLay.Solid(parent, "Swatch " + i, Ramp(0.10f + i * 0.40f));
                PlaceAt(chip.rectTransform, new Rect(x, y + 1f, swatch, swatch));
                x += step;
            }
            LegendLabel(parent, "Carrier", "CARRIER", new Rect(x + 2f, y, 64f, 12f));
        }

        private static void LegendLabel(RectTransform parent, string name, string text, Rect area)
        {
            TMP_Text label = AvText.Make(parent, name, AvTextRole.Micro, text, TextAlignmentOptions.MidlineLeft);
            PlaceAt(label.rectTransform, area);
            label.color = AvStyleHost.FuiColor("ink-dim", AvTheme.Dim);
            AvText.Fit(label, false);
        }

        /// <summary>The display rects are top-anchored with y already assigned directly (y up), so undo AvLay's flow-down sign.</summary>
        private static void PlaceAt(RectTransform target, Rect area) =>
            AvLay.Place(target, area.x, -area.y, area.width, area.height);

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
        /// The one place the ramp is written down, so the legend swatches and the pixels they
        /// explain can never drift apart. 6th/7th-generation quantum SIGINT spectrum:
        /// deep-space obsidian ground -> datalink cyan -> quantum hyper-emerald -> white-hot.
        /// </summary>
        internal static Color Ramp(float t)
        {
            Color floor = AvStyleHost.FuiColor("ground", AvTheme.Ground);
            Color low = new Color(0.00f, 0.55f, 0.70f, 1f);   // spectrum data colour: datalink cyan
            Color mid = AvStyleHost.FuiColor("ready", AvTheme.RailReady);
            Color hot = Color.white;
            t = Mathf.Clamp01(t);
            return t < 0.35f
                ? Color.Lerp(floor, low, t / 0.35f)
                : t < 0.75f ? Color.Lerp(low, mid, (t - 0.35f) / 0.40f)
                : Color.Lerp(mid, hot, (t - 0.75f) / 0.25f);
        }

        private void BuildPalette()
        {
            for (int i = 0; i < PaletteSteps; i++)
                palette[i] = Ramp(i / (float)(PaletteSteps - 1));
        }
    }
}
