using NOAvionics;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Command.Presentation.MapUi
{
    internal static partial class VanillaMfdRebuild
    {
        // ------------------------------------------------------------ MIS briefing-board parts
        //
        // Module-local kit parts (AvPart subclasses) for the MIS console. Each one measures from its real
        // text at the width it is given (one Lay() shared by Measure and Place, so they cannot disagree) and
        // calls Changed() whenever a setter can alter its height.

        private static Color StateFill(AvState state) =>
            AvStyleHost.FuiFill("metric-fill " + AvStates.Class(state), AvTheme.Accent);

        private static Color StateRail(AvState state) =>
            AvStyleHost.Resolve(AvStyleHost.FuiStyle("row " + AvStates.Class(state)).Rail, AvTheme.RailInfo);

        private static AvGaugeGraphic MakeBar(RectTransform parent, string name, AvGaugeShape shape)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer));
            go.transform.SetParent(parent, false);
            AvGaugeGraphic bar = go.AddComponent<AvGaugeGraphic>();
            bar.Shape = shape;
            bar.raycastTarget = false;
            return bar;
        }

        /// <summary>
        /// The escalation ladder as one horizontal track: three gates (conventional, tactical, strategic)
        /// with a node, name, threshold and state word each. The lit track is the score's progress; every
        /// state reads as a word as well as a colour.
        /// </summary>
        private sealed class EscalationLadderPart : AvPart
        {
            private const float Pad = 12f, Node = 12f;
            private readonly AvFrame frame;
            private readonly Image track, lit;
            private readonly AvFrame[] nodes = new AvFrame[3];
            private readonly TMP_Text[] names = new TMP_Text[3], thresholds = new TMP_Text[3], words = new TMP_Text[3];
            private readonly AvState[] states = new AvState[3];
            private float fill;

            public EscalationLadderPart(RectTransform parent)
            {
                Rect = AvLay.Child(parent, "EscalationLadder");
                frame = AvFrame.Add(Rect, "Frame", AvChamfer.Diagonal(4f)); AvLay.Fill(frame.rectTransform);
                track = AvLay.Solid(Rect, "Track", Color.clear);
                lit = AvLay.Solid(Rect, "Lit", Color.clear);
                for (int i = 0; i < 3; i++)
                {
                    nodes[i] = AvFrame.Add(Rect, "Node" + i, AvChamfer.Diagonal(3f));
                    names[i] = AvText.Make(Rect, "Name" + i, AvTextRole.Micro, MfdMissionOverview.StageName(i), TextAlignmentOptions.Top, true);
                    thresholds[i] = AvText.Make(Rect, "Threshold" + i, AvTextRole.DataStrong, "—", TextAlignmentOptions.Top);
                    words[i] = AvText.Make(Rect, "Word" + i, AvTextRole.Label, "—", TextAlignmentOptions.Top);
                    states[i] = AvState.Inert;
                }
                Restyle();
            }

            public void SetRung(int rung, string threshold, string word, AvState state)
            {
                AvText.Set(thresholds[rung], threshold ?? "—");
                AvText.Set(words[rung], word ?? "—");
                if (states[rung] != state) { states[rung] = state; Restyle(); }
                Changed();
            }

            public void SetFill(float value)
            {
                float next = Mathf.Clamp01(value);
                if (Mathf.Approximately(fill, next)) return;
                fill = next;
                PlaceLit(Rect.rect.width);
            }

            private void PlaceLit(float width)
            {
                float colW = (width - 2f * Pad) / 3f;
                float x0 = Pad + colW * 0.5f, x1 = Pad + colW * 2.5f;
                AvLay.Place(lit.rectTransform, x0, 14f + Node * 0.5f - 1f, Mathf.Max(0f, (x1 - x0) * fill), 2f);
            }

            private float NamesHeight(float colW)
            {
                float h = 0f;
                for (int i = 0; i < 3; i++) h = Mathf.Max(h, AvText.Height(names[i], colW));
                return h;
            }

            public override float Measure(float width)
            {
                float colW = (width - 2f * Pad) / 3f;
                return 14f + Node + 8f + NamesHeight(colW - 6f) + 4f + 20f + 18f + 12f;
            }

            public override void Place(AvSlot s)
            {
                base.Place(s);
                float colW = (s.W - 2f * Pad) / 3f;
                float trackY = 14f + Node * 0.5f - 1f;
                float x0 = Pad + colW * 0.5f, x1 = Pad + colW * 2.5f;
                AvLay.Place(track.rectTransform, x0, trackY, x1 - x0, 2f);
                PlaceLit(s.W);
                float nameH = NamesHeight(colW - 6f);
                for (int i = 0; i < 3; i++)
                {
                    float cx = Pad + colW * (i + 0.5f);
                    AvLay.Place(nodes[i].rectTransform, cx - Node * 0.5f, 14f, Node, Node);
                    float y = 14f + Node + 8f;
                    AvLay.Place(names[i], Pad + colW * i + 3f, y, colW - 6f, AvText.Height(names[i], colW - 6f));
                    y += nameH + 4f;
                    AvLay.Place(thresholds[i], Pad + colW * i, y, colW, 20f);
                    AvLay.Place(words[i], Pad + colW * i, y + 20f, colW, 18f);
                }
            }

            public override void Restyle()
            {
                AvStyle card = AvStyleHost.FuiStyle("card inert");
                frame.Paint(AvStyleHost.Resolve(card.Background, AvTheme.SurfaceInert), AvStyleHost.Resolve(card.Border, AvTheme.Hairline));
                Color hairline = AvStyleHost.FuiFill("gauge-track", AvTheme.Hairline);
                track.color = hairline;
                AvState leading = AvState.Ready;
                for (int i = 0; i < 3; i++)
                    if (states[i] == AvState.Caution || states[i] == AvState.Danger) leading = states[i];
                lit.color = StateFill(leading);
                for (int i = 0; i < 3; i++)
                {
                    bool reached = states[i] != AvState.Inert;
                    nodes[i].Paint(reached ? StateFill(states[i]) : hairline,
                        reached ? StateFill(states[i]) : AvStyleHost.FuiBorder("frame", AvTheme.Frame));
                    names[i].color = reached ? AvStyleHost.FuiInk("row-name", AvTheme.TextPrimary) : AvStyleHost.FuiInk("row-sub", AvTheme.Dim);
                    thresholds[i].color = reached ? AvStyleHost.FuiInk("readout", AvTheme.TextPrimary) : AvStyleHost.FuiInk("row-sub", AvTheme.Dim);
                    words[i].color = states[i] == AvState.Inert
                        ? AvStyleHost.FuiInk("row-sub", AvTheme.Dim)
                        : AvStyleHost.FuiInk("row-value " + AvStates.Class(states[i]), AvTheme.TextPrimary);
                }
            }
        }

        /// <summary>
        /// A checklist line: a state icon (open circle, ticked circle, cross), the objective, a mono value on
        /// the right (distance or DONE) and a thin progress bar. The icon is a real icon-font glyph; inline
        /// glyph characters in label text do not resolve against the label font.
        /// </summary>
        private sealed class ChecklistRow : AvPart
        {
            private const float PadX = 12f, PadY = 8f, IconW = 26f, BarH = 3f;
            private readonly AvFrame frame;
            private readonly Image rail;
            private readonly TMP_Text icon, title, sub, value;
            private readonly AvGaugeGraphic bar;
            private AvState state = AvState.Inert;
            private bool hasBar;
            private float placedWidth;

            public ChecklistRow(RectTransform parent)
            {
                Rect = AvLay.Child(parent, "ChecklistRow");
                frame = AvFrame.Add(Rect, "Frame", default(AvChamfer)); AvLay.Fill(frame.rectTransform);
                rail = AvLay.Solid(Rect, "Rail", Color.clear);
                icon = AvIcons.Make(Rect, AvIcon.Circle, AvGridTokens.IconTool, Color.white);
                title = AvText.Make(Rect, "Title", AvTextRole.Head, "", TextAlignmentOptions.TopLeft, true);
                sub = AvText.Make(Rect, "Sub", AvTextRole.ProseSmall, "", TextAlignmentOptions.TopLeft, true);
                value = AvText.Make(Rect, "Value", AvTextRole.DataStrong, "", TextAlignmentOptions.TopRight);
                AvText.Fit(value, false);
                bar = MakeBar(Rect, "Bar", AvGaugeShape.Bar);
                Restyle();
            }

            /// <summary>Hover help shown in the console footer.</summary>
            public string Help { set { frame.raycastTarget = true; AvHelpTip.Attach(frame.gameObject, value); } }

            public void Set(string titleText, string subText, string valueText, float fraction, bool showBar, AvIcon glyph, AvState st)
            {
                bool layoutChanged = AvText.Set(title, titleText);
                layoutChanged |= AvText.Set(sub, subText);
                layoutChanged |= AvText.Set(value, valueText);
                layoutChanged |= hasBar != showBar;
                bar.Value = fraction;
                hasBar = showBar;
                bar.gameObject.SetActive(showBar);
                AvIcons.Set(icon, glyph, AvGridTokens.IconTool);
                if (state != st) { state = st; Restyle(); }
                if (layoutChanged)
                {
                    if (placedWidth > 0f) Arrange(placedWidth, true);
                    Changed();
                }
            }

            public override float Measure(float width) => Arrange(width, false);

            public override void Place(AvSlot s)
            {
                base.Place(s);
                placedWidth = s.W;
                AvLay.Place(rail.rectTransform, 0f, 0f, 2f, s.H);
                Arrange(s.W, true);
            }

            private float Arrange(float width, bool place)
            {
                float x = PadX + IconW, inner = Mathf.Max(1f, width - x - PadX);
                float vw = Mathf.Min(inner * .44f, AvText.Width(value) + 3f);
                float tw = Mathf.Max(1f, inner - vw - 8f);
                float top = Mathf.Max(20f, Mathf.Max(AvText.Height(title, tw), AvText.Height(value, vw)));
                float y = PadY + top + 2f;
                float sh = sub.text.Length > 0 ? AvText.Height(sub, inner) : 0f;
                if (place)
                {
                    AvLay.Place(icon.rectTransform, PadX, PadY, 18f, 18f);
                    AvLay.Place(title, x, PadY, tw, top);
                    AvLay.Place(value, width - PadX - vw, PadY, vw, top);
                    AvLay.Place(sub, x, y, inner, sh);
                    AvLay.Place(bar.rectTransform, x, y + sh + 4f, inner, BarH);
                }
                return y + sh + (hasBar ? 4f + BarH : 0f) + PadY;
            }

            public override void Restyle()
            {
                AvStyle row = AvStyleHost.FuiStyle("row " + AvStates.Class(state));
                frame.Paint(AvStyleHost.Resolve(row.Background, AvTheme.SurfaceInert), Color.clear);
                rail.color = StateRail(state);
                icon.color = state == AvState.Inert ? AvStyleHost.FuiInk("row-sub", AvTheme.Dim) : StateFill(state);
                title.color = AvStyleHost.FuiInk("row-name", AvTheme.TextPrimary);
                sub.color = AvStyleHost.FuiInk("row-sub", AvTheme.Dim);
                value.color = AvStyleHost.FuiInk("row-value " + AvStates.Class(state), AvTheme.TextPrimary);
                bar.Track = AvStyleHost.FuiFill("gauge-track", AvTheme.Hairline);
                bar.FillColor = bar.FillEnd = StateFill(state == AvState.Inert ? AvState.Info : state);
                bar.SetVerticesDirty();
            }
        }
    }
}
