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

    /// <summary>
    /// A section header that also carries its controls: icon, title, caption, then up to a few icon keys on
    /// the right (paging arrows, rescan, open folder). Replaces "section + stepper row + button row".
    /// </summary>
    internal sealed class RadioHeaderPart : AvPart
    {
        private const float ButtonW = 30f, ButtonGap = 3f;
        private readonly TMP_Text icon, title, caption;
        private readonly Image rule, cap;
        private readonly AvControl[] buttons;
        private float placedW = -1f, placedH;

        public AvControl this[int index] => buttons[index];

        public RadioHeaderPart(RectTransform parent, AvIcon glyph, string titleText, params AvControl.Spec[] specs)
        {
            Rect = AvLay.Child(parent, "Header " + titleText);
            icon = AvIcons.Make(Rect, glyph, AvGridTokens.IconHead, Color.white);
            title = AvText.Make(Rect, "Title", AvTextRole.Head, titleText, TextAlignmentOptions.MidlineLeft, true);
            caption = AvText.Make(Rect, "Caption", AvTextRole.Micro, "", TextAlignmentOptions.MidlineRight);
            rule = AvLay.Solid(Rect, "Rule", Color.clear);
            cap = AvLay.Solid(Rect, "Cap", Color.clear);
            buttons = new AvControl[specs.Length];
            for (int i = 0; i < specs.Length; i++) buttons[i] = AvControl.Make(Rect, specs[i]);
            Restyle();
        }

        public void SetTitle(string text)
        {
            if (title.text == (text ?? string.Empty)) return;
            title.text = text ?? string.Empty;
            Arrange();
            Changed();
        }

        public void SetCaption(string text)
        {
            if (caption.text == (text ?? string.Empty)) return;
            caption.text = text ?? string.Empty;
            Arrange();
            Changed();
        }

        private float TitleWidth(float width)
        {
            float strip = buttons.Length == 0 ? 0f : buttons.Length * (ButtonW + ButtonGap) - ButtonGap;
            float left = width - strip;
            float captionW = Mathf.Min(AvText.Width(caption) + 2f, Mathf.Max(0f, left - 90f));
            return Mathf.Max(30f, left - (buttons.Length == 0 ? 0f : 8f) - captionW - 30f);
        }

        private bool TitleRow(float width) => AvText.Width(title) > TitleWidth(width);
        private float TitleHeight(float width) => Mathf.Max(22f, AvText.Height(title, width - 22f));

        public override float Measure(float width) => TitleRow(width) ? TitleHeight(width) + 28f : 28f;

        public override void Place(AvSlot s)
        {
            base.Place(s);
            placedW = s.W;
            placedH = s.H;
            Arrange();
        }

        private void Arrange()
        {
            if (placedW < 0f) return;
            float w = placedW;
            float strip = buttons.Length == 0 ? 0f : buttons.Length * (ButtonW + ButtonGap) - ButtonGap;
            float left = w - strip;
            bool titleRow = TitleRow(w);
            float keyY = titleRow ? TitleHeight(w) : 0f;
            for (int i = 0; i < buttons.Length; i++)
                AvLay.Place(buttons[i].Rect, left + i * (ButtonW + ButtonGap), keyY + 1f, ButtonW, 25f);
            float captionW = Mathf.Min(AvText.Width(caption) + 2f, Mathf.Max(0f, left - 90f));
            float captionX = left - (buttons.Length == 0 ? 0f : 8f) - captionW;
            AvLay.Place(icon.rectTransform, 0f, 5f, 16f, 16f);
            AvLay.Place(title.rectTransform, 22f, 0f, titleRow ? w - 22f : TitleWidth(w), titleRow ? keyY : 26f);
            AvLay.Place(caption.rectTransform, captionX, keyY, captionW, 26f);
            AvLay.Place(rule.rectTransform, 0f, placedH - 1f, w, 1f);
            AvLay.Place(cap.rectTransform, 0f, placedH - 3f, 28f, 3f);
        }

        public override void Restyle()
        {
            title.color = AvStyleHost.Resolve(AvStyleHost.FuiStyle("section-title").Color, AvTheme.RailInfo);
            icon.color = AvStyleHost.Resolve(AvStyleHost.FuiStyle("section-icon").Color, AvTheme.RailInfo);
            caption.color = AvStyleHost.Resolve(AvStyleHost.FuiStyle("section-caption").Color, AvTheme.Disabled);
            rule.color = AvStyleHost.Resolve(AvStyleHost.FuiStyle("section").Border, AvTheme.Hairline);
            cap.color = title.color;
            foreach (AvControl b in buttons) b.Restyle();
        }
    }

    /// <summary>
    /// The receiver's head line: frequency and band mode on the left; station, programme, the track on
    /// air and a status slab on the right. The compact block grows when station names or programme text wrap.
    /// </summary>
    internal sealed class RadioTunePart : AvPart
    {
        private const float LeftW = 132f;
        private readonly TMP_Text value, unit, mode, station, programme, track, slabText;
        private readonly Image slabBack, hit;
        private AvState slabState = AvState.Inert;
        private float placedW = -1f;

        public RadioTunePart(RectTransform parent)
        {
            Rect = AvLay.Child(parent, "Tune");
            hit = AvLay.Solid(Rect, "Hit", Color.clear);
            hit.raycastTarget = true;
            value = AvText.Make(Rect, "Value", AvTextRole.Display);
            unit = AvText.Make(Rect, "Unit", AvTextRole.Label);
            mode = AvText.Make(Rect, "Mode", AvTextRole.Micro);
            station = AvText.Make(Rect, "Station", AvTextRole.Head, string.Empty, TextAlignmentOptions.MidlineLeft, true);
            programme = AvText.Make(Rect, "Programme", AvTextRole.ProseSmall);
            track = AvText.Make(Rect, "Track", AvTextRole.Micro);
            slabBack = AvLay.Solid(Rect, "SlabBack", Color.clear);
            slabText = AvText.Make(Rect, "Slab", AvTextRole.Micro, string.Empty, TextAlignmentOptions.Center);
            AvText.Fit(value, false); // VHF's three decimals must stay clear of the MHz unit.
            programme.enableWordWrapping = true;
            AvText.Fit(track, false);
            AvText.Fit(mode, false);
            Restyle();
        }

        public string Help { set => AvHelpTip.Attach(hit.gameObject, value); }

        public void Set(string frequency, string unitText, string modeText, string stationText,
            string programmeText, string trackText, string statusWord, AvState state)
        {
            bool moved = false;
            if (value.text != (frequency ?? string.Empty)) { value.text = frequency ?? string.Empty; moved = true; }
            if (unit.text != (unitText ?? string.Empty)) unit.text = unitText ?? string.Empty;
            if (mode.text != (modeText ?? string.Empty)) mode.text = modeText ?? string.Empty;
            if (station.text != (stationText ?? string.Empty)) { station.text = stationText ?? string.Empty; moved = true; Changed(); }
            if (programme.text != (programmeText ?? string.Empty)) { programme.text = programmeText ?? string.Empty; moved = true; Changed(); }
            if (track.text != (trackText ?? string.Empty)) track.text = trackText ?? string.Empty;
            string word = AvStates.Glyph(state) + (statusWord ?? string.Empty);
            if (slabText.text != word) { slabText.text = word; moved = true; Changed(); }
            if (state != slabState) { slabState = state; RestyleSlab(); }
            if (moved && placedW > 0f) Arrange(placedW);
        }

        private float SlabWidth(float rightWidth) => Mathf.Min(rightWidth * 0.5f, AvText.Width(slabText) + 14f);
        private float StationHeight(float width)
        {
            float rightWidth = Mathf.Max(1f, width - LeftW - 8f);
            return Mathf.Max(19f, AvText.Height(station, Mathf.Max(1f, rightWidth - SlabWidth(rightWidth) - 8f)));
        }

        public override float Measure(float width) => Mathf.Max(50f,
            StationHeight(width) + 3f + AvText.Height(programme, Mathf.Max(1f, width - LeftW - 8f)) + 15f);

        public override void Place(AvSlot s)
        {
            base.Place(s);
            placedW = s.W;
            AvLay.Place(hit.rectTransform, 0f, 0f, s.W, s.H);
            Arrange(s.W);
        }

        private void Arrange(float w)
        {
            float vw = Mathf.Min(AvText.Width(value), LeftW - 40f);
            AvLay.Place(value.rectTransform, 0f, 0f, vw + 2f, 32f);
            AvLay.Place(unit.rectTransform, vw + 6f, 9f, Mathf.Max(0f, LeftW - vw - 6f), 20f);
            AvLay.Place(mode.rectTransform, 0f, 33f, LeftW, 16f);

            float x0 = LeftW + 8f, rw = Mathf.Max(0f, w - x0);
            float sw = SlabWidth(rw), stationH = StationHeight(w);
            AvLay.Place(slabBack.rectTransform, w - sw, 1f, sw, 17f);
            AvLay.Place(slabText.rectTransform, w - sw, 1f, sw, 17f);
            AvLay.Place(station.rectTransform, x0, 0f, Mathf.Max(0f, rw - sw - 8f), stationH);
            float programmeH = AvText.Height(programme, rw);
            AvLay.Place(programme.rectTransform, x0, stationH + 1f, rw, programmeH);
            AvLay.Place(track.rectTransform, x0, stationH + 3f + programmeH, rw, 15f);
        }

        public override void Restyle()
        {
            value.color = AvStyleHost.Resolve(AvStyleHost.FuiStyle("readout").Color, AvTheme.TextPrimary);
            unit.color = mode.color = AvStyleHost.Resolve(AvStyleHost.FuiStyle("readout-unit").Color, AvTheme.Dim);
            station.color = AvStyleHost.Resolve(AvStyleHost.FuiStyle("row-name").Color, AvTheme.TextPrimary);
            programme.color = AvStyleHost.Resolve(AvStyleHost.FuiStyle("row-sub").Color, AvTheme.Dim);
            track.color = AvStyleHost.Resolve(AvStyleHost.FuiStyle("metric-key").Color, AvTheme.RailInfo);
            RestyleSlab();
        }

        private void RestyleSlab()
        {
            slabBack.color = AvStyleHost.Resolve(AvStyleHost.FuiStyle("slab " + AvStates.Class(slabState)).Background, AvTheme.Accent);
            slabText.color = AvStyleHost.Resolve(AvStyleHost.FuiStyle("slab").Color, Color.black);
        }
    }

    /// <summary>
    /// One slim line: S-report, a level bar with the squelch gate drawn on it, and the dBm / state word.
    /// Kit gap: v2 has no meter-with-marker widget, so it is built from <see cref="AvText"/>,
    /// <see cref="AvGaugeGraphic"/> and <see cref="AvLay"/>.
    /// </summary>
    internal sealed class RadioSignalPart : AvPart
    {
        private const float ReportW = 58f, DetailW = 118f, Gap = 6f;
        private readonly TMP_Text report, detail;
        private readonly AvGaugeGraphic bar;
        private readonly Image gate, hit;
        private float barX, barW = 1f, squelch;

        public RadioSignalPart(RectTransform parent)
        {
            Rect = AvLay.Child(parent, "Meter");
            hit = AvLay.Solid(Rect, "Hit", Color.clear);
            hit.raycastTarget = true;
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

        public override float Measure(float width) => 20f;

        public override void Place(AvSlot s)
        {
            base.Place(s);
            barX = ReportW + Gap;
            barW = Mathf.Max(20f, s.W - ReportW - DetailW - 2f * Gap);
            AvLay.Place(hit.rectTransform, 0f, 0f, s.W, s.H);
            AvLay.Place(report.rectTransform, 0f, 0f, ReportW, s.H);
            AvLay.Place(detail.rectTransform, s.W - DetailW, 0f, DetailW, s.H);
            AvLay.Place((RectTransform)bar.transform, barX, 6f, barW, 8f);
            PlaceGate();
        }

        public void Set(string reportText, string detailText, float level01, float squelch01, Color color)
        {
            report.text = reportText ?? string.Empty;
            report.color = color;
            detail.text = detailText ?? string.Empty;
            bar.FillColor = bar.FillEnd = color;
            bar.Value = level01;
            squelch = Mathf.Clamp01(squelch01);
            PlaceGate();
        }

        private void PlaceGate() => AvLay.Place(gate.rectTransform, barX + squelch * barW - 1f, 3f, 2f, 14f);

        public override void Restyle()
        {
            detail.color = AvStyleHost.Resolve(AvStyleHost.FuiStyle("row-sub").Color, AvTheme.Dim);
            bar.Track = AvStyleHost.Resolve(AvStyleHost.FuiStyle("gauge-track").Background, AvTheme.Hairline);
            gate.color = AvStyleHost.FuiColor("caution", AvTheme.Warning);
            bar.SetVerticesDirty();
        }
    }

    /// <summary>
    /// Volume and squelch as two rings each with a stacked +/- pair, and the three receiver switches
    /// (mode, bandwidth, step) stacked beside them: one 82 px block where there used to be a ring row,
    /// a key row and a switch row.
    /// </summary>
    internal sealed class RadioLevelsPart : AvPart
    {
        private const float Ring = 30f, GaugeW = 62f, KeyW = 30f, BlockW = 104f, SwitchX = 212f;
        public readonly AvGauge Volume, Squelch;
        public readonly AvControl VolumeDown, VolumeUp, SquelchDown, SquelchUp, Mode, Bandwidth, Step;

        public RadioLevelsPart(RectTransform parent, Action volumeDown, Action volumeUp, Action squelchDown,
            Action squelchUp, Action mode, Action bandwidth, Action step)
        {
            Rect = AvLay.Child(parent, "Levels");
            Volume = new AvGauge(Rect, "VOL", AvGaugeShape.Segments, Ring);
            Squelch = new AvGauge(Rect, "SQL", AvGaugeShape.Segments, Ring);
            VolumeUp = AvControl.Make(Rect, new AvControl.Spec(string.Empty, volumeUp, AvButtonStyle.Quiet, AvIcon.Plus));
            VolumeDown = AvControl.Make(Rect, new AvControl.Spec(string.Empty, volumeDown, AvButtonStyle.Quiet, AvIcon.Minus));
            SquelchUp = AvControl.Make(Rect, new AvControl.Spec(string.Empty, squelchUp, AvButtonStyle.Quiet, AvIcon.Plus));
            SquelchDown = AvControl.Make(Rect, new AvControl.Spec(string.Empty, squelchDown, AvButtonStyle.Quiet, AvIcon.Minus));
            Mode = AvControl.Make(Rect, new AvControl.Spec("MODE", mode, AvButtonStyle.Toggle));
            Bandwidth = AvControl.Make(Rect, new AvControl.Spec("BANDWIDTH", bandwidth, AvButtonStyle.Toggle));
            Step = AvControl.Make(Rect, new AvControl.Spec("STEP", step, AvButtonStyle.Toggle));
        }

        public override float Measure(float width) => 68f;

        public override void Place(AvSlot s)
        {
            base.Place(s);
            float gy = (s.H - (Ring + 18f)) * 0.5f, ky = (s.H - 60f) * 0.5f;
            Volume.Place(new AvSlot(0f, gy, GaugeW, Ring + 18f));
            AvLay.Place(VolumeUp.Rect, GaugeW + 2f, ky, KeyW, 28f);
            AvLay.Place(VolumeDown.Rect, GaugeW + 2f, ky + 32f, KeyW, 28f);
            Squelch.Place(new AvSlot(BlockW, gy, GaugeW, Ring + 18f));
            AvLay.Place(SquelchUp.Rect, BlockW + GaugeW + 2f, ky, KeyW, 28f);
            AvLay.Place(SquelchDown.Rect, BlockW + GaugeW + 2f, ky + 32f, KeyW, 28f);

            float w = Mathf.Max(60f, s.W - SwitchX), y = (s.H - 64f) * 0.5f;
            AvLay.Place(Mode.Rect, SwitchX, y, w, 20f);
            AvLay.Place(Bandwidth.Rect, SwitchX, y + 22f, w, 20f);
            AvLay.Place(Step.Rect, SwitchX, y + 44f, w, 20f);
        }

        public override void Restyle()
        {
            Volume.Restyle();
            Squelch.Restyle();
            VolumeUp.Restyle(); VolumeDown.Restyle(); SquelchUp.Restyle(); SquelchDown.Restyle();
            Mode.Restyle(); Bandwidth.Restyle(); Step.Restyle();
        }
    }
}
