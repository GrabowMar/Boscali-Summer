using BoscaliSummer.Features.Support.Domain.Layout;
using BoscaliSummer.Features.Support.Presentation.Window;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Features.Support.Presentation.Viz
{
    /// <summary>A row of pips (the room's glyph): filled, ghosted (projected) and empty, with a caption.</summary>
    internal sealed class PipTrack
    {
        private Skin skin;
        private Image[] pips;
        private TMP_Text caption;
        private int shownFilled = -1, shownGhosts = -1;
        private string shownCaption;

        public void Build(RectTransform parent, Rect area, Skin skin, int count)
        {
            this.skin = skin;
            pips = new Image[Mathf.Clamp(count, 1, 24)];
            float size = Mathf.Min(14f, area.width / pips.Length - 2f);
            for (int i = 0; i < pips.Length; i++)
            {
                pips[i] = AvKit.Panel(parent, new Rect(area.x + i * (size + 2f), area.y, size, size), skin.Track);
                if (skin.Glyph != null) pips[i].sprite = skin.Glyph;
            }
            caption = PrimitiveText.Label(parent, new Rect(area.x, area.y - size - 2f, area.width, 14f), skin, TextAlignmentOptions.MidlineLeft);
        }

        public void Set(int filled, int ghosts, string captionText)
        {
            if (filled != shownFilled || ghosts != shownGhosts)
            {
                shownFilled = filled;
                shownGhosts = ghosts;
                for (int i = 0; i < pips.Length; i++)
                {
                    bool on = i < filled;
                    bool ghost = !on && i < filled + Mathf.Max(0, ghosts);
                    pips[i].color = on ? skin.Fill : ghost ? skin.Fill.WithAlpha(0.35f) : skin.Track;
                }
            }
            PrimitiveText.Write(caption, skin, captionText, ref shownCaption);
        }
    }
}
