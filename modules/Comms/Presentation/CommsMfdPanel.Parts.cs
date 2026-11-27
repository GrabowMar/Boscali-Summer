using NOAvionics;
using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Comms.Presentation
{
    // Layout parts for the shared map's drawing tools.
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

    }
}
