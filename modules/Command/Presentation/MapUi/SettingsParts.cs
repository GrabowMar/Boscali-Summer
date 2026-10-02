using NOAvionics;
using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Command.Presentation.MapUi
{
    /// <summary>
    /// A compact level setting: title, numeric value, adjacent step keys and a rectangular ladder.
    /// The row carries the same accessibility a stepper row had: the title text
    /// is named "Title" under a "Cell ..." root, and the - / + buttons are ordinary <see cref="AvControl"/>s.
    /// </summary>
    internal sealed class SetRingCell : AvPart
    {
        private const float Pad = 6f, TitleH = 16f, BtnH = 24f, Top = 5f;
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

        public override float Measure(float width) => 66f;

        public override void Place(AvSlot s)
        {
            base.Place(s);
            AvLay.Place(title.rectTransform, Pad, Top, s.W - 2f * Pad, TitleH);
            float bw = 25f, x = s.W - Pad - 2f * bw - 3f;
            AvLay.Place(value.rectTransform, Pad, 24f, x - Pad - 3f, 24f);
            AvLay.Place(minus.Rect, x, 24f, bw, BtnH);
            AvLay.Place(plus.Rect, x + bw + 3f, 24f, bw, BtnH);
            AvLay.Place((RectTransform)dial.transform, Pad, 55f, s.W - 2f * Pad, 4f);
        }

        public override void Restyle()
        {
            AvStyle cell = AvStyleHost.FuiStyle("cell", available ? null : "disabled");
            frame.Paint(AvStyleHost.Resolve(cell.Background, AvTheme.SurfaceInert), AvStyleHost.Resolve(cell.Border, AvTheme.Hairline));
            title.color = available
                ? AvStyleHost.Resolve(AvStyleHost.FuiStyle("row-name").Color, AvTheme.TextPrimary)
                : AvTheme.Disabled;
            value.color = available
                ? AvStyleHost.Resolve(AvStyleHost.FuiStyle("metric-value").Color, AvTheme.TextPrimary)
                : AvTheme.Disabled;
            dial.Track = AvStyleHost.Resolve(AvStyleHost.FuiStyle("gauge-track").Background, AvTheme.Hairline);
            dial.TickColor = AvStyleHost.Resolve(AvStyleHost.FuiStyle("chart-axis").Color, AvTheme.Hairline);
            Color fill = AvStyleHost.Resolve(AvStyleHost.FuiStyle("metric-fill " + AvStates.Class(state)).Background, AvTheme.Accent);
            dial.FillColor = fill;
            dial.FillEnd = Color.Lerp(fill, Color.white, 0.25f);
            dial.SetVerticesDirty();
            minus.Restyle();
            plus.Restyle();
        }
    }

    /// <summary>
    /// One flow line that holds up to four ring cells in equal columns. Hidden cells collapse and the rest
    /// share the width, so a row of dials never has a hole in the middle. The flow re-lays a line only when
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
            float w = AvFlowMath.ColumnWidth(width, n, Gap), h = 0f;
            for (int i = 0; i < cells.Count; i++)
                if (cells[i].Shown) h = Mathf.Max(h, cells[i].Measure(w));
            return h;
        }

        public override void Place(AvSlot s)
        {
            base.Place(s);
            lastSlot = s; placed = true;
            int n = ShownCount();
            if (n == 0) return;
            float w = AvFlowMath.ColumnWidth(s.W, n, Gap);
            int at = 0;
            for (int i = 0; i < cells.Count; i++)
            {
                if (!cells[i].Shown) continue;
                cells[i].Place(new AvSlot(at * (w + Gap), 0f, w, s.H));
                at++;
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

    /// <summary>Filled rectangles (flat or gradient) in the owner's top-left pixel space: what the SET previews draw with, one draw call each.</summary>
    internal sealed class SetQuadGraphic : MaskableGraphic
    {
        private struct Quad
        {
            public float X, Y, W, H;
            public Color A, B;
            public bool Horizontal;
        }

        private readonly Quad[] quads = new Quad[640];
        private int count;

        public void Begin() { count = 0; }

        public void Add(float x, float y, float w, float h, Color c) => Add(x, y, w, h, c, c, false);

        /// <summary>A gradient from <paramref name="a"/> (top, or left when horizontal) to <paramref name="b"/>.</summary>
        public void Add(float x, float y, float w, float h, Color a, Color b, bool horizontal)
        {
            if (count >= quads.Length || w <= 0f || h <= 0f) return;
            quads[count++] = new Quad { X = x, Y = y, W = w, H = h, A = a, B = b, Horizontal = horizontal };
        }

        public void End() => SetVerticesDirty();

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Rect r = rectTransform.rect;
            for (int i = 0; i < count; i++)
            {
                Quad q = quads[i];
                float x0 = r.xMin + q.X, x1 = x0 + q.W, y1 = r.yMax - q.Y, y0 = y1 - q.H;
                Color bl = q.Horizontal ? q.A : q.B, tl = q.A, tr = q.Horizontal ? q.B : q.A, br = q.B;
                int c = vh.currentVertCount;
                vh.AddVert(new Vector3(x0, y0), bl, Vector2.zero);
                vh.AddVert(new Vector3(x0, y1), tl, Vector2.zero);
                vh.AddVert(new Vector3(x1, y1), tr, Vector2.zero);
                vh.AddVert(new Vector3(x1, y0), br, Vector2.zero);
                vh.AddTriangle(c, c + 1, c + 2);
                vh.AddTriangle(c, c + 2, c + 3);
            }
        }
    }
}
