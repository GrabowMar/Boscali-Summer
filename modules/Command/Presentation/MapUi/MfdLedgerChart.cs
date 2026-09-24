using NOAvionics;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Features.Command.Presentation.MapUi
{
    internal sealed class MfdLedgerChart
    {
        private readonly Image[] primary = new Image[4];
        private readonly Image[] secondary = new Image[4];
        private readonly TMP_Text[] values = new TMP_Text[4];
        private readonly TMP_Text legend;
        private readonly TMP_Text scale;
        private readonly float width;
        private readonly float rowPitch;
        private readonly float primaryHeight, secondaryHeight;

        public MfdLedgerChart(RectTransform parent, float y, float panelWidth, float availableHeight)
        {
            float x = AvTokens.Space3;
            width = panelWidth - 2f * x;
            rowPitch = Mathf.Clamp((availableHeight - 34f) / 4f, 28f, 64f);
            primaryHeight = rowPitch > 40f ? 12f : 5f;
            secondaryHeight = rowPitch > 40f ? 8f : 3f;
            legend = AvStyled.Label(parent, new Rect(x, y, width, 14f), "", "row-sub");
            y -= 20f;
            string[] names = { "BUILDINGS", "VEHICLES", "SHIPS", "AIRCRAFT" };
            for (int i = 0; i < 4; i++)
            {
                float top = y-i*rowPitch;
                AvStyled.Label(parent, new Rect(x, top, width * .4f, 16f), names[i], "row-main");
                AvKit.Rule(parent, new Rect(x, top-16f, width, primaryHeight+secondaryHeight+2f), AvTheme.SurfaceRaised);
                for (int tick = 1; tick < 4; tick++)
                    AvKit.Rule(parent, new Rect(x + width * tick / 4f, top - 16f,
                        1f, primaryHeight + secondaryHeight + 2f),
                        AvTheme.Hairline.WithAlpha(0.55f));
                primary[i] = AvKit.Rule(parent, new Rect(x, top-16f, 0f, primaryHeight), AvTheme.Accent);
                secondary[i] = AvKit.Rule(parent, new Rect(x, top-18f-primaryHeight, 0f, secondaryHeight), AvTheme.Warning);
                AvKit.Rule(parent, new Rect(x, top - 16f, 2f,
                    primaryHeight + secondaryHeight + 2f), AvTheme.RailInfo);
                values[i] = AvStyled.Label(parent, new Rect(x+width*.4f, top, width*.6f, 16f), "", "row-main",
                    align: TextAlignmentOptions.MidlineRight);
            }
            scale = AvStyled.Label(parent, new Rect(x, y-4f*rowPitch, width, 14f), "", "row-sub");
        }

        public void Set(float[] first, float[] second, string firstName, string secondName, string unit,
            System.Func<float, string> format)
        {
            float maximum = 0f;
            for (int i = 0; i < 4; i++) maximum = Mathf.Max(maximum, first[i], second[i]);
            legend.text = "TOP: " + firstName + "   /   LOWER: " + secondName;
            scale.text = maximum > 0f ? "COMMON SCALE  0 — " + format(maximum) + " " + unit : "NO RECORDED DATA";
            for (int i = 0; i < 4; i++)
            {
                primary[i].rectTransform.sizeDelta = new Vector2(width * MfdChartScale.Fraction(first[i], maximum), primaryHeight);
                secondary[i].rectTransform.sizeDelta = new Vector2(width * MfdChartScale.Fraction(second[i], maximum), secondaryHeight);
                values[i].text = format(first[i]) + " / " + format(second[i]);
            }
        }

        internal void Clear()
        {
            legend.text = "WAITING FOR MISSION STATISTICS";
            scale.text = "—";
            for (int i = 0; i < 4; i++)
            {
                primary[i].rectTransform.sizeDelta = Vector2.zero;
                secondary[i].rectTransform.sizeDelta = Vector2.zero;
                values[i].text = "— / —";
            }
        }
    }
}
