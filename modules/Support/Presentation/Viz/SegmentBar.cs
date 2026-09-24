using BoscaliSummer.Features.Support.Domain.Layout;
using BoscaliSummer.Features.Support.Presentation.Window;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Features.Support.Presentation.Viz
{
    /// <summary>
    /// Success, fail and loss as three proportional segments with three different patterns (solid,
    /// the room's pattern, dashes), so colour is never the only cue; the numbers are written under it.
    /// </summary>
    internal sealed class SegmentBar
    {
        private Skin skin;
        private readonly Image[] segments = new Image[3];
        private TMP_Text words;
        private float x0, width;
        private int shownA = -1, shownB = -1, shownC = -1;
        private string shownWords;

        public void Build(RectTransform parent, Rect area, Skin skin, Color success, Color fail, Color loss)
        {
            this.skin = skin;
            x0 = area.x;
            width = area.width;
            Color[] colours = { success, fail, loss };
            for (int i = 0; i < 3; i++)
            {
                segments[i] = AvKit.Panel(parent, new Rect(area.x, area.y, 0f, 10f), colours[i]);
                Sprite pattern = i == 1 ? skin.Pattern ?? OpsSprites.Guard : i == 2 ? OpsSprites.Dash : null;
                if (pattern == null) continue;
                segments[i].sprite = pattern;
                segments[i].type = Image.Type.Tiled;
            }
            words = PrimitiveText.Label(parent, new Rect(area.x, area.y - 13f, area.width, 14f), skin, TextAlignmentOptions.MidlineLeft);
        }

        public void Set(int success, int fail, int loss)
        {
            if (success != shownA || fail != shownB || loss != shownC)
            {
                shownA = success;
                shownB = fail;
                shownC = loss;
                float total = Mathf.Max(1, success + fail + loss);
                float x = x0;
                int[] shares = { success, fail, loss };
                for (int i = 0; i < 3; i++)
                {
                    float w = width * shares[i] / total;
                    RectTransform r = segments[i].rectTransform;
                    r.anchoredPosition = new Vector2(x, r.anchoredPosition.y);
                    r.sizeDelta = new Vector2(Mathf.Max(0f, w - 2f), r.sizeDelta.y);
                    x += w;
                }
            }
            PrimitiveText.Write(words, skin, success + "% SUCCESS · " + fail + "% FAIL · " + loss + "% LOSS", ref shownWords);
        }
    }
}
