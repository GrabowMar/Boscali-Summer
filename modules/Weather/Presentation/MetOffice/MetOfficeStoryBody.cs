using BoscaliSummer.Modules.Weather.Domain;
using NOAvionics;
using TMPro;
using UnityEngine;

namespace BoscaliSummer.Modules.Weather.Presentation
{
    /// <summary>
    /// The inside of the STORY card (R3, R5): nine step pips, the tape strip and its meta line. Today the strip draws the legacy sky step (this class, then the next one at the step)
    /// and the pips stay empty; with the tape (4B) it draws the story's segments and markers from the same model fields.
    /// </summary>
    internal sealed class MetStoryBody : EnvPart
    {
        private const int Pips = MetOfficeModel.SegmentCount;
        private const float StripTop = MetOfficeLayout.PipsRow + 2f, BlockY = StripTop + 15f, BlockH = 20f, AxisY = BlockY + BlockH + 5f, RangeMin = 93f;
        private readonly TMP_Text[] blockLabel = new TMP_Text[MetOfficeModel.MaxBlocks], markLabel = new TMP_Text[MetOfficeModel.MaxMarks], axisLabel = new TMP_Text[7];
        private readonly TMP_Text nowLabel, metaLeft, metaRight;
        private readonly TMP_Text[] pipLabel = new TMP_Text[Pips];
        private MetOfficeModel model;

        /// <summary>How many pips the last paint drew: the check requires all nine, empty or not.</summary>
        public int PipsPainted { get; private set; }
        private static readonly int[] AxisAt = { -93, -62, -31, 0, 31, 62, 93 };

        public MetStoryBody(RectTransform parent) : base(parent, "Story strip", null)
        {
            MakeArt(Paint);
            for (int i = 0; i < blockLabel.Length; i++) blockLabel[i] = Txt("Block" + i, AvTextRole.DataSmall, TextAlignmentOptions.Center, "ink", false, true);
            for (int i = 0; i < markLabel.Length; i++) markLabel[i] = Txt("Mark" + i, AvTextRole.DataSmall, TextAlignmentOptions.MidlineLeft, "caution", false, true);
            for (int i = 0; i < axisLabel.Length; i++)
            {
                axisLabel[i] = Txt("Axis" + i, AvTextRole.Micro, TextAlignmentOptions.Center, AxisAt[i] == 0 ? "key" : "ink-muted");
                axisLabel[i].text = AxisAt[i] == 0 ? "NOW" : (AxisAt[i] > 0 ? "+" : "−") + System.Math.Abs(AxisAt[i]) + "'";
            }
            for (int i = 0; i < Pips; i++) pipLabel[i] = Txt("Pip" + i, AvTextRole.Micro, TextAlignmentOptions.Center, "ink", false, true);
            nowLabel = Txt("Now", AvTextRole.DataSmall, TextAlignmentOptions.MidlineLeft, "key");
            nowLabel.text = "NOW";
            metaLeft = Txt("MetaLeft", AvTextRole.Micro, TextAlignmentOptions.MidlineLeft, "ink-dim", false, true);
            metaRight = Txt("MetaRight", AvTextRole.Micro, TextAlignmentOptions.MidlineRight, "ink-dim", false, true);
            Restyle();
        }

        public override float Measure(float width) => MetOfficeLayout.PipsRow + 2f + MetOfficeLayout.StripArt + MetOfficeLayout.StripMeta;

        private float X(float minutes, float w) => (Mathf.Clamp(minutes, -RangeMin, RangeMin) + RangeMin) / (2f * RangeMin) * w;

        public void Set(MetOfficeModel m)
        {
            model = m;
            for (int i = 0; i < blockLabel.Length; i++)
                AvText.Set(blockLabel[i], i < m.BlockCount ? m.Blocks[i].Label ?? "" : "");
            for (int i = 0; i < markLabel.Length; i++)
                AvText.Set(markLabel[i], i < m.MarkCount ? m.Marks[i].Label ?? "" : "");
            for (int i = 0; i < Pips; i++)
                AvText.Set(pipLabel[i], m.Segments[i].Present ? m.Segments[i].Label ?? "" : "");
            AvText.Set(metaLeft, m.HasStory ? m.StoryName.ToUpperInvariant() : "LEGACY SKY STEP · 5 MIN");
            AvText.Set(metaRight, m.StripNote ?? "");
            Arrange();
            Redraw();
        }

        protected override void Layout(float w, float h) => Arrange();

