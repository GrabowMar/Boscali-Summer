using NOAvionics;
using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Command.Presentation.MapUi
{
    /// <summary>
    /// One switch as a thin full-width bar: a stripe that lights with the setting, the name, the state word and a
    /// slide track at the right edge. Click anywhere on the bar to flip it; the sentence lives on hover.
    /// </summary>
    internal sealed class SetToggleBar : AvPart
    {
        private const float H = 24f, Pad = 10f, Stripe = 2f, TrackW = 24f, TrackH = 8f, KnobW = 10f, StateW = 30f;
        private readonly AvFrame frame;
        private readonly Image stripe, track, knob;
        private readonly TMP_Text title, stateWord;
        private readonly Func<bool> get;
        private bool on, hover, interactable = true;
        private float width;

        public SetToggleBar(RectTransform parent, string titleText, Func<bool> get, Action<bool> set)
        {
            this.get = get;
            Rect = AvLay.Child(parent, "Cell " + titleText);
            frame = AvFrame.Add(Rect, "Frame", AvChamfer.Diagonal(3f));
            AvLay.Fill(frame.rectTransform);
            stripe = AvLay.Solid(Rect, "Stripe", Color.clear);
            title = AvText.Make(Rect, "Title", AvTextRole.Label, titleText, TextAlignmentOptions.MidlineLeft);
            AvText.Fit(title, false);
            stateWord = AvText.Make(Rect, "State", AvTextRole.DataSmall, "", TextAlignmentOptions.MidlineRight);
            track = AvLay.Solid(Rect, "Track", Color.clear);
            knob = AvLay.Solid(Rect, "Knob", Color.clear);
            stripe.raycastTarget = track.raycastTarget = knob.raycastTarget = false;
            AvHit hit = AvHit.On(frame);
            hit.Hover = h => { hover = h; Restyle(); };
            hit.Click = e =>
            {
                if (e.button != PointerEventData.InputButton.Left || !interactable) return;
                set(!on);
                Refresh();
            };
            Refresh();
        }

        public string Help { set => AvHelpTip.Attach(frame.gameObject, value); }

        public bool Interactable
        {
            get => interactable;
            set { if (interactable == value) return; interactable = value; frame.GetComponent<AvHit>().Interactable = value; Restyle(); }
        }

        public void Refresh()
        {
            bool now = get();
            if (now != on) { on = now; PlaceKnob(); }
            AvText.Set(stateWord, on ? "ON" : "OFF");
            Restyle();
        }

        public override float Measure(float w) => H;

        public override void Place(AvSlot s)
        {
            base.Place(s);
            width = s.W;
            float right = s.W - Pad - TrackW;
            AvLay.Place(stripe.rectTransform, 0f, 4f, Stripe, s.H - 8f);
            AvLay.Place(title.rectTransform, Pad, 0f, right - StateW - 6f - Pad, s.H);
            AvLay.Place(stateWord.rectTransform, right - StateW - 6f, 0f, StateW, s.H);
            AvLay.Place(track.rectTransform, right, (s.H - TrackH) * .5f, TrackW, TrackH);
            PlaceKnob();
        }

        private void PlaceKnob()
        {
            if (width <= 0f) return;
            float x = width - Pad - TrackW + (on ? TrackW - KnobW : 0f);
            AvLay.Place(knob.rectTransform, x, (H - TrackH) * .5f, KnobW, TrackH);
        }

        public override void Restyle()
        {
            string st = !interactable ? "disabled" : hover ? "hover" : null;
            AvStyle c = AvStyleHost.FuiStyle("cell", st);
            frame.Paint(AvStyleHost.Resolve(c.Background, AvTheme.SurfaceInert), AvStyleHost.Resolve(c.Border, AvTheme.Hairline));
            Color lit = AvStyleHost.FuiFill("cell-led", AvTheme.RailInert, "on");
            Color dark = AvStyleHost.FuiFill("gauge-track", AvTheme.Hairline);
            bool shown = on && interactable;
            title.color = interactable ? AvStyleHost.Resolve(c.Color, AvTheme.TextPrimary) : AvTheme.Disabled;
            stateWord.color = AvStyleHost.FuiInk("cell-state", AvTheme.Disabled, shown ? "on" : null);
            stripe.color = shown ? lit : Color.clear;
            track.color = shown ? new Color(lit.r, lit.g, lit.b, .35f) : dark;
            knob.color = shown ? lit : interactable ? AvStyleHost.FuiInk("cell-sub", AvTheme.Dim) : AvTheme.Disabled;
        }
    }

    /// <summary>
    /// A compact level setting: title, numeric value, adjacent step keys and a rectangular ladder.
    /// The row carries the same accessibility a stepper row had: the title text
    /// is named "Title" under a "Cell ..." root, and the - / + buttons are ordinary <see cref="AvControl"/>s.
    /// </summary>
    internal sealed class SetRingCell : AvPart
    {
        private const float Pad = 10f, H = 30f, BtnW = 22f, BtnH = 20f, Ladder = 2f;
        private readonly AvFrame frame;
        private readonly TMP_Text title, value;
        private readonly AvGaugeGraphic dial;
        private readonly AvControl minus, plus;
        private AvState state = AvState.Ready;
        private bool available = true;

        public SetRingCell(RectTransform parent, string titleText, Action onMinus, Action onPlus)
        {
            Rect = AvLay.Child(parent, "Cell " + titleText);
            frame = AvFrame.Add(Rect, "Frame", AvChamfer.Diagonal(5f));
            AvLay.Fill(frame.rectTransform);
            frame.raycastTarget = true;
            title = AvText.Make(Rect, "Title", AvTextRole.Label, titleText, TextAlignmentOptions.MidlineLeft);
            AvText.Fit(title, false);
            var go = new GameObject("Dial", typeof(RectTransform), typeof(CanvasRenderer));
            go.transform.SetParent(Rect, false);
            dial = go.AddComponent<AvGaugeGraphic>();
            dial.Shape = AvGaugeShape.Segments;
            dial.Segments = 12;
            dial.raycastTarget = false;
            value = AvText.Make(Rect, "Value", AvTextRole.DataStrong, "", TextAlignmentOptions.MidlineRight);
            AvText.Fit(value, false);
            minus = AvControl.Make(Rect, new AvControl.Spec("", onMinus, AvButtonStyle.Quiet, AvIcon.Minus));
            plus = AvControl.Make(Rect, new AvControl.Spec("", onPlus, AvButtonStyle.Quiet, AvIcon.Plus));
            Restyle();
        }

        /// <summary>Refresh the level and its compact numeric label.</summary>
        public void Bind(string ringText, float level01, bool isAvailable, bool canMinus, bool canPlus,
            string help, string minusHelp, string plusHelp)
        {
            string text = ringText ?? "";
            if (value.text != text) value.text = text;
            dial.Value = level01;
            if (isAvailable != available)
            {
                available = isAvailable;
                state = available ? AvState.Ready : AvState.Inert;
                Restyle();
            }
            minus.Interactable = isAvailable && canMinus;
            plus.Interactable = isAvailable && canPlus;
            AvHelpTip.Attach(frame.gameObject, help);
            minus.Help = minusHelp;
            plus.Help = plusHelp;
        }

        public override float Measure(float width) => H;

        // One thin line: name at the left, value then - / + at the right, the level ladder as a hairline underneath.
        public override void Place(AvSlot s)
        {
            base.Place(s);
            float y = (H - Ladder - BtnH) * .5f, bx = s.W - Pad - 2f * BtnW - 2f;
            AvLay.Place(minus.Rect, bx, y, BtnW, BtnH);
            AvLay.Place(plus.Rect, bx + BtnW + 2f, y, BtnW, BtnH);
            AvLay.Place(value.rectTransform, bx - 76f, 0f, 70f, H - Ladder);
            AvLay.Place(title.rectTransform, Pad, 0f, bx - 82f - Pad, H - Ladder);
            AvLay.Place((RectTransform)dial.transform, Pad, H - Ladder - 2f, s.W - 2f * Pad, Ladder);
        }

        public override void Restyle()
        {
            AvStyle cell = AvStyleHost.FuiStyle("cell", available ? null : "disabled");
            frame.Paint(AvStyleHost.Resolve(cell.Background, AvTheme.SurfaceInert), AvStyleHost.Resolve(cell.Border, AvTheme.Hairline));
            title.color = available
                ? AvStyleHost.FuiInk("row-name", AvTheme.TextPrimary)
                : AvTheme.Disabled;
            value.color = available
                ? AvStyleHost.FuiInk("metric-value", AvTheme.TextPrimary)
                : AvTheme.Disabled;
            dial.Track = AvStyleHost.FuiFill("gauge-track", AvTheme.Hairline);
            Color fill = AvStyleHost.FuiFill("metric-fill " + AvStates.Class(state), AvTheme.Accent);
            dial.FillColor = fill;
            dial.FillEnd = Color.Lerp(fill, Color.white, 0.25f);
            dial.SetVerticesDirty();
            minus.Restyle();
            plus.Restyle();
        }
    }

    /// <summary>
    /// Step cells wrap when their labels and values need more width. Hidden cells collapse and the rest
    /// share each line's width, so a row of dials never has a hole in the middle. The flow re-lays a line only when
    /// its height moves, so a caller that shows or hides a cell calls <see cref="Reflow"/>.
    /// </summary>
    internal sealed class SetRingRow : AvPart
    {
        private const float Gap = AvGridTokens.Gap;
        private readonly List<AvPart> cells = new List<AvPart>(4);
        private AvSlot lastSlot;
        private bool placed;

        public SetRingRow(RectTransform parent) { Rect = AvLay.Child(parent, "RingRow"); }

        public T Add<T>(T part) where T : AvPart
        {
            part.Parent = this;
            cells.Add(part);
            part.Rect.SetParent(Rect, false);
            return part;
        }

        private int ShownCount()
        {
            int n = 0;
            for (int i = 0; i < cells.Count; i++) if (cells[i].Shown) n++;
            return n;
        }

        public override float Measure(float width)
        {
            int n = ShownCount();
            if (n == 0) return 0f;
            int columns = Columns(width, n), at = 0;
            float total = 0f, lineHeight = 0f;
            for (int i = 0; i < cells.Count; i++)
            {
                if (!cells[i].Shown) continue;
                int lineCount = Mathf.Min(columns, n - at / columns * columns);
                float w = AvFlowMath.ColumnWidth(width, lineCount, Gap);
                lineHeight = Mathf.Max(lineHeight, cells[i].Measure(w));
                at++;
                if (at % columns == 0 || at == n)
                {
                    total += lineHeight + (at == n ? 0f : Gap);
                    lineHeight = 0f;
                }
            }
            return total;
        }

        private static int Columns(float width, int count) =>
            Mathf.Clamp(Mathf.FloorToInt((width + Gap) / (320f + Gap)), 1, count);   // thin bars: one per line on an MFD

        public override void Place(AvSlot s)
        {
            base.Place(s);
            lastSlot = s; placed = true;
            int n = ShownCount();
            if (n == 0) return;
            int columns = Columns(s.W, n), at = 0;
            float y = 0f, lineHeight = 0f;
            for (int i = 0; i < cells.Count; i++)
            {
                if (!cells[i].Shown) continue;
                int lineCount = Mathf.Min(columns, n - at / columns * columns);
                float w = AvFlowMath.ColumnWidth(s.W, lineCount, Gap);
                float h = cells[i].Measure(w);
                cells[i].Place(new AvSlot(at % columns * (w + Gap), y, w, h));
                lineHeight = Mathf.Max(lineHeight, h);
                at++;
                if (at % columns == 0 || at == n) { y += lineHeight + Gap; lineHeight = 0f; }
            }
        }

        /// <summary>Re-place the cells inside the row's current slot after one of them was shown or hidden.</summary>
        public void Reflow()
        {
            SetShown(ShownCount() > 0);   // a row with no dial left collapses, gap included
            if (placed && Shown) Place(lastSlot);
            Changed();
        }

        public override void Restyle()
        {
            for (int i = 0; i < cells.Count; i++) cells[i].Restyle();
        }
    }
}
