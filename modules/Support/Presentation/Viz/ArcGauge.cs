using BoscaliSummer.Modules.Support.Domain.Layout;
using BoscaliSummer.Modules.Support.Presentation.Window;
using NOAvionics;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Support.Presentation.Viz
{
    /// <summary>Compact rectangular telemetry with a segmented fraction ladder.</summary>
    internal sealed class ArcGauge
    {
        private Skin skin;
        private AvGaugeGraphic gauge;
        private TMP_Text value, caption;
        private float shownFraction = -1f;
        private string shownValue, shownCaption;

        public void Build(RectTransform parent, Rect area, Skin skin)
        {
            this.skin = skin;
            gauge = Chrome.Graphic<AvGaugeGraphic>(parent,
                new Rect(area.x + 3f, area.y - area.height + 8f, area.width - 6f, 5f), "Ladder");
            gauge.Shape = AvGaugeShape.Segments;
            gauge.Segments = 8; gauge.SegmentGap = 1f; gauge.Ticks = 0;
            gauge.Track = skin.Track;
            gauge.FillColor = gauge.FillEnd = skin.Fill;
            gauge.Value = 0f;
            value = PrimitiveText.Label(parent, new Rect(area.x + 3f, area.y - 2f, area.width - 6f, 22f), skin,
                TextAlignmentOptions.Center, Mathf.Max(skin.FontSize, 14f));
            caption = PrimitiveText.Label(parent, new Rect(area.x + 3f, area.y - 24f, area.width - 6f, 14f), skin, TextAlignmentOptions.Center, 10f);
        }

        public void Set(float fraction, string valueText, string captionText)
        {
            fraction = Mathf.Clamp01(float.IsNaN(fraction) ? 0f : fraction);
            if (Mathf.Abs(fraction - shownFraction) >= 0.002f)
            {
                shownFraction = fraction;
                gauge.Value = fraction;
            }
            PrimitiveText.Write(value, skin, valueText, ref shownValue);
            PrimitiveText.Write(caption, skin, captionText, ref shownCaption);
        }
    }
}
