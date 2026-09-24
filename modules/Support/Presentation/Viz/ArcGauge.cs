using BoscaliSummer.Features.Support.Domain.Layout;
using BoscaliSummer.Features.Support.Presentation.Window;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Features.Support.Presentation.Viz
{
    /// <summary>A radial fraction with its value in the middle and a caption under it.</summary>
    internal sealed class ArcGauge
    {
        private Skin skin;
        private Image fill;
        private TMP_Text value, caption;
        private float shownFraction = -1f;
        private string shownValue, shownCaption;

        public void Build(RectTransform parent, Rect area, Skin skin)
        {
            this.skin = skin;
            OpsSprites.Ensure();
            float d = Mathf.Min(area.width, area.height - 16f);
            var ring = new Rect(area.x + (area.width - d) * 0.5f, area.y, d, d);
            Image track = AvKit.Panel(parent, ring, skin.Track, OpsSprites.Arc);
            track.type = Image.Type.Simple;
            fill = AvKit.Panel(parent, ring, skin.Fill, OpsSprites.Arc);
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Radial360;
            fill.fillOrigin = (int)Image.Origin360.Top;
            fill.fillClockwise = true;
            fill.fillAmount = 0f;
            float size = Mathf.Max(skin.FontSize, d * 0.22f);
            float height = size * 1.4f;
            value = PrimitiveText.Label(parent, new Rect(ring.x + d * 0.15f, ring.y - (d - height) * 0.5f, d * 0.7f, height), skin,
                TextAlignmentOptions.Center, size);
            caption = PrimitiveText.Label(parent, new Rect(area.x, area.y - d - 2f, area.width, 14f), skin, TextAlignmentOptions.Center, 10f);
        }

        public void Set(float fraction, string valueText, string captionText)
        {
            fraction = Mathf.Clamp01(float.IsNaN(fraction) ? 0f : fraction);
            if (Mathf.Abs(fraction - shownFraction) >= 0.002f)
            {
                shownFraction = fraction;
                fill.fillAmount = fraction;
            }
            PrimitiveText.Write(value, skin, valueText, ref shownValue);
            PrimitiveText.Write(caption, skin, captionText, ref shownCaption);
        }
    }
}
