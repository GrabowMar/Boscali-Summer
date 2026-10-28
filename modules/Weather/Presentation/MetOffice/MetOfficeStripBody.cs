using BoscaliSummer.Modules.Weather.Domain;
using NOAvionics;
using TMPro;
using UnityEngine;

namespace BoscaliSummer.Modules.Weather.Presentation
{
    /// <summary>
    /// The inside of the ALONG THE WIND card: the weather pattern's segments and front ticks from 150 km downwind (left, already passed) to 150 km upwind (right, coming),
    /// the camera in the middle, a km axis and the pattern's name. Wide segments are labelled with their role and class, narrow ones with the class alone.
    /// </summary>
    internal sealed class MetStripBody : EnvPart
    {
        private const float Range = MetOfficeTapeFill.ReachKm, LabelTop = MetOfficeLayout.StripTop, BlockY = LabelTop + 15f, BlockH = 20f, AxisY = BlockY + BlockH + 5f, NamedBlock = 120f;
        private static readonly int[] AxisAt = { -150, -100, -50, 0, 50, 100, 150 };
        private readonly TMP_Text[] blockLabel = new TMP_Text[MetOfficeModel.MaxBlocks], markLabel = new TMP_Text[MetOfficeModel.MaxMarks], axisLabel = new TMP_Text[AxisAt.Length];
        private readonly TMP_Text meta;
        private MetOfficeModel model;

        public MetStripBody(RectTransform parent) : base(parent, "Wind strip", null)
        {
            MakeArt(Paint);
            for (int i = 0; i < blockLabel.Length; i++) blockLabel[i] = Txt("Block" + i, AvTextRole.DataSmall, TextAlignmentOptions.Center, "ink", false, true);
            for (int i = 0; i < markLabel.Length; i++) markLabel[i] = Txt("Mark" + i, AvTextRole.DataSmall, TextAlignmentOptions.MidlineLeft, "caution", false, true);
            for (int i = 0; i < axisLabel.Length; i++)
            {
                int km = AxisAt[i];
                axisLabel[i] = Txt("Axis" + i, AvTextRole.Micro, TextAlignmentOptions.Center, km == 0 ? "key" : "ink-muted");
                // The end labels hang inward (left- and right-aligned): centred on the strip edges they would stand half out of the card.
                if (i == 0) axisLabel[i].alignment = TextAlignmentOptions.MidlineLeft; else if (i == AxisAt.Length - 1) axisLabel[i].alignment = TextAlignmentOptions.MidlineRight;
                axisLabel[i].text = km == 0 ? "YOU" : i == 0 ? "150 KM DOWNWIND" : i == AxisAt.Length - 1 ? "UPWIND 150 KM" : System.Math.Abs(km) + " KM";
            }
            meta = Txt("Meta", AvTextRole.Micro, TextAlignmentOptions.MidlineLeft, "ink-dim", false, true);
            Restyle();
        }

        public override float Measure(float width) => MetOfficeLayout.StripBody;

        private static float X(float km, float w) => (Mathf.Clamp(km, -Range, Range) + Range) / (2f * Range) * w;

        public void Set(MetOfficeModel m)
        {
            model = m;
            for (int i = 0; i < markLabel.Length; i++) AvText.Set(markLabel[i], i < m.MarkCount ? m.Marks[i].Label ?? "" : "");
            AvText.Set(meta, !m.Captured ? "" : m.PinnedSky >= 0 ? "SKY SET BY THE HOST · ONE SKY EVERYWHERE, NO FRONTS" : "PATTERN · " + m.StoryName.ToUpperInvariant());
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
                MetBlock b = model != null && i < model.BlockCount ? model.Blocks[i] : default;
                float x0 = X(b.FromKm, w), bw = X(b.ToKm, w) - x0;
                bool on = model != null && i < model.BlockCount && bw >= 30f;
                blockLabel[i].gameObject.SetActive(on);
                if (!on) continue;
                AvText.Set(blockLabel[i], bw >= NamedBlock && !string.IsNullOrEmpty(b.Name) ? b.Name + " · " + b.Code : b.Code ?? "");
                AvLay.Place(blockLabel[i], x0, BlockY, bw, BlockH);
            }
            for (int i = 0; i < markLabel.Length; i++)
            {
                bool on = model != null && i < model.MarkCount;
                markLabel[i].gameObject.SetActive(on);
                if (on) AvLay.Place(markLabel[i], Mathf.Min(X(model.Marks[i].AtKm, w) + 6f, w - 60f), LabelTop - 2f, 60f, 16f);
            }
            for (int i = 0; i < axisLabel.Length; i++)
            {
                float lw = i == 0 || i == AxisAt.Length - 1 ? 110f : 50f;
                AvLay.Place(axisLabel[i], i == 0 ? 0f : i == AxisAt.Length - 1 ? w - lw : X(AxisAt[i], w) - lw * 0.5f, AxisY + 4f, lw, 15f);
            }
            AvLay.Place(meta, 0f, MetOfficeLayout.StripTop + MetOfficeLayout.StripArt, w, MetOfficeLayout.StripMeta);
        }

        private void Paint(EnvCanvas c)
        {
            float w = PlacedW;
            if (w <= 0f) return;
            Color hair = EnvInk.Role("hairline"), key = EnvInk.Role("key");
            c.Quad(0f, BlockY, w, BlockH, EnvInk.Alpha(hair, 0.35f));
            if (model != null)
            {
                for (int i = 0; i < model.BlockCount; i++)
                {
                    MetBlock b = model.Blocks[i];
                    float x0 = X(b.FromKm, w), x1 = X(b.ToKm, w);
                    c.Quad(x0, BlockY, Mathf.Max(0f, x1 - x0), BlockH, EnvInk.Alpha(MetSkyFacts.Tint(b.Class), 0.45f));
                    c.Quad(x1 - 1f, BlockY, 1f, BlockH, EnvInk.Role("ground"));
                }
                for (int i = 0; i < model.MarkCount; i++)
                {
                    MetMark mk = model.Marks[i];
                    float x = X(mk.AtKm, w);
                    Color col = MetFrontGlyph.Ink(mk.Kind);
                    c.Quad(x - 0.65f, LabelTop + 1f, 1.3f, BlockH + 14f, col);
                    c.Tri(x - 4f, LabelTop + 1f, x + 4f, LabelTop + 1f, x, LabelTop + 7f, col);
                }
            }
            float nx = X(0f, w);
            c.Quad(nx - 0.8f, LabelTop + 1f, 1.6f, BlockH + 18f, key);
            c.Tri(nx - 4f, LabelTop - 3f, nx + 4f, LabelTop - 3f, nx, LabelTop + 3f, key);
            c.Quad(0f, AxisY, w, 1f, hair);
            for (int i = 0; i < AxisAt.Length; i++) c.Quad(X(AxisAt[i], w) - 0.4f, AxisY, 0.8f, 4f, EnvInk.Role("ink-muted"));
        }
    }
}
