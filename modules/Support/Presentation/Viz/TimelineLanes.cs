using BoscaliSummer.Features.Support.Domain.Layout;
using BoscaliSummer.Features.Support.Presentation.Window;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Features.Support.Presentation.Viz
{
    /// <summary>One lane of time: current phases solid, projected ones patterned, a now cursor, a caption.</summary>
    internal sealed class TimelineLanes
    {
        private const int Segments = 6;
        private Skin skin;
        private readonly Image[] bars = new Image[Segments];
        private TMP_Text caption;
        private float x0, width;
        private int signature = int.MinValue;
        private string shownCaption;

        public void Build(RectTransform parent, Rect area, Skin skin)
        {
            this.skin = skin;
            x0 = area.x;
            width = area.width;
            AvKit.Panel(parent, new Rect(area.x, area.y, area.width, 12f), skin.Track);
            for (int i = 0; i < Segments; i++)
            {
                bars[i] = AvKit.Panel(parent, new Rect(area.x, area.y, 0f, 12f), skin.Fill);
                bars[i].enabled = false;
            }
            AvKit.Panel(parent, new Rect(area.x, area.y + 3f, 2f, 18f), skin.Mark);
            caption = PrimitiveText.Label(parent, new Rect(area.x, area.y - 15f, area.width, 14f), skin, TextAlignmentOptions.MidlineLeft);
        }

        public void Set(LaneSegment[] segments, int count, Color[] kindColours, string captionText)
        {
            int hash = count;
            for (int i = 0; i < count && segments != null; i++)
                hash = hash * 31 + (int)(segments[i].Start * 1000f) * 7 + (int)(segments[i].End * 1000f) + (int)segments[i].Kind;
            if (hash != signature)
            {
                signature = hash;
                for (int i = 0; i < Segments; i++)
                {
                    bool on = segments != null && i < count && segments[i].Kind != LaneKind.Empty;
                    bars[i].enabled = on;
                    if (!on) continue;
                    LaneSegment s = segments[i];
                    RectTransform r = bars[i].rectTransform;
                    r.anchoredPosition = new Vector2(x0 + s.Start * width, r.anchoredPosition.y);
                    r.sizeDelta = new Vector2(Mathf.Max(1f, (s.End - s.Start) * width - 1f), r.sizeDelta.y);
                    Color c = kindColours != null && (int)s.Kind < kindColours.Length ? kindColours[(int)s.Kind] : skin.Fill;
                    bars[i].color = s.Projected ? c.WithAlpha(0.5f) : c;
                    bars[i].sprite = s.Projected ? skin.Pattern ?? OpsSprites.Guard : null;
                    bars[i].type = s.Projected ? Image.Type.Tiled : Image.Type.Simple;
                }
            }
            PrimitiveText.Write(caption, skin, captionText, ref shownCaption);
        }
    }
}
