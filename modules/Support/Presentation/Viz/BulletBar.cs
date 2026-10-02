using BoscaliSummer.Modules.Support.Domain.Layout;
using BoscaliSummer.Modules.Support.Presentation.Window;
using NOAvionics;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Support.Presentation.Viz
{
    /// <summary>A labelled measure: the words are the fact, the bar repeats it, a tick marks a limit.</summary>
    internal sealed class BulletBar
    {
        private Skin skin;
        private AvGaugeGraphic bar;
        private Image limit, pattern;
        private TMP_Text label, value;
        private float width, barX, barHeight;
        private float shownFraction = -1f, shownLimit = -1f;
        private string shownLabel, shownValue;

        public void Build(RectTransform parent, Rect area, Skin skin)
        {
            this.skin = skin;
            width = area.width;
            label = PrimitiveText.Label(parent, new Rect(area.x, area.y, area.width * 0.6f, 14f), skin, TextAlignmentOptions.MidlineLeft);
            value = PrimitiveText.Label(parent, new Rect(area.x + area.width * 0.4f, area.y, area.width * 0.6f, 14f), skin,
                TextAlignmentOptions.MidlineRight);
            float barY = area.y - 17f;
            float barH = Mathf.Max(4f, area.height - 18f);
            barX = area.x;
            barHeight = barH;
            bar = Chrome.Graphic<AvGaugeGraphic>(parent, new Rect(area.x, barY, area.width, barH), "Bar");
            bar.Shape = AvGaugeShape.Bar;
            bar.Track = skin.Track;
            bar.FillColor = bar.FillEnd = skin.Fill;
            bar.Value = 0f;
            // A pattern for fills that must not rely on colour: tiled over the filled part only.
            if (skin.Pattern != null)
            {
                pattern = Chrome.Panel(parent, new Rect(area.x, barY, 0f, barH), skin.Track, skin.Pattern);
                pattern.type = Image.Type.Tiled;
            }
            limit = Chrome.Panel(parent, new Rect(area.x, barY + 3f, 2f, barH + 6f), skin.Mark);
            limit.enabled = false;
        }

        public void Set(float fraction, float limitFraction, string labelText, string valueText)
        {
            fraction = Mathf.Clamp01(float.IsNaN(fraction) ? 0f : fraction);
            if (Mathf.Abs(fraction - shownFraction) >= 0.002f)
            {
                shownFraction = fraction;
                bar.Value = fraction;
                if (pattern != null) pattern.rectTransform.sizeDelta = new Vector2(width * fraction, barHeight);
            }
            if (Mathf.Abs(limitFraction - shownLimit) >= 0.002f)
            {
                shownLimit = limitFraction;
                limit.enabled = limitFraction > 0f && limitFraction < 1f;
                Vector2 at = limit.rectTransform.anchoredPosition;
                limit.rectTransform.anchoredPosition = new Vector2(barX + width * limitFraction - 1f, at.y);
            }
            PrimitiveText.Write(label, skin, labelText, ref shownLabel);
            PrimitiveText.Write(value, skin, valueText, ref shownValue);
        }
    }
}
