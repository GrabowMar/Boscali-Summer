using System;
using BoscaliSummer.Modules.Command.Domain;
using NOAvionics;
using TMPro;
using UnityEngine;

namespace BoscaliSummer.Modules.Command.Presentation
{
    /// <summary>A tug-of-war balance bar of the BOARD's FORCE BALANCE: own, middle (contested or neutral) and hostile parts of one track, the words under it. An unknown is the hatched track. The caption (LEAD, LAG, EVEN) shows only where it says something the label and the counts do not.</summary>
    internal sealed class StrBalanceBar : StrPart
    {
        private const float BarH = 8f, Pitch = 8f, SegW = 6f;

        private readonly Func<StrViewModel, StrBalance> pick;
        private readonly bool showCaption;
        private readonly TMP_Text key, caption, text;
        private readonly RectTransform barRect;
        private readonly AvVector bar;
        private float own = float.NaN, middle = float.NaN, hostile = float.NaN, drawnW = -1f;

        internal StrBalanceBar(RectTransform parent, StrControls controls, IStrHub hub, Func<StrViewModel, StrBalance> select, bool captioned) : base(parent, "Balance", controls, hub)
        {
            pick = select;
            showCaption = captioned;
            key = Text(Rect, "Key", AvTextRole.Micro, TextAlignmentOptions.MidlineLeft);
            caption = Text(Rect, "Caption", AvTextRole.Micro, TextAlignmentOptions.MidlineRight);
            text = Text(Rect, "val.balance", AvTextRole.DataSmall, TextAlignmentOptions.TopLeft, true);
            barRect = AvLay.Child(Rect, "Track");
            bar = AvVector.Create(barRect, "TrackInk", 160);
            Restyle();
        }

        protected override void Apply(StrViewModel vm)
        {
            StrBalance b = pick(vm);
            AvText.Set(key, b.Key);
            AvText.Set(caption, showCaption ? b.Caption : "");
            AvText.Set(text, b.Text);
            if (!own.Equals(b.Own) || !middle.Equals(b.Middle) || !hostile.Equals(b.Hostile)) { own = b.Own; middle = b.Middle; hostile = b.Hostile; drawnW = -1f; }
            Paint();
            Changed();
        }

        public override float Measure(float width) => 17f + BarH + 5f + Mathf.Max(15f, AvText.Height(text, width)) + 4f;

        public override void Place(AvSlot s)
        {
            base.Place(s);
            AvLay.Place(key.rectTransform, 0f, 0f, s.W * 0.5f, 15f);
            AvLay.Place(caption.rectTransform, s.W * 0.5f, 0f, s.W * 0.5f, 15f);
            AvLay.Place(barRect, 0f, 17f, s.W, BarH);
            AvLay.Place(text.rectTransform, 0f, 17f + BarH + 5f, s.W, Mathf.Max(15f, AvText.Height(text, s.W)));
            if (!drawnW.Equals(s.W)) { drawnW = s.W; Draw(s.W); }
        }

        private void Draw(float w)
        {
            AvQuadBuffer b = bar.Buffer;
            b.Clear();
            Rgba track = AvInk.Hairline.ToRgba();
            Seg(b, 0f, w, track);
            bool known = !float.IsNaN(own) || !float.IsNaN(hostile);
            if (!known) StrStrokes.HatchRect(b, 0f, 0f, w, BarH, 8f, 1.5f, AvInk.Dim.ToRgba());
            else
            {
                float o = float.IsNaN(own) ? 0f : Mathf.Clamp01(own), m = float.IsNaN(middle) ? 0f : Mathf.Clamp01(middle), h = float.IsNaN(hostile) ? 0f : Mathf.Clamp01(hostile);
                float x1 = o * w, x2 = Mathf.Min(w, x1 + m * w), x3 = Mathf.Min(w, x2 + h * w);
                Rgba oc = AvInk.Friendly.WithAlpha(0.9f).ToRgba(), mc = AvInk.Ink.WithAlpha(0.45f).ToRgba(), hc = AvInk.Hostile.WithAlpha(0.9f).ToRgba();
                Seg(b, 0f, x1, oc); Seg(b, x1, x2, mc); Seg(b, x2, x3, hc);
            }
            bar.Commit();
        }

