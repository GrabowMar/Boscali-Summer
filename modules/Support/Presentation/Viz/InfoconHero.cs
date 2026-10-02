using NOAvionics;
using TMPro;
using UnityEngine;

namespace BoscaliSummer.Modules.Support.Presentation.Viz
{
    /// <summary>
    /// CYBER's headline card: INFOCON as a five-step rail (5 on the left is quiet, 1 on the right is
    /// maximum readiness; every step from 5 down to the current one is lit and the current one is
    /// solid), the level and its word above, the campaign phase below and the network's advice as one
    /// wrapped sentence. Height follows the advice text.
    /// </summary>
    internal sealed class InfoconHero : AvPart
    {
        private const float PadX = 12f, TopY = 6f, RailY = 36f, RailH = 22f, PhaseY = 64f, Gap = 4f;
        private readonly AvFrame frame;
        private readonly OpsCanvas art;
        private readonly TMP_Text head, word, phase, advice;
        private readonly TMP_Text[] numbers = new TMP_Text[5];
        private AvState tone = AvState.Inert;
        private int level;
        private float width;
        private bool dirty = true;

        public InfoconHero(RectTransform parent)
        {
            Rect = AvLay.Child(parent, "InfoconHero");
            frame = AvFrame.Add(Rect, "Frame", AvChamfer.Diagonal(8f));
            AvLay.Fill(frame.rectTransform);
            frame.Bracket = 8f;
            art = OpsCanvas.Add(Rect, "Art");
            head = OpsText.Line(Rect, "Head", AvTextRole.Title, TextAlignmentOptions.MidlineLeft);
            word = OpsText.Line(Rect, "Word", AvTextRole.Label, TextAlignmentOptions.MidlineRight);
            phase = OpsText.Line(Rect, "Phase", AvTextRole.Label, TextAlignmentOptions.MidlineLeft);
            advice = OpsText.Block(Rect, "Advice", AvTextRole.Prose);
            for (int i = 0; i < 5; i++)
                numbers[i] = OpsText.Line(Rect, "Step" + (5 - i), AvTextRole.DataStrong, TextAlignmentOptions.Center);
            for (int i = 0; i < 5; i++) numbers[i].text = (5 - i).ToString(System.Globalization.CultureInfo.InvariantCulture);
            Restyle();
        }

        /// <summary><paramref name="infocon"/> 1..5, or 0 for no network (the rail stays dark).</summary>
        public void Set(int infocon, string headline, string wordText, string phaseText, string adviceText, AvState state)
        {
            bool grew = OpsText.Set(advice, adviceText);
            OpsText.Set(head, AvStates.Glyph(state) + headline);
            OpsText.Set(word, wordText);
            OpsText.Set(phase, phaseText);
            int lv = Mathf.Clamp(infocon, 0, 5);
            bool restyle = lv != level || state != tone;
            if ((lv > 0) != (level > 0))
            {
                for (int i = 0; i < 5; i++) numbers[i].gameObject.SetActive(lv > 0);
                grew = true;
            }
            level = lv;
            tone = state;
            if (restyle) { dirty = true; Restyle(); }
            if (grew) { Changed(); if (width > 0f) LayoutChildren(width); }
        }

        /// <summary>The phase line sits under the rail; with no network the rail is left out (one compact card).</summary>
        private float PhaseTop => level > 0 ? PhaseY : RailY - 4f;

        private float AdviceH(float w) => advice.text.Length == 0 ? 0f : AvText.Height(advice, w - 2f * PadX);

        public override float Measure(float w)
        {
            float a = AdviceH(w);
            return PhaseTop + 18f + (a > 0f ? 4f + a : 0f) + 8f;
        }

        public override void Place(AvSlot s)
        {
            base.Place(s);
            width = s.W;
            LayoutChildren(s.W);
            dirty = true;
            Paint();
        }

        /// <summary>Child boxes only (never this part's own rect, which the flow owns).</summary>
        private void LayoutChildren(float w)
        {
            AvLay.Place((RectTransform)art.transform, 0f, 0f, w, Measure(w));
            OpsText.Place(head, PadX, TopY, w - 2f * PadX - 150f, 28f);
            OpsText.Place(word, w - PadX - 146f, TopY + 4f, 146f, 20f);
            OpsText.Place(phase, PadX, PhaseTop, w - 2f * PadX, 18f);
            OpsText.Place(advice, PadX, PhaseTop + 18f + 4f, w - 2f * PadX, AdviceH(w));
            float cw = (w - 2f * PadX - 4f * Gap) / 5f;
            for (int i = 0; i < 5; i++) OpsText.Place(numbers[i], PadX + i * (cw + Gap), RailY, cw, RailH - 2f);
        }

        public override void Restyle()
        {
            frame.Paint(OpsInk.Sunken, OpsInk.Hairline);
            frame.BracketColor = OpsInk.Frame;
            frame.SetVerticesDirty();
            head.color = OpsInk.Word(tone);
            word.color = OpsInk.Word(tone);
            phase.color = OpsInk.Dim;
            advice.color = OpsInk.Ink;
            for (int i = 0; i < 5; i++)
            {
                int lv = 5 - i;
                numbers[i].color = level > 0 && lv == level ? OpsInk.Ink : level > 0 && lv > level ? OpsInk.Dim : OpsInk.Muted;
            }
            dirty = true;
            Paint();
        }

        private void Paint()
        {
            if (!dirty || width <= 0f) return;
            dirty = false;
            float cw = (width - 2f * PadX - 4f * Gap) / 5f;
            Color hue = OpsInk.Rail(tone == AvState.Inert ? AvState.Info : tone);
            art.Begin();
            for (int i = 0; i < 5 && level > 0; i++)
            {
                int lv = 5 - i;
                float x = PadX + i * (cw + Gap);
                bool lit = level > 0 && lv >= level, current = level > 0 && lv == level;
                art.Chamfer(x, RailY, cw, RailH, 6f, current ? OpsInk.A(hue, 0.42f) : lit ? OpsInk.A(hue, 0.18f) : OpsInk.Inert);
                art.Box(x, RailY + RailH - (current ? 3f : 2f), cw, current ? 3f : 2f,
                    current ? hue : lit ? OpsInk.A(hue, 0.6f) : OpsInk.A(OpsInk.Hairline, 0.8f));
            }
            art.End();
        }
    }
}
