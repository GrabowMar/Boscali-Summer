using BoscaliSummer.Modules.Weather.Domain;
using NOAvionics;
using TMPro;
using UnityEngine;

namespace BoscaliSummer.Modules.Weather.Presentation
{
    /// <summary>The fronts under the wind line, nearest first, each with its symbol, kind, distance and bearing.</summary>
    internal sealed class MetFrontsBody : EnvPart
    {
        private const int Rows = MetOfficeModel.MaxFronts;
        private const float RowH = 20f, WordW = 150f;
        private readonly TMP_Text[] word = new TMP_Text[Rows], where = new TMP_Text[Rows];
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
            }
            Restyle();
        }

        public override float Measure(float width) => MetOfficeLayout.FrontsBody;

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
                }
                else
                {
                    AvText.Set(word[r], r == 0 && m.Captured ? "NO FRONT NEAR" : "");
                    AvText.Set(where[r], "");
                }
            }
            Redraw();
        }

        protected override void Layout(float w, float h)
        {
            for (int r = 0; r < Rows; r++)
            {
                float y = r * RowH;
                AvLay.Place(word[r], 30f, y, WordW, RowH - 1f);
                AvLay.Place(where[r], 30f + WordW + 4f, y, Mathf.Max(1f, w - WordW - 34f), RowH - 1f);
            }
        }

        private void Paint(EnvCanvas c)
        {
            for (int r = 0; r < Rows; r++)
                if (used[r]) MetFrontGlyph.Draw(c, kind[r], 0f, r * RowH - 1f);
        }
    }

    /// <summary>The front symbols in a 28 x 16 box: warm half-discs, cold triangles, an occluded alternation, a dash-dot squall line. Shared by the fronts card and the strip's ticks.</summary>
    internal static class MetFrontGlyph
    {
        public static Color Ink(MetFrontKind kind) => EnvInk.Role(kind switch
        {
            MetFrontKind.Warm => "caution",
            MetFrontKind.Squall => "danger",
            MetFrontKind.Occluded => "ink-dim",
            _ => "info",
        });

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
