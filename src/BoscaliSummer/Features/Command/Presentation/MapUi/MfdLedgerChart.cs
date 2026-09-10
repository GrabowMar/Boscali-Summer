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

        public MfdLedgerChart(RectTransform parent, float y, float panelWidth)
        {
            float x = AvTokens.Space3;
            width = panelWidth - x - 154f;
            legend = AvStyled.Label(parent, new Rect(x, y, panelWidth-x, 14f), "", "row-sub");
            y -= 20f;
            string[] names = { "BLD", "VEH", "SHP", "AIR" };
            for (int i = 0; i < 4; i++)
            {
                float top = y-i*28f;
                AvStyled.Label(parent, new Rect(x, top, 36f, 20f), names[i], "row-main");
                AvKit.Rule(parent, new Rect(x+42f, top-2f, width, 16f), AvTheme.SurfaceRaised);
                primary[i] = AvKit.Rule(parent, new Rect(x+42f, top-2f, 0f, 7f), AvTheme.Accent);
                secondary[i] = AvKit.Rule(parent, new Rect(x+42f, top-11f, 0f, 4f), AvTheme.Warning);
                values[i] = AvStyled.Label(parent, new Rect(x+48f+width, top, 106f, 20f), "", "row-sub",
                    align: TextAlignmentOptions.MidlineRight);
            }
            scale = AvStyled.Label(parent, new Rect(x, y-112f, panelWidth-x, 12f), "", "row-sub");
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
                primary[i].rectTransform.sizeDelta = new Vector2(width * MfdChartScale.Fraction(first[i], maximum), 7f);
                secondary[i].rectTransform.sizeDelta = new Vector2(width * MfdChartScale.Fraction(second[i], maximum), 4f);
                values[i].text = format(first[i]) + " / " + format(second[i]);
            }
        }
    }
}