        private void Arrange()
        {
            float w = PlacedW;
            if (w <= 0f) return;
            for (int i = 0; i < blockLabel.Length; i++)
            {
                bool on = model != null && i < model.BlockCount && X(model.Blocks[i].ToMin, w) - X(model.Blocks[i].FromMin, w) >= 30f;
                blockLabel[i].gameObject.SetActive(on);
                if (on) AvLay.Place(blockLabel[i], X(model.Blocks[i].FromMin, w), BlockY, X(model.Blocks[i].ToMin, w) - X(model.Blocks[i].FromMin, w), BlockH);
            }
            for (int i = 0; i < markLabel.Length; i++)
            {
                bool on = model != null && i < model.MarkCount;
                markLabel[i].gameObject.SetActive(on);
                if (on) AvLay.Place(markLabel[i], Mathf.Min(X(model.Marks[i].AtMin, w) + 6f, w - 60f), StripTop - 2f, 60f, 16f);
            }
            for (int i = 0; i < axisLabel.Length; i++)
                AvLay.Place(axisLabel[i], X(AxisAt[i], w) - 20f, AxisY + 4f, 40f, 15f);
            float pw = (w - (Pips - 1) * 2f) / Pips;
            for (int i = 0; i < Pips; i++) AvLay.Place(pipLabel[i], i * (pw + 2f), 0f, pw, MetOfficeLayout.PipsRow);
            nowLabel.gameObject.SetActive(false);
            AvLay.Place(metaLeft, 0f, MetOfficeLayout.PipsRow + 2f + MetOfficeLayout.StripArt, w * 0.6f, MetOfficeLayout.StripMeta);
            AvLay.Place(metaRight, w * 0.6f, MetOfficeLayout.PipsRow + 2f + MetOfficeLayout.StripArt, w * 0.4f, MetOfficeLayout.StripMeta);
        }

        private void Paint(EnvCanvas c)
        {
            float w = PlacedW;
            if (w <= 0f) return;
            Color sunken = EnvInk.Role("surface-sunken"), hair = EnvInk.Role("hairline"), key = EnvInk.Role("key");
            float pw = (w - (Pips - 1) * 2f) / Pips;
            for (int i = 0; i < Pips; i++)
            {
                float x = i * (pw + 2f);
                c.Quad(x, 0f, pw, MetOfficeLayout.PipsRow, sunken);
                if (model != null && model.Segments[i].Present)
                {
                    AvState seg = WeatherEnvView.RegimeState(model.Segments[i].Class);
                    c.Quad(x, 0f, pw, MetOfficeLayout.PipsRow, EnvInk.Alpha(EnvInk.State(seg == AvState.Info ? AvState.Ready : seg), 0.45f));
                }
                c.Quad(x, 0f, pw, 1f, hair); c.Quad(x, MetOfficeLayout.PipsRow - 1f, pw, 1f, hair);
                c.Quad(x, 0f, 1f, MetOfficeLayout.PipsRow, hair); c.Quad(x + pw - 1f, 0f, 1f, MetOfficeLayout.PipsRow, hair);
            }
            PipsPainted = Pips;
            c.Quad(0f, BlockY, w, BlockH, EnvInk.Alpha(hair, 0.35f));
            if (model != null)
            {
                for (int i = 0; i < model.BlockCount; i++)
                {
                    MetBlock b = model.Blocks[i];
                    Color tint = EnvInk.State(WeatherEnvView.RegimeState(b.Class) == AvState.Info ? AvState.Ready : WeatherEnvView.RegimeState(b.Class));
                    float x0 = X(b.FromMin, w), x1 = X(b.ToMin, w);
                    c.Quad(x0, BlockY, Mathf.Max(0f, x1 - x0), BlockH, EnvInk.Alpha(tint, 0.45f));
                    c.Quad(x1 - 1f, BlockY, 1f, BlockH, EnvInk.Role("ground"));
                }
                for (int i = 0; i < model.MarkCount; i++)
                {
                    MetMark mk = model.Marks[i];
                    float x = X(mk.AtMin, w);
                    Color col = mk.Step ? EnvInk.Role("caution") : MetFrontGlyph.Ink(mk.Kind);
                    c.Quad(x - 0.65f, StripTop + 1f, 1.3f, BlockH + 14f, col);
                    c.Tri(x - 4f, StripTop + 1f, x + 4f, StripTop + 1f, x, StripTop + 7f, col);
                }
            }
            float nx = X(0f, w);
            c.Quad(nx - 0.8f, StripTop + 1f, 1.6f, BlockH + 18f, key);
            c.Tri(nx - 4f, StripTop - 3f, nx + 4f, StripTop - 3f, nx, StripTop + 3f, key);
            c.Quad(0f, AxisY, w, 1f, hair);
            for (int i = 0; i < AxisAt.Length; i++) c.Quad(X(AxisAt[i], w) - 0.4f, AxisY, 0.8f, 4f, EnvInk.Role("ink-muted"));
        }
    }
}
