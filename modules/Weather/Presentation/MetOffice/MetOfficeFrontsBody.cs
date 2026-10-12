using BoscaliSummer.Modules.Weather.Domain;
using NOAvionics;
using TMPro;
using UnityEngine;

namespace BoscaliSummer.Modules.Weather.Presentation
{
    /// <summary>The inside of the NEXT FRONTS card (R4): the two nearest fronts, each with its symbol, distance and bearing and ETA (STATIC while the front stands still).</summary>
    internal sealed class MetFrontsBody : EnvPart
    {
        private const int Rows = 2;
        private const float RowH = 20f;
        private readonly TMP_Text[] word = new TMP_Text[Rows], where = new TMP_Text[Rows], eta = new TMP_Text[Rows];
        private readonly TMP_Text none;
        private readonly MetFrontKind[] kind = new MetFrontKind[Rows];
        private readonly bool[] used = new bool[Rows];
        private readonly int[] order = new int[MetOfficeModel.MaxFronts];
        private readonly float[] dist = new float[MetOfficeModel.MaxFronts];

        public MetFrontsBody(RectTransform parent) : base(parent, "Next fronts", null)
        {
            MakeArt(Paint);
            for (int r = 0; r < Rows; r++)
            {
                string ink = r == 0 ? "ink" : "ink-dim";
                word[r] = Txt("Word" + r, AvTextRole.Head, TextAlignmentOptions.MidlineLeft, ink, false, true);
                where[r] = Txt("Where" + r, AvTextRole.DataSmall, TextAlignmentOptions.MidlineLeft, ink, false, true);
                eta[r] = Txt("Eta" + r, AvTextRole.DataStrong, TextAlignmentOptions.MidlineRight, r == 0 ? "key" : "ink-dim", false, true);
            }
            none = Txt("None", AvTextRole.Micro, TextAlignmentOptions.MidlineLeft, "ink-dim", false, true);
            Restyle();
        }

        public override float Measure(float width) => MetOfficeLayout.FrontsCard - MetOfficeLayout.CardChrome;

        public void Set(MetOfficeModel m)
        {
            int n = MetChartMath.NextFronts(m, order, dist);
            for (int r = 0; r < Rows; r++)
            {
                used[r] = r < n;
                if (r < n)
                {
                    MetFront f = m.Fronts[order[r]];
                    MetChartMath.Nearest(m.CamX, m.CamZ, f, out float d, out float bearing);
                    kind[r] = f.Kind;
                    AvText.Set(word[r], MetOfficeText.FrontWord(f.Kind));
                    AvText.Set(where[r], MetOfficeText.Where(d, bearing));
                    AvText.Set(eta[r], MetOfficeText.Eta(f.EtaMinutes));
                }
                else
                {
                    AvText.Set(word[r], r == 0 ? "NO FRONT" : "");
                    AvText.Set(where[r], ""); AvText.Set(eta[r], "");
                }
            }
            AvText.Set(none, n == 0 ? "NONE IN ±150 KM · FRONTS ARRIVE WITH TAPE (4B)" : "");
            Redraw();
        }

        protected override void Layout(float w, float h)
        {
            AvLay.Place(none, 0f, RowH, w, RowH - 1f);
            for (int r = 0; r < Rows; r++)
            {
                float y = r * RowH;
                AvLay.Place(word[r], 30f, y, 112f, RowH - 1f);
                AvLay.Place(where[r], 144f, y, 74f, RowH - 1f);
                AvLay.Place(eta[r], w - 76f, y, 76f, RowH - 1f);
            }
        }

        private void Paint(EnvCanvas c)
        {
            for (int r = 0; r < Rows; r++)
                if (used[r]) MetFrontGlyph.Draw(c, kind[r], 0f, r * RowH - 1f);
        }
    }

    /// <summary>The front symbols of the mockup in a 28 x 16 box: warm half-discs, cold triangles, an occluded alternation, a dash-dot squall line. Shared by the chart legend and the next-fronts card.</summary>
    internal static class MetFrontGlyph
    {
        public static Color Ink(MetFrontKind kind)
        {
            switch (kind)
            {
                case MetFrontKind.Warm: return EnvInk.Role("caution");
                case MetFrontKind.Squall: return EnvInk.Role("danger");
                case MetFrontKind.Occluded: return EnvInk.Role("ink-dim");
                default: return EnvInk.Role("info");
            }
        }

        public static void Draw(EnvCanvas c, MetFrontKind kind, float x, float y)
        {
            Color col = Ink(kind);
            float b = y + 13f;
            if (kind == MetFrontKind.Squall) { c.Dashed(x + 1f, y + 8f, x + 27f, y + 8f, 1.4f, 6f, 3f, col); return; }
            c.Line(x + 1f, b, x + 27f, b, 1.5f, col);
            if (kind == MetFrontKind.Warm || kind == MetFrontKind.Occluded)
                for (float cx = x + 7f; cx < x + 26f; cx += 12f) c.Arc(cx, b, 4f, 4f, 0f, 180f, 1.6f, kind == MetFrontKind.Occluded ? EnvInk.Role("caution") : col);
            if (kind == MetFrontKind.Cold)
                for (float cx = x + 7f; cx < x + 26f; cx += 12f) c.Tri(cx - 4f, b, cx + 4f, b, cx, b - 7f, col);
            if (kind == MetFrontKind.Occluded) c.Tri(x + 15f, b, x + 23f, b, x + 19f, b - 7f, EnvInk.Role("info"));
        }
    }
}
