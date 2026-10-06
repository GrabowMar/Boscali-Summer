using NOAvionics;
using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace BoscaliSummer.Modules.Command.Presentation.MapUi
{
    /// <summary>
    /// A compact level setting: title, numeric value, adjacent step keys and a rectangular ladder.
    /// The row carries the same accessibility a stepper row had: the title text
    /// is named "Title" under a "Cell ..." root, and the - / + buttons are ordinary <see cref="AvControl"/>s.
    /// </summary>
    internal sealed class SetRingCell : AvPart
    {
        private const float Pad = 6f, TitleH = 16f, BtnH = 28f, Top = 5f;
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
            title = AvText.Make(Rect, "Title", AvTextRole.Label, titleText, TextAlignmentOptions.Center);
            AvText.Fit(title, false);
            var go = new GameObject("Dial", typeof(RectTransform), typeof(CanvasRenderer));
            go.transform.SetParent(Rect, false);
            dial = go.AddComponent<AvGaugeGraphic>();
            dial.Shape = AvGaugeShape.Segments;
            dial.Thickness = 7f;
            dial.Ticks = 0;
            dial.Segments = 12;
            dial.raycastTarget = false;
            value = AvText.Make(Rect, "Value", AvTextRole.Display, "", TextAlignmentOptions.Center);
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

        public override float Measure(float width) => 72f;

        public override void Place(AvSlot s)
        {
            base.Place(s);
            AvLay.Place(title.rectTransform, Pad, Top, s.W - 2f * Pad, TitleH);
            float bw = 30f, x = s.W - Pad - 2f * bw - 4f;
            AvLay.Place(value.rectTransform, Pad, 24f, x - Pad - 4f, BtnH);
            AvLay.Place(minus.Rect, x, 24f, bw, BtnH);
            AvLay.Place(plus.Rect, x + bw + 4f, 24f, bw, BtnH);
            AvLay.Place((RectTransform)dial.transform, Pad, 63f, s.W - 2f * Pad, 4f);
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
            dial.TickColor = AvStyleHost.FuiInk("chart-axis", AvTheme.Hairline);
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

        public int Count => cells.Count;

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
            Mathf.Clamp(Mathf.FloorToInt((width + Gap) / (160f + Gap)), 1, count);

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
