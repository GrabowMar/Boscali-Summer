using NOAvionics;
using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Radio.Presentation
{
    /// <summary>One control of a <see cref="RadioStripPart"/>: its spec, its share of the width and whether a wider gap precedes it.</summary>
    internal readonly struct RadioStripItem
    {
        public readonly AvControl.Spec Spec;
        public readonly float Weight;
        public readonly bool NewGroup;

        public RadioStripItem(string label, Action click, AvButtonStyle style, AvIcon icon, float weight = 1f, bool newGroup = false)
        {
            Spec = new AvControl.Spec(label, click, style, icon);
            Weight = weight;
            NewGroup = newGroup;
        }
    }

    /// <summary>
    /// One line of mixed-width buttons, grouped by wider gaps: the receiver's band keys, dial keys and
    /// transport, or the deck's transport, modes and stop. Icon-only keys are narrow, worded keys wide;
    /// the line splits the width by weight, so nothing wraps to a second row.
    /// </summary>
    internal sealed class RadioStripPart : AvPart
    {
        private const float InGap = 3f, GroupGap = 9f;
        private readonly AvControl[] controls;
        private readonly float[] weights;
        private readonly bool[] groups;

        public AvControl this[int index] => controls[index];

        public RadioStripPart(RectTransform parent, params RadioStripItem[] items)
        {
            Rect = AvLay.Child(parent, "Strip");
            controls = new AvControl[items.Length];
            weights = new float[items.Length];
            groups = new bool[items.Length];
            for (int i = 0; i < items.Length; i++)
            {
                controls[i] = AvControl.Make(Rect, items[i].Spec);
                weights[i] = Mathf.Max(0.1f, items[i].Weight);
                groups[i] = items[i].NewGroup;
            }
        }

        public override float Measure(float width) => AvGridTokens.Row + 2f;

        public override void Place(AvSlot s)
        {
            base.Place(s);
            float gaps = 0f, total = 0f;
            for (int i = 0; i < controls.Length; i++)
            {
                total += weights[i];
                if (i > 0) gaps += groups[i] ? GroupGap : InGap;
            }
            float unit = Mathf.Max(0f, s.W - gaps) / Mathf.Max(0.1f, total), x = 0f;
            for (int i = 0; i < controls.Length; i++)
            {
                if (i > 0) x += groups[i] ? GroupGap : InGap;
                float w = unit * weights[i];
                AvLay.Place(controls[i].Rect, x, 0f, w, s.H);
                x += w;
            }
        }

        public override void Restyle()
        {
            foreach (AvControl c in controls) c.Restyle();
        }
    }

    /// <summary>A one-pixel box around a rect, built from four solids (the kit frame is chamfered and themed, this one takes the station colour).</summary>
    internal sealed class RadioOutline
    {
        private readonly Image top, bottom, left, right;

        public RadioOutline(RectTransform parent, string name)
        {
            top = AvLay.Solid(parent, name + "T", Color.clear);
            bottom = AvLay.Solid(parent, name + "B", Color.clear);
            left = AvLay.Solid(parent, name + "L", Color.clear);
            right = AvLay.Solid(parent, name + "R", Color.clear);
        }

        public Color Color { set { top.color = bottom.color = left.color = right.color = value; } }

        public void Place(float x, float y, float w, float h)
        {
            AvLay.Place(top.rectTransform, x, y, w, 1f);
            AvLay.Place(bottom.rectTransform, x, y + h - 1f, w, 1f);
            AvLay.Place(left.rectTransform, x, y, 1f, h);
            AvLay.Place(right.rectTransform, x + w - 1f, y, 1f, h);
        }
    }

    /// <summary>
    /// BROADCAST DESK head card: the station's crest and dial, its name and state slab, the show and its host,
    /// what is on air now (a song with its clock, or the station's own talk) and the break that comes next.
    /// The card takes the station's colour, so each faction's station reads as its own.
    /// </summary>
    internal sealed class RadioOnAirPart : AvPart
    {
        private const float H = 132f, Crest = 86f, Pad = 10f, ClockW = 92f;
        private readonly Image back, crestBack, crestIcon, rule, barBack, barFill, slabBack, hit;
        private readonly RadioOutline outline, crestOutline;
        private readonly TMP_Text crestCode, station, slab, show, label, title, clock, next, nextIn;
        private Color tint = new Color(0.45f, 0.95f, 0.55f);
        private float placedW = -1f, progress;
        private bool slabLive;

        public RadioOnAirPart(RectTransform parent)
        {
            Rect = AvLay.Child(parent, "OnAir");
            back = AvLay.Solid(Rect, "Back", Color.clear);
            hit = AvLay.Solid(Rect, "Hit", Color.clear);
            hit.raycastTarget = true;
            outline = new RadioOutline(Rect, "Edge");
            crestBack = AvLay.Solid(Rect, "CrestBack", Color.clear);
            crestOutline = new RadioOutline(Rect, "CrestEdge");
            crestIcon = AvLay.Solid(Rect, "CrestIcon", Color.white);
            crestIcon.preserveAspect = true;
            crestIcon.enabled = false;
            crestCode = AvText.Make(Rect, "CrestCode", AvTextRole.Micro, string.Empty, TextAlignmentOptions.Center);
            station = AvText.Make(Rect, "Station", AvTextRole.Title);
            AvText.Fit(station, false);
            slabBack = AvLay.Solid(Rect, "SlabBack", Color.clear);
            slab = AvText.Make(Rect, "Slab", AvTextRole.Micro, string.Empty, TextAlignmentOptions.Center);
            show = AvText.Make(Rect, "Show", AvTextRole.Micro);
            AvText.Fit(show, false);
            rule = AvLay.Solid(Rect, "Rule", Color.clear);
            label = AvText.Make(Rect, "Label", AvTextRole.Micro);
            title = AvText.Make(Rect, "Title", AvTextRole.Label);
            AvText.Fit(title, false);
            barBack = AvLay.Solid(Rect, "BarBack", Color.clear);
            barFill = AvLay.Solid(Rect, "BarFill", Color.clear);
            clock = AvText.Make(Rect, "Clock", AvTextRole.DataSmall, string.Empty, TextAlignmentOptions.MidlineRight);
            next = AvText.Make(Rect, "Next", AvTextRole.Micro);
            AvText.Fit(next, false);
            nextIn = AvText.Make(Rect, "NextIn", AvTextRole.DataSmall, string.Empty, TextAlignmentOptions.MidlineRight);
            Restyle();
        }

        public string Help { set => AvHelpTip.Attach(hit.gameObject, value); }

        public override float Measure(float width) => H;

        public void Set(Color stationColor, Sprite crest, string code, string stationName, string stateWord, bool live,
            string showLine, string labelText, string titleText, float progress01, string clockText, string nextText, string nextInText)
        {
            if (crestIcon.sprite != crest) { crestIcon.sprite = crest; crestIcon.enabled = crest != null; }
            Put(crestCode, code); Put(station, stationName); Put(slab, stateWord); Put(show, showLine);
            Put(label, labelText); Put(title, titleText); Put(clock, clockText); Put(next, nextText); Put(nextIn, nextInText);
            progress = Mathf.Clamp01(progress01);
            if (tint != stationColor || slabLive != live) { tint = stationColor; slabLive = live; Restyle(); }
            if (placedW > 0f) Arrange(placedW);
        }

        private static void Put(TMP_Text t, string s)
        {
            if (t.text != (s ?? string.Empty)) t.text = s ?? string.Empty;
        }

        public override void Place(AvSlot s)
        {
            base.Place(s);
            placedW = s.W;
            AvLay.Place(back.rectTransform, 0f, 0f, s.W, H);
            AvLay.Place(hit.rectTransform, 0f, 0f, s.W, H);
            outline.Place(0f, 0f, s.W, H);
            Arrange(s.W);
        }

        private void Arrange(float w)
        {
            AvLay.Place(crestBack.rectTransform, Pad, Pad, Crest, Crest);
            crestOutline.Place(Pad, Pad, Crest, Crest);
            AvLay.Place(crestIcon.rectTransform, Pad + 17f, Pad + 8f, Crest - 34f, Crest - 34f);
            AvLay.Place(crestCode.rectTransform, Pad, Pad + Crest - 22f, Crest, 18f);

            float x = Pad * 2f + Crest, rw = Mathf.Max(40f, w - x - Pad);
            float slabW = Mathf.Min(rw * 0.35f, AvText.Width(slab) + 14f);
            AvLay.Place(station.rectTransform, x, Pad - 2f, Mathf.Max(20f, rw - slabW - 6f), 22f);
            AvLay.Place(slabBack.rectTransform, x + rw - slabW, Pad, slabW, 17f);
            AvLay.Place(slab.rectTransform, x + rw - slabW, Pad, slabW, 17f);
            AvLay.Place(show.rectTransform, x, Pad + 21f, rw, 16f);
            AvLay.Place(rule.rectTransform, x, Pad + 41f, rw, 1f);
            AvLay.Place(label.rectTransform, x, Pad + 45f, rw, 14f);
            AvLay.Place(title.rectTransform, x, Pad + 59f, rw, 18f);
            float barW = Mathf.Max(10f, rw - ClockW - 6f);
            AvLay.Place(barBack.rectTransform, x, Pad + 82f, barW, 4f);
            AvLay.Place(barFill.rectTransform, x, Pad + 82f, barW * progress, 4f);
            AvLay.Place(clock.rectTransform, x + rw - ClockW, Pad + 76f, ClockW, 16f);

            AvLay.Place(next.rectTransform, Pad, H - 26f, Mathf.Max(20f, w - 2f * Pad - 70f), 18f);
            AvLay.Place(nextIn.rectTransform, w - Pad - 70f, H - 26f, 70f, 18f);
        }

        public override void Restyle()
        {
            Color dark = AvStyleHost.FuiFill("panel", new Color(0.06f, 0.08f, 0.1f));
            back.color = Color.Lerp(dark, tint, 0.07f);
            outline.Color = tint.WithAlpha(0.45f);
            crestBack.color = Color.Lerp(dark, tint, 0.14f);
            crestOutline.Color = tint;
            crestCode.color = tint;
            station.color = Color.Lerp(tint, Color.white, 0.55f);
            // The slab is the only filled block on the card: the station colour while on air, grey otherwise.
            slabBack.color = slabLive ? tint : AvTheme.Dim.WithAlpha(0.6f);
            slab.color = Color.black;
            show.color = Color.Lerp(tint, AvTheme.Dim, 0.45f);
            rule.color = tint.WithAlpha(0.25f);
            label.color = Color.Lerp(tint, AvTheme.Dim, 0.6f);
            title.color = AvStyleHost.FuiInk("readout", AvTheme.TextPrimary);
            barBack.color = tint.WithAlpha(0.18f);
            barFill.color = tint;
            clock.color = Color.Lerp(tint, AvTheme.Dim, 0.4f);
            next.color = nextIn.color = AvStyleHost.FuiInk("row-sub", AvTheme.Dim);
        }
    }

    /// <summary>
    /// Big frequency on the left; the S-report, dBm / range line and the signal bar with its squelch gate on the right.
    /// </summary>
    internal sealed class RadioReadoutPart : AvPart
    {
        private const float FreqW = 128f, Gap = 10f;
        private readonly TMP_Text value, unit, report, detail;
        private readonly AvGaugeGraphic bar;
        private readonly Image gate, hit;
        private float barX, barW = 1f, squelch;

        public RadioReadoutPart(RectTransform parent)
        {
            Rect = AvLay.Child(parent, "Readout");
            hit = AvLay.Solid(Rect, "Hit", Color.clear);
            hit.raycastTarget = true;
            value = AvText.Make(Rect, "Value", AvTextRole.Display);
            AvText.Fit(value, false);
            unit = AvText.Make(Rect, "Unit", AvTextRole.Micro);
            AvText.Fit(unit, false);
            report = AvText.Make(Rect, "Report", AvTextRole.DataStrong);
            AvText.Fit(report, false);
            detail = AvText.Make(Rect, "Detail", AvTextRole.DataSmall, string.Empty, TextAlignmentOptions.MidlineRight);
            AvText.Fit(detail, false);
            var barGo = new GameObject("Bar", typeof(RectTransform), typeof(CanvasRenderer));
            barGo.transform.SetParent(Rect, false);
            bar = barGo.AddComponent<AvGaugeGraphic>();
            bar.Shape = AvGaugeShape.Bar;
            bar.raycastTarget = false;
            gate = AvLay.Solid(Rect, "Gate", Color.clear);
            Restyle();
        }

        public string Help { set => AvHelpTip.Attach(hit.gameObject, value); }

        public override float Measure(float width) => 44f;

        public override void Place(AvSlot s)
        {
            base.Place(s);
            AvLay.Place(hit.rectTransform, 0f, 0f, s.W, s.H);
            AvLay.Place(value.rectTransform, 0f, 0f, FreqW, 28f);
            AvLay.Place(unit.rectTransform, 0f, 28f, FreqW, 15f);
            barX = FreqW + Gap;
            barW = Mathf.Max(20f, s.W - barX);
            AvLay.Place(report.rectTransform, barX, 2f, barW * 0.4f, 18f);
            AvLay.Place(detail.rectTransform, barX + barW * 0.4f, 2f, barW * 0.6f, 18f);
            AvLay.Place((RectTransform)bar.transform, barX, 26f, barW, 10f);
            PlaceGate();
        }

        public void Set(string frequency, string unitText, string reportText, string detailText, float level01, float squelch01, Color color)
        {
            if (value.text != (frequency ?? string.Empty)) value.text = frequency ?? string.Empty;
            if (unit.text != (unitText ?? string.Empty)) unit.text = unitText ?? string.Empty;
            report.text = reportText ?? string.Empty;
            report.color = color;
            detail.text = detailText ?? string.Empty;
            bar.FillColor = bar.FillEnd = color;
            bar.Value = level01;
            squelch = Mathf.Clamp01(squelch01);
            PlaceGate();
        }

        private void PlaceGate() => AvLay.Place(gate.rectTransform, barX + squelch * barW - 1f, 23f, 2f, 16f);

        public override void Restyle()
        {
            value.color = AvStyleHost.FuiInk("readout", AvTheme.TextPrimary);
            unit.color = AvStyleHost.FuiInk("readout-unit", AvTheme.Dim);
            detail.color = AvStyleHost.FuiInk("row-sub", AvTheme.Dim);
            bar.Track = AvStyleHost.FuiFill("gauge-track", AvTheme.Hairline);
            gate.color = AvStyleHost.FuiColor("caution", AvTheme.Warning);
            bar.SetVerticesDirty();
        }
    }

    /// <summary>
    /// The wire: the last lines the receiver picked up (bulletins, talk on air, intercepts, alarms), newest on top,
    /// each with its clock and the station that aired it. It is the RECEIVER page's growing part.
    /// </summary>
    internal sealed class RadioWirePart : AvPart
    {
        public const int MaxLines = 12;
        private const float LineH = 16f, Pad = 7f, MinLines = 3f;
        private readonly Image back, hit;
        private readonly RadioOutline outline;
        private readonly TMP_Text[] lines = new TMP_Text[MaxLines];

        public RadioWirePart(RectTransform parent)
        {
            Rect = AvLay.Child(parent, "Wire");
            back = AvLay.Solid(Rect, "Back", Color.clear);
            outline = new RadioOutline(Rect, "Edge");
            hit = AvLay.Solid(Rect, "Hit", Color.clear);
            hit.raycastTarget = true;
            for (int i = 0; i < MaxLines; i++)
            {
                lines[i] = AvText.Make(Rect, "Line" + i, AvTextRole.DataSmall);
                lines[i].richText = true;
                lines[i].overflowMode = TextOverflowModes.Ellipsis;
            }
            Restyle();
        }

        public string Help { set => AvHelpTip.Attach(hit.gameObject, value); }

        public override float Measure(float width) => 2f * Pad + MinLines * LineH;

        public override void Place(AvSlot s)
        {
            base.Place(s);
            AvLay.Place(back.rectTransform, 0f, 0f, s.W, s.H);
            AvLay.Place(hit.rectTransform, 0f, 0f, s.W, s.H);
            outline.Place(0f, 0f, s.W, s.H);
            int shown = Mathf.Clamp(Mathf.FloorToInt((s.H - 2f * Pad) / LineH), 1, MaxLines);
            for (int i = 0; i < MaxLines; i++)
            {
                lines[i].gameObject.SetActive(i < shown);
                AvLay.Place(lines[i].rectTransform, Pad, Pad + i * LineH, Mathf.Max(10f, s.W - 2f * Pad), LineH);
            }
        }

        /// <summary>Rich-text line i (0 = newest); null clears it.</summary>
        public void SetLine(int i, string richText)
        {
            if (i < 0 || i >= MaxLines) return;
            string t = richText ?? string.Empty;
            if (lines[i].text != t) lines[i].text = t;
        }

        public override void Restyle()
        {
            back.color = AvStyleHost.FuiFill("well", new Color(0.03f, 0.05f, 0.06f));
            outline.Color = AvStyleHost.FuiBorder("section", AvTheme.Hairline);
            foreach (TMP_Text line in lines) line.color = AvStyleHost.FuiInk("row-name", AvTheme.TextPrimary);
        }
    }

    /// <summary>
    /// Volume and squelch as two rings each with a stacked +/- pair, and the three receiver switches
    /// (mode, bandwidth, step) stacked beside them: one 82 px block where there used to be a ring row,
    /// a key row and a switch row.
    /// </summary>
    internal sealed class RadioLevelsPart : AvPart
    {
        private const float BlockH = 60f, GaugeW = 62f, KeyW = 30f, KeyH = 28f, BlockW = 104f, SwitchX = 214f, SwitchH = 18f;
        public readonly AvGauge Volume, Squelch;
        private readonly Image rule;
        public readonly AvControl VolumeDown, VolumeUp, SquelchDown, SquelchUp, Mode, Bandwidth, Step;

        public RadioLevelsPart(RectTransform parent, Action volumeDown, Action volumeUp, Action squelchDown,
            Action squelchUp, Action mode, Action bandwidth, Action step)
        {
            Rect = AvLay.Child(parent, "Levels");
            Volume = new AvGauge(Rect, "VOL");
            Squelch = new AvGauge(Rect, "SQL");
            VolumeUp = AvControl.Make(Rect, new AvControl.Spec(string.Empty, volumeUp, AvButtonStyle.Quiet, AvIcon.Plus));
            VolumeDown = AvControl.Make(Rect, new AvControl.Spec(string.Empty, volumeDown, AvButtonStyle.Quiet, AvIcon.Minus));
            SquelchUp = AvControl.Make(Rect, new AvControl.Spec(string.Empty, squelchUp, AvButtonStyle.Quiet, AvIcon.Plus));
            SquelchDown = AvControl.Make(Rect, new AvControl.Spec(string.Empty, squelchDown, AvButtonStyle.Quiet, AvIcon.Minus));
            Mode = AvControl.Make(Rect, new AvControl.Spec("MODE", mode, AvButtonStyle.Toggle));
            Bandwidth = AvControl.Make(Rect, new AvControl.Spec("BANDWIDTH", bandwidth, AvButtonStyle.Toggle));
            Step = AvControl.Make(Rect, new AvControl.Spec("STEP", step, AvButtonStyle.Toggle));
            rule = AvLay.Solid(Rect, "Rule", Color.clear);
            Restyle();
        }

        public override float Measure(float width) => 68f;

        public override void Place(AvSlot s)
        {
            base.Place(s);
            // One 60 px block: both rings, both key stacks and the three switches share its top and bottom edge.
            float top = (s.H - BlockH) * 0.5f;
            Volume.Place(new AvSlot(0f, top, GaugeW, BlockH));
            AvLay.Place(VolumeUp.Rect, GaugeW + 2f, top, KeyW, KeyH);
            AvLay.Place(VolumeDown.Rect, GaugeW + 2f, top + BlockH - KeyH, KeyW, KeyH);
            Squelch.Place(new AvSlot(BlockW, top, GaugeW, BlockH));
            AvLay.Place(SquelchUp.Rect, BlockW + GaugeW + 2f, top, KeyW, KeyH);
            AvLay.Place(SquelchDown.Rect, BlockW + GaugeW + 2f, top + BlockH - KeyH, KeyW, KeyH);

            float w = Mathf.Max(60f, s.W - SwitchX), gap = (BlockH - 3f * SwitchH) * 0.5f;
            AvLay.Place(rule.rectTransform, SwitchX - 8f, top, 1f, BlockH);
            AvLay.Place(Mode.Rect, SwitchX, top, w, SwitchH);
            AvLay.Place(Bandwidth.Rect, SwitchX, top + SwitchH + gap, w, SwitchH);
            AvLay.Place(Step.Rect, SwitchX, top + BlockH - SwitchH, w, SwitchH);
        }

        public override void Restyle()
        {
            Volume.Restyle();
            Squelch.Restyle();
            VolumeUp.Restyle(); VolumeDown.Restyle(); SquelchUp.Restyle(); SquelchDown.Restyle();
            Mode.Restyle(); Bandwidth.Restyle(); Step.Restyle();
            if (rule != null) rule.color = AvStyleHost.FuiBorder("section", AvTheme.Hairline);
        }
    }
}