        // A meter of short lit segments, like the kit's gauges: each span lights the segments (or the part of one) it covers.
        private static void Seg(AvQuadBuffer b, float from, float to, Rgba c)
        {
            for (float x = Mathf.Floor(from / Pitch) * Pitch; x < to; x += Pitch)
            {
                float a = Mathf.Max(x, from), z = Mathf.Min(x + SegW, to);
                if (z > a) b.Add(a, 0f, z, 0f, z, BarH, a, BarH, c, c, c, c);
            }
        }

        private void Paint()
        {
            key.color = AvInk.Key;
            caption.color = AvInk.Dim;
            text.color = AvInk.Ink;
        }

        public override void Restyle()
        {
            Paint();
            drawnW = -1f;
        }
    }

    /// <summary>The ALERT LEVEL row (D2): five DEFCON lamps with the host's level lit, its word, and the rule that set it. Level 0 lights nothing and says NOT MEASURED.</summary>
    internal sealed class StrAlertRow : StrPart
    {
        private const float Gap = 4f, CellH = 36f;
        private static readonly string[] Words = { "NORMAL", "ELEVATED", "HIGH", "SEVERE", "MAXIMUM" };

        private readonly AvFrame[] cells = new AvFrame[5];
        private readonly TMP_Text[] numbers = new TMP_Text[5], words = new TMP_Text[5];
        private readonly TMP_Text head, rule;
        private int level;

        internal StrAlertRow(RectTransform parent, StrControls controls, IStrHub hub) : base(parent, "Alert", controls, hub)
        {
            head = Text(Rect, "Head", AvTextRole.DataStrong, TextAlignmentOptions.MidlineLeft);
            rule = Text(Rect, "Rule", AvTextRole.ProseSmall, TextAlignmentOptions.TopLeft, true);
            for (int i = 0; i < 5; i++)
            {
                RectTransform box = AvLay.Child(Rect, "Lamp " + (5 - i));
                cells[i] = AvFrame.Add(box, "Frame", default(AvChamfer));
                AvLay.Fill(cells[i].rectTransform);
                numbers[i] = Text(box, "Number", AvTextRole.DataStrong, TextAlignmentOptions.Center);
                words[i] = Text(box, "Word", AvTextRole.Micro, TextAlignmentOptions.Center);
                words[i].text = Words[i];
            }
            Restyle();
        }

        protected override void Apply(StrViewModel vm)
        {
            level = vm.AlertLevel;
            AvText.Set(head, level >= 1 && level <= 5 ? "DEFCON " + level + StrWords.Dot + vm.AlertWord : vm.AlertWord);
            AvText.Set(rule, vm.AlertRule);
            for (int i = 0; i < 5; i++) AvText.Set(numbers[i], (5 - i).ToString());
            Paint();
            Changed();
        }

        public override float Measure(float width) => 20f + 4f + CellH + (rule.text.Length > 0 ? 4f + Mathf.Max(15f, AvText.Height(rule, width)) : 0f) + 4f;

        public override void Place(AvSlot s)
        {
            base.Place(s);
            AvLay.Place(head.rectTransform, 0f, 0f, s.W, 20f);
            float w = AvFlowMath.ColumnWidth(s.W, 5, Gap);
            for (int i = 0; i < 5; i++)
            {
                RectTransform box = (RectTransform)cells[i].transform.parent;
                AvLay.Place(box, i * (w + Gap), 24f, w, CellH);
                AvLay.Place(numbers[i].rectTransform, 0f, 1f, w, 18f);
                AvLay.Place(words[i].rectTransform, 0f, 19f, w, 15f);
            }
            if (rule.text.Length > 0) AvLay.Place(rule.rectTransform, 0f, 24f + CellH + 4f, s.W, Mathf.Max(15f, AvText.Height(rule, s.W)));
        }

        private static AvState StateOf(int lvl) => lvl == 5 ? AvState.Ready : lvl == 4 ? AvState.Info : lvl == 3 ? AvState.Caution : AvState.Danger;

        private void Paint()
        {
            head.color = level >= 1 && level <= 5 ? AvInk.State(StateOf(level)) : AvInk.Dim;
            rule.color = AvInk.Dim;
            for (int i = 0; i < 5; i++)
            {
                int lamp = 5 - i;
                bool lit = lamp == level;
                Color c = AvInk.State(StateOf(lamp));
                cells[i].Paint(lit ? c.WithAlpha(0.22f) : AvInk.Inert, lit ? c : AvInk.Hairline);
                numbers[i].color = lit ? c : AvInk.Dim;
                words[i].color = lit ? AvInk.Ink : AvInk.Dim;
            }
        }

        public override void Restyle() => Paint();
    }
}
