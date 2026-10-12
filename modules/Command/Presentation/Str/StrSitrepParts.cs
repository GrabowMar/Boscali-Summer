using System;
using BoscaliSummer.Modules.Command.Domain;
using NOAvionics;
using TMPro;
using UnityEngine;

namespace BoscaliSummer.Modules.Command.Presentation
{
    /// <summary>A tug-of-war balance bar of the SITREP tab: own, middle (contested or neutral) and hostile parts of one track, the words under it. An unknown is the hatched track.</summary>
    internal sealed class StrBalanceBar : StrPart
    {
        private const float BarH = 12f;

        private readonly Func<StrViewModel, StrBalance> pick;
        private readonly TMP_Text key, caption, text;
        private readonly RectTransform barRect;
        private readonly AvVector bar;
        private float own = float.NaN, middle = float.NaN, hostile = float.NaN, drawnW = -1f;

        internal StrBalanceBar(RectTransform parent, StrControls controls, IStrHub hub, Func<StrViewModel, StrBalance> select) : base(parent, "Balance", controls, hub)
        {
            pick = select;
            key = Text(Rect, "Key", AvTextRole.Micro, TextAlignmentOptions.MidlineLeft);
            caption = Text(Rect, "Caption", AvTextRole.Micro, TextAlignmentOptions.MidlineRight);
            text = Text(Rect, "val.balance", AvTextRole.DataSmall, TextAlignmentOptions.TopLeft, true);
            barRect = AvLay.Child(Rect, "Track");
            bar = AvVector.Create(barRect, "TrackInk", 40);
            Restyle();
        }

        protected override void Apply(StrViewModel vm)
        {
            StrBalance b = pick(vm);
            AvText.Set(key, b.Key);
            AvText.Set(caption, b.Caption);
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
            b.Add(0f, 0f, w, 0f, w, BarH, 0f, BarH, track, track, track, track);
            bool known = !float.IsNaN(own) || !float.IsNaN(hostile);
            if (!known) StrStrokes.HatchRect(b, 0f, 0f, w, BarH, 8f, 1.5f, AvInk.Dim.ToRgba());
            else
            {
                float o = float.IsNaN(own) ? 0f : Mathf.Clamp01(own), m = float.IsNaN(middle) ? 0f : Mathf.Clamp01(middle), h = float.IsNaN(hostile) ? 0f : Mathf.Clamp01(hostile);
                float x1 = o * w, x2 = Mathf.Min(w, x1 + m * w), x3 = Mathf.Min(w, x2 + h * w);
                Rgba oc = AvInk.Friendly.WithAlpha(0.9f).ToRgba(), mc = AvInk.Ink.WithAlpha(0.45f).ToRgba(), hc = AvInk.Hostile.WithAlpha(0.9f).ToRgba();
                if (x1 > 0f) b.Add(0f, 0f, x1, 0f, x1, BarH, 0f, BarH, oc, oc, oc, oc);
                if (x2 > x1) b.Add(x1, 0f, x2, 0f, x2, BarH, x1, BarH, mc, mc, mc, mc);
                if (x3 > x2) b.Add(x2, 0f, x3, 0f, x3, BarH, x2, BarH, hc, hc, hc, hc);
            }
            bar.Commit();
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

    /// <summary>The AI sortie board (D2): CAP, SEAD, CAS, STRIKE and TRANSIT of the friendly AI, each with its share bar and count. An unknown count is a dash and no bar.</summary>
    internal sealed class StrSortieBoard : StrPart
    {
        private const float RowH = 22f, CodeW = 62f, CountW = 44f;

        private readonly TMP_Text[] codes = new TMP_Text[5], counts = new TMP_Text[5];
        private readonly RectTransform[] barRects = new RectTransform[5];
        private readonly AvVector[] bars = new AvVector[5];
        private readonly float[] shares = { float.NaN, float.NaN, float.NaN, float.NaN, float.NaN };
        private readonly bool[] known = new bool[5];
        private float drawnW = -1f;

        internal StrSortieBoard(RectTransform parent, StrControls controls, IStrHub hub) : base(parent, "SortieBoard", controls, hub)
        {
            for (int i = 0; i < 5; i++)
            {
                codes[i] = Text(Rect, "Code " + i, AvTextRole.DataStrong, TextAlignmentOptions.MidlineLeft);
                counts[i] = Text(Rect, "val.count " + i, AvTextRole.DataStrong, TextAlignmentOptions.MidlineRight);
                barRects[i] = AvLay.Child(Rect, "Bar " + i);
                bars[i] = AvVector.Create(barRects[i], "BarInk", 12);
            }
            Restyle();
        }

        protected override void Apply(StrViewModel vm)
        {
            for (int i = 0; i < 5; i++)
            {
                AvText.Set(codes[i], vm.SortieCodes[i]);
                AvText.Set(counts[i], vm.SortieCounts[i] < 0 ? StrWords.Dash : vm.SortieCounts[i].ToString());
                if (!shares[i].Equals(vm.SortieShares[i]) || known[i] != vm.SortieCounts[i] >= 0) { shares[i] = vm.SortieShares[i]; known[i] = vm.SortieCounts[i] >= 0; drawnW = -1f; }
            }
            Paint();
            Changed();
        }

        public override float Measure(float width) => 5f * RowH + 2f;

        public override void Place(AvSlot s)
        {
            base.Place(s);
            float bw = s.W - CodeW - CountW - 12f;
            for (int i = 0; i < 5; i++)
            {
                float y = i * RowH;
                AvLay.Place(codes[i].rectTransform, 0f, y, CodeW, RowH);
                AvLay.Place(barRects[i], CodeW + 6f, y + 5f, bw, 12f);
                AvLay.Place(counts[i].rectTransform, s.W - CountW, y, CountW, RowH);
            }
            if (!drawnW.Equals(s.W)) { drawnW = s.W; Draw(bw); }
        }

        private void Draw(float w)
        {
            for (int i = 0; i < 5; i++)
            {
                AvQuadBuffer b = bars[i].Buffer;
                b.Clear();
                Rgba track = AvInk.Hairline.ToRgba();
                b.Add(0f, 0f, w, 0f, w, 12f, 0f, 12f, track, track, track, track);
                if (!known[i] || float.IsNaN(shares[i])) StrStrokes.HatchRect(b, 0f, 0f, w, 12f, 8f, 1.5f, AvInk.Dim.ToRgba());
                else if (shares[i] > 0f)
                {
                    Rgba c = AvInk.Key.ToRgba();
                    float x = Mathf.Clamp01(shares[i]) * w;
                    b.Add(0f, 0f, x, 0f, x, 12f, 0f, 12f, c, c, c, c);
                }
                bars[i].Commit();
            }
        }

        private void Paint()
        {
            for (int i = 0; i < 5; i++) { codes[i].color = AvInk.Ink; counts[i].color = AvInk.Ink; }
        }

        public override void Restyle()
        {
            Paint();
            drawnW = -1f;
        }
    }
}
