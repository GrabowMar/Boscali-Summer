using NOAvionics;
using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Comms.Presentation
{
    // Local layout parts of the COM console. The kit has no part for a key grid with wide keys, a swatch-and-width
    // line, a four-cell tally or a multi-column ledger row, so each is composed here from kit primitives.
    internal sealed partial class CommsMfdPanel
    {
        /// <summary>
        /// Keys on one column grid, so every row of a deck lines up with the rows above it. A key may span
        /// several columns (the OFF key takes two), and a short last row keeps its columns instead of stretching.
        /// </summary>
        private sealed class KeyGrid : AvPart
        {
            private readonly int columns;
            private readonly int[] spans;
            private readonly int rows;
            public readonly AvControl[] Controls;

            public KeyGrid(RectTransform parent, int columns, AvControl.Spec[] specs, int[] spans)
            {
                Rect = AvLay.Child(parent, "Key Grid");
                this.columns = columns;
                this.spans = spans;
                Controls = new AvControl[specs.Length];
                int col = 0, row = 0;
                for (int i = 0; i < specs.Length; i++)
                {
                    Controls[i] = AvControl.Make(Rect, specs[i]);
                    if (col + spans[i] > columns) { row++; col = 0; }
                    col += spans[i];
                }
                rows = row + 1;
            }

            public override float Measure(float width) => rows * AvGridTokens.Row + (rows - 1) * AvGridTokens.Gap;

            public override void Place(AvSlot s)
            {
                base.Place(s);
                float cell = AvFlowMath.ColumnWidth(s.W, columns, AvGridTokens.Gap);
                int col = 0, row = 0;
                for (int i = 0; i < Controls.Length; i++)
                {
                    if (col + spans[i] > columns) { row++; col = 0; }
                    float w = cell * spans[i] + AvGridTokens.Gap * (spans[i] - 1);
                    AvLay.Place(Controls[i].Rect, col * (cell + AvGridTokens.Gap), row * (AvGridTokens.Row + AvGridTokens.Gap), w, AvGridTokens.Row);
                    col += spans[i];
                }
            }

            public override void Restyle()
            {
                foreach (AvControl c in Controls) c.Restyle();
            }
        }

        /// <summary>
        /// The ink line: six swatches and the three line weights, each group under its own key that names the
        /// choice in words (colour alone never carries it), the picked swatch ringed and ticked.
        /// </summary>
        private sealed class InkDeck : AvPart
        {
            private const float SwatchW = 34f, Gap = 4f, Between = 14f, KeyH = 15f;

            public readonly PenSwatch[] Swatches;
            public readonly AvControl[] Widths;
            private readonly TMP_Text inkKey, lineKey;
            private float widthsX;

            public InkDeck(RectTransform parent, Color[] inks, Action<int> pickInk, string[] widthNames, Action<int> pickWidth)
            {
                Rect = AvLay.Child(parent, "Ink Deck");
                inkKey = AvText.Make(Rect, "InkKey", AvTextRole.Micro);
                lineKey = AvText.Make(Rect, "LineKey", AvTextRole.Micro);
                AvText.Fit(inkKey, false); AvText.Fit(lineKey, false);
                Swatches = new PenSwatch[inks.Length];
                for (int i = 0; i < inks.Length; i++)
                {
                    int ink = i;
                    Swatches[i] = new PenSwatch(Rect, inks[i]);
                    Swatches[i].Clicked += () => pickInk(ink);
                }
                Widths = new AvControl[widthNames.Length];
                for (int i = 0; i < widthNames.Length; i++)
                {
                    int w = i;
                    Widths[i] = AvControl.Make(Rect, new AvControl.Spec(widthNames[i], () => pickWidth(w)));
                }
                Restyle();
            }

            /// <summary>The two key lines, e.g. "INK · CYAN" and "LINE · MED".</summary>
            public void SetKeys(string ink, string line)
            {
                AvText.Set(inkKey, ink);
                AvText.Set(lineKey, line);
            }

            public override float Measure(float width) => KeyH + 3f + AvGridTokens.Row;

            public override void Place(AvSlot s)
            {
                base.Place(s);
                float y = KeyH + 3f, x = 0f;
                for (int i = 0; i < Swatches.Length; i++)
                {
                    Swatches[i].Place(new AvSlot(x, y, SwatchW, AvGridTokens.Row));
                    x += SwatchW + Gap;
                }
                widthsX = x - Gap + Between;
                float ow = AvFlowMath.ColumnWidth(Mathf.Max(0f, s.W - widthsX), Widths.Length, Gap);
                for (int i = 0; i < Widths.Length; i++) AvLay.Place(Widths[i].Rect, widthsX + i * (ow + Gap), y, ow, AvGridTokens.Row);
                AvLay.Place(inkKey.rectTransform, 0f, 0f, Mathf.Max(0f, widthsX - Between), KeyH);
                AvLay.Place(lineKey.rectTransform, widthsX, 0f, Mathf.Max(0f, s.W - widthsX), KeyH);
            }

            public override void Restyle()
            {
                inkKey.color = lineKey.color = AvStyleHost.FuiInk("metric-key", AvTheme.RailInfo);
                if (Swatches == null) return;
                foreach (PenSwatch s in Swatches) s.Restyle();
                foreach (AvControl w in Widths) w.Restyle();
            }
        }

        /// <summary>One clickable ink swatch: the pen's own colour (data) in a frame; the picked one is ringed in the select colour and ticked.</summary>
        private sealed class PenSwatch : AvPart
        {
            private readonly AvFrame frame;
            private readonly Color ink;
            private readonly TMP_Text tick;
            private bool selected;

            public event Action Clicked;

            public PenSwatch(RectTransform parent, Color inkColor)
            {
                ink = inkColor;
                Rect = AvLay.Child(parent, "Pen Swatch");
                frame = AvFrame.Add(Rect, "Frame", AvChamfer.Diagonal(3f));
                AvLay.Fill(frame.rectTransform);
                AvHit hit = AvHit.On(frame);
                hit.Click = _ => Clicked?.Invoke();
                tick = AvIcons.Make(Rect, AvIcon.CircleCheck, 18f, Color.black);
                Restyle();
            }

            public bool Selected { get => selected; set { if (selected == value) return; selected = value; Restyle(); } }

            /// <summary>Hover help shown in the console footer.</summary>
            public string Help { set => AvHelpTip.Attach(frame.gameObject, value); }

            public override float Measure(float width) => AvGridTokens.Row;

            public override void Place(AvSlot s)
            {
                base.Place(s);
                AvLay.Place(tick.rectTransform, (s.W - 18f) * 0.5f, (s.H - 18f) * 0.5f, 18f, 18f);
            }

            public override void Restyle()
            {
                Color stroke = selected
                    ? AvStyleHost.FuiColor("select", AvTheme.Accent)
                    : AvStyleHost.FuiFill("card-bracket", AvTheme.Frame);
                frame.Stroke = selected ? 2.25f : 0.65f;
                frame.Paint(ink, stroke);
                frame.SetVerticesDirty();
                // The tick is dark ink on a bright swatch: every pen colour is light.
                tick.color = AvStyleHost.FuiColor("ground", AvTheme.Ground);
                tick.enabled = selected;
            }
        }

        /// <summary>
        /// A strip of small counters, each a key and a mono value on one line over a ten-step bar: the three
        /// personal mark budgets and the shared board's load in one 36 px line.
        /// </summary>
        private sealed class TallyPart : AvPart
        {
            private const float Height = 36f;
            private readonly AvFrame[] frames;
            private readonly TMP_Text[] keys, values;
            private readonly AvGaugeGraphic[] bars;
            private readonly AvState[] states;
            private AvSlot lastSlot;
            private bool placed;

            public TallyPart(RectTransform parent, params string[] keyTexts)
            {
                Rect = AvLay.Child(parent, "Tally");
                int n = keyTexts.Length;
                frames = new AvFrame[n]; keys = new TMP_Text[n]; values = new TMP_Text[n];
                bars = new AvGaugeGraphic[n]; states = new AvState[n];
                for (int i = 0; i < n; i++)
                {
                    RectTransform cell = AvLay.Child(Rect, "Tally " + keyTexts[i]);
                    frames[i] = AvFrame.Add(cell, "Instrument", AvChamfer.Diagonal(2f));
                    AvLay.Fill(frames[i].rectTransform);
                    frames[i].Bracket = 3f;
                    keys[i] = AvText.Make(cell, "Key", AvTextRole.Micro, keyTexts[i]);
                    values[i] = AvText.Make(cell, "Value", AvTextRole.DataStrong, "", TextAlignmentOptions.MidlineRight);
                    AvText.Fit(keys[i], false); AvText.Fit(values[i], false);
                    var go = new GameObject("Bar", typeof(RectTransform), typeof(CanvasRenderer));
                    go.transform.SetParent(cell, false);
                    bars[i] = go.AddComponent<AvGaugeGraphic>();
                    bars[i].Shape = AvGaugeShape.Segments; bars[i].Segments = 10; bars[i].SegmentGap = 1.5f; bars[i].raycastTarget = false;
                    states[i] = AvState.Info;
                }
                Restyle();
            }

            /// <summary>Hover help while the pointer is over cell <paramref name="index"/>.</summary>
            public void Help(int index, string text)
            {
                frames[index].raycastTarget = !string.IsNullOrEmpty(text);
                AvHelpTip.Attach(frames[index].gameObject, text);
            }

            public void Set(int index, string value, float fill01, AvState state)
            {
                bars[index].Value = fill01;
                bool moved = AvText.Set(values[index], value);
                if (state != states[index]) { states[index] = state; Restyle(); }
                if (moved && placed) PlaceCell(index, CellWidth(lastSlot.W));
            }

            public override float Measure(float width) => Height;

            private float CellWidth(float w) => AvFlowMath.ColumnWidth(w, frames.Length, AvGridTokens.Gap);

            public override void Place(AvSlot s)
            {
                base.Place(s);
                lastSlot = s; placed = true;
                float cw = CellWidth(s.W);
                for (int i = 0; i < frames.Length; i++) PlaceCell(i, cw);
            }

            private void PlaceCell(int i, float cw)
            {
                var cell = (RectTransform)frames[i].rectTransform.parent;
                AvLay.Place(cell, i * (cw + AvGridTokens.Gap), 0f, cw, Height);
                float vw = AvText.Width(values[i]) + 2f;
                float kw = Mathf.Min(AvText.Width(keys[i]) + 2f, Mathf.Max(0f, cw - 12f - vw - 4f));
                AvLay.Place(keys[i].rectTransform, 6f, 3f, kw, 16f);
                AvLay.Place(values[i].rectTransform, cw - 6f - vw, 2f, vw, 18f);
                AvLay.Place((RectTransform)bars[i].transform, 6f, Height - 11f, cw - 12f, 5f);
            }

            public override void Restyle()
            {
                for (int i = 0; i < frames.Length; i++)
                {
                    frames[i].Paint(AvStyleHost.FuiColor("surface-inert", AvTheme.SurfaceInert), AvStyleHost.FuiColor("hairline", AvTheme.Hairline));
                    frames[i].BracketColor = AvStyleHost.FuiColor("ink-muted", AvTheme.Dim).WithAlpha(0.5f);
                    frames[i].SetVerticesDirty();
                    keys[i].color = AvStyleHost.FuiInk("metric-key", AvTheme.RailInfo);
                    values[i].color = states[i] == AvState.Caution
                        ? AvStyleHost.FuiInk("chip caution", AvTheme.Warning)
                        : AvStyleHost.FuiInk("metric-value", AvTheme.TextPrimary);
                    bars[i].Track = AvStyleHost.FuiFill("gauge-track", AvTheme.Hairline);
                    Color fill = AvStyleHost.FuiFill("metric-fill " + AvStates.Class(states[i]), AvTheme.Accent);
                    bars[i].FillColor = fill; bars[i].FillEnd = Color.Lerp(fill, Color.white, 0.25f);
                    bars[i].SetVerticesDirty();
                }
            }
        }

        /// <summary>
        /// One ledger line of the net: a tag, who, the words (they wrap, and the line grows), a trailing mono value,
        /// and optionally a key at the far end. A column with no text gives its width back to the words. The line
        /// is the kit's list row in every other respect: state rail, hover and click, hover help.
        /// </summary>
        private sealed class NetRow : AvPart
        {
            private const float PadX = 8f, PadY = 4f, Gap = 6f, KeyW = 58f, MinH = 26f, LineH = 18f;
            private readonly AvFrame frame;
            private readonly Image rail;
            private readonly TMP_Text lead, who, text, trail;
            private readonly float leadW, whoW, trailW;
            private readonly bool tagLead;
            private AvControl key;
            private AvState state = AvState.Info;
            private bool hover;
            private AvSlot lastSlot;
            private bool placed;

            public NetRow(RectTransform parent, AvTextRole leadRole, float leadWidth, float whoWidth, float trailWidth, Action onClick = null)
            {
                leadW = leadWidth; whoW = whoWidth; trailW = trailWidth;
                tagLead = leadRole == AvTextRole.Head;   // a stencilled tag carries the tone; a plain value (an age) does not
                Rect = AvLay.Child(parent, "Net Row");
                frame = AvFrame.Add(Rect, "Frame", default(AvChamfer));
                AvLay.Fill(frame.rectTransform);
                rail = AvLay.Solid(Rect, "Rail", Color.clear);
                lead = AvText.Make(Rect, "Lead", leadRole);
                who = AvText.Make(Rect, "Who", AvTextRole.Label);
                text = AvText.Make(Rect, "Text", AvTextRole.Label, "", TextAlignmentOptions.TopLeft, true);
                trail = AvText.Make(Rect, "Trail", AvTextRole.DataStrong, "", TextAlignmentOptions.MidlineRight);
                AvText.Fit(lead, false); AvText.Fit(who, false); AvText.Fit(trail, false);
                if (onClick != null)
                {
                    AvHit hit = AvHit.On(frame);
                    hit.Hover = h => { hover = h; Restyle(); };
                    hit.Click = e => onClick();
                }
                Restyle();
            }

            /// <summary>Hover help shown in the console footer.</summary>
            public string Help { set { frame.raycastTarget = true; AvHelpTip.Attach(frame.gameObject, value); } }

            /// <summary>Adds the far-end key (MUTE); the words and the other columns make room for it.</summary>
            public AvControl AddKey(AvControl.Spec spec)
            {
                key = AvControl.Make(Rect, spec);
                return key;
            }

            public void Set(string leadText, string whoText, string words, string trailText, AvState st)
            {
                bool changed = AvText.Set(lead, leadText);
                changed |= AvText.Set(who, whoText);
                changed |= AvText.Set(text, words);
                changed |= AvText.Set(trail, trailText);
                if (st != state) { state = st; Restyle(); }
                if (!changed) return;
                // A column that appears or vanishes narrows the words, so lay out inside the slot at once.
                if (placed) Place(lastSlot);
                Changed();
            }

            private float TextWidth(float width) =>
                width - 2f * PadX - 4f
                - (lead.text.Length > 0 ? leadW + Gap : 0f)
                - (who.text.Length > 0 ? whoW + Gap : 0f)
                - (trail.text.Length > 0 ? trailW + Gap : 0f)
                - (key != null ? KeyW + Gap : 0f);

            public override float Measure(float width) =>
                Mathf.Max(MinH, 2f * PadY + AvText.Height(text, TextWidth(width)));

            public override void Place(AvSlot s)
            {
                base.Place(s);
                lastSlot = s; placed = true;
                float tw = TextWidth(s.W), th = AvText.Height(text, tw);
                bool multi = th > LineH + 2f;
                float colY = multi ? PadY : (s.H - LineH) * 0.5f;
                AvLay.Place(rail.rectTransform, 0f, 0f, 2f, s.H);
                float x = PadX + 4f;
                if (lead.text.Length > 0) { AvLay.Place(lead.rectTransform, x, colY, leadW, LineH); x += leadW + Gap; }
                if (who.text.Length > 0) { AvLay.Place(who.rectTransform, x, colY, whoW, LineH); x += whoW + Gap; }
                AvLay.Place(text.rectTransform, x, multi ? PadY : (s.H - th) * 0.5f, Mathf.Max(0f, tw), th);
                float right = s.W - PadX;
                if (key != null) { AvLay.Place(key.Rect, right - KeyW, (s.H - 24f) * 0.5f, KeyW, 24f); right -= KeyW + Gap; }
                if (trail.text.Length > 0) AvLay.Place(trail.rectTransform, right - trailW, colY, trailW, LineH);
            }

            public override void Restyle()
            {
                string st = hover ? "hover" : null;
                AvStyle r = AvStyleHost.FuiStyle("row " + AvStates.Class(state), st);
                frame.Paint(AvStyleHost.Resolve(r.Background, AvTheme.SurfaceInert),
                    r.Border.HasValue ? AvStyleHost.Resolve(r.Border, Color.clear) : Color.clear);
                rail.color = AvStyleHost.Resolve(r.Rail, AvTheme.RailInfo);
                bool plain = state == AvState.Info || state == AvState.Inert;
                lead.color = !tagLead ? AvStyleHost.FuiInk("row-sub", AvTheme.Dim)
                    : plain ? AvStyleHost.FuiInk("metric-key", AvTheme.RailInfo)
                    : AvStyleHost.FuiInk("row-value " + AvStates.Class(state), AvTheme.TextPrimary);
                who.color = AvStyleHost.FuiInk("row-sub", AvTheme.Dim);
                text.color = state == AvState.Inert ? AvStyleHost.FuiInk("row-sub", AvTheme.Dim) : AvStyleHost.FuiInk("row-name", AvTheme.TextPrimary);
                trail.color = AvStyleHost.FuiInk("row-value", AvTheme.TextPrimary);
                if (key != null) key.Restyle();
            }
        }

        /// <summary>Everything on a page except its pooled rows, from the last layout: what the rows may not take.</summary>
        private static float FixedHeight(AvFlow page, NetRow[] pool)
        {
            float h = page.ContentHeight;
            foreach (NetRow row in pool)
                if (row != null && row.PlacedShown) h -= row.PlacedHeight + AvGridTokens.Gap;
            return Mathf.Max(0f, h);
        }
    }
}
