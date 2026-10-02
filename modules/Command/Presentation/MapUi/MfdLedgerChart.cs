using NOAvionics;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Command.Presentation.MapUi
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
            const float x = 12f;
            width = panelWidth - 2f * x;
            rowPitch = Mathf.Clamp((availableHeight - 34f) / 4f, 28f, 64f);
            primaryHeight = rowPitch > 40f ? 12f : 5f;
            secondaryHeight = rowPitch > 40f ? 8f : 3f;
            Color track = AvStyleHost.FuiColor("surface-raised", AvTheme.SurfaceRaised);
            Color grid = AvStyleHost.FuiColor("hairline", AvTheme.Hairline).WithAlpha(0.55f);
            Color primaryInk = AvStyleHost.FuiColor("ready", AvTheme.Accent);
            Color secondaryInk = AvStyleHost.FuiColor("caution", AvTheme.Warning);
            Color rail = AvStyleHost.FuiColor("info", AvTheme.RailInfo);
            legend = Label(parent, "Legend", AvTextRole.Micro, new Rect(x, y, width, 14f), "ink-dim", TextAlignmentOptions.MidlineLeft);
            y -= 20f;
            string[] names = { "BUILDINGS", "VEHICLES", "SHIPS", "AIRCRAFT" };
            for (int i = 0; i < 4; i++)
            {
                float top = y - i * rowPitch;
                Label(parent, "Name " + names[i], AvTextRole.Label, new Rect(x, top, width * .4f, 16f), "ink",
                    TextAlignmentOptions.MidlineLeft).text = names[i];
                MfdChromeLay.Rule(parent, "Track", new Rect(x, top - 16f, width, primaryHeight + secondaryHeight + 2f), track);
                for (int tick = 1; tick < 4; tick++)
                    MfdChromeLay.Rule(parent, "Tick", new Rect(x + width * tick / 4f, top - 16f,
                        1f, primaryHeight + secondaryHeight + 2f), grid);
                primary[i] = MfdChromeLay.Rule(parent, "Primary", new Rect(x, top - 16f, 0f, primaryHeight), primaryInk);
                secondary[i] = MfdChromeLay.Rule(parent, "Secondary", new Rect(x, top - 18f - primaryHeight, 0f, secondaryHeight), secondaryInk);
                MfdChromeLay.Rule(parent, "Rail", new Rect(x, top - 16f, 2f, primaryHeight + secondaryHeight + 2f), rail);
                values[i] = Label(parent, "Value " + names[i], AvTextRole.DataSmall, new Rect(x + width * .4f, top, width * .6f, 16f),
                    "ink", TextAlignmentOptions.MidlineRight);
            }
            scale = Label(parent, "Scale", AvTextRole.DataSmall, new Rect(x, y - 4f * rowPitch, width, 14f), "ink-dim",
                TextAlignmentOptions.MidlineLeft);
        }

        /// <summary>A fixed-box caption: kit type role, role colour, shrinks toward the 11 px floor instead of spilling.</summary>
        private static TMP_Text Label(RectTransform parent, string name, AvTextRole role, Rect area, string colourRole,
            TextAlignmentOptions align)
        {
            TMP_Text text = AvText.Make(parent, name, role, "", align);
            MfdChromeLay.Place(text.rectTransform, area);
            text.color = AvStyleHost.FuiColor(colourRole, AvTheme.TextPrimary);
            AvText.Fit(text, false);
            return text;
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
