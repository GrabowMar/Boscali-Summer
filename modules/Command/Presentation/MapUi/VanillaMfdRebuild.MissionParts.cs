using NOAvionics;
using System;
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

        private static Color StyleColor(string classes, Color fallback) =>
            AvStyleHost.FuiInk(classes, fallback);

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
        /// The mission hero card: mission name and clock, the lead objective (its type word, title,
        /// progress and nearest fix), then the briefing text. One framed block, one glance.
        /// </summary>
        private sealed class MissionHeroPart : AvPart
        {
            private const float Pad = 12f, ClockW = 104f, PctW = 64f;
            private readonly AvFrame frame;
            private readonly TMP_Text keyIcon, key, clock, name, meta, objIcon, objWord, objPct, objTitle, fixKey, fixValue, brief;
            private readonly Image ruleTop, ruleBottom;
            private readonly AvGaugeGraphic bar;
            private bool hasBrief = true, hasObjective, hasFix;
            private AvState objState = AvState.Info;

            public MissionHeroPart(RectTransform parent)
            {
                Rect = AvLay.Child(parent, "MissionHero");
                frame = AvFrame.Add(Rect, "Frame", AvChamfer.Diagonal(8f)); AvLay.Fill(frame.rectTransform);
                frame.Bracket = 8f;
                keyIcon = AvIcons.Make(Rect, AvIcon.Flag, AvGridTokens.IconHead, Color.white);
                key = AvText.Make(Rect, "Key", AvTextRole.Micro, "MISSION", TextAlignmentOptions.MidlineLeft);
                clock = AvText.Make(Rect, "Clock", AvTextRole.DataStrong, "—", TextAlignmentOptions.MidlineRight);
                name = AvText.Make(Rect, "Name", AvTextRole.Title, "LOADING MISSION", TextAlignmentOptions.TopLeft, true);
                meta = AvText.Make(Rect, "Meta", AvTextRole.DataSmall, "", TextAlignmentOptions.TopLeft, true);
                ruleTop = AvLay.Solid(Rect, "RuleTop", Color.clear);
                objIcon = AvIcons.Make(Rect, AvIcon.Target, AvGridTokens.IconHead, Color.white);
                objWord = AvText.Make(Rect, "ObjWord", AvTextRole.Head, "", TextAlignmentOptions.MidlineLeft);
                objPct = AvText.Make(Rect, "ObjPct", AvTextRole.DataStrong, "", TextAlignmentOptions.MidlineRight);
                objTitle = AvText.Make(Rect, "ObjTitle", AvTextRole.Label, "", TextAlignmentOptions.TopLeft, true);
                bar = MakeBar(Rect, "Bar", AvGaugeShape.Bar);
                fixKey = AvText.Make(Rect, "FixKey", AvTextRole.Micro, "NEAREST FIX", TextAlignmentOptions.MidlineLeft);
                fixValue = AvText.Make(Rect, "FixValue", AvTextRole.DataStrong, "", TextAlignmentOptions.MidlineRight);
                ruleBottom = AvLay.Solid(Rect, "RuleBottom", Color.clear);
                brief = AvText.Make(Rect, "Brief", AvTextRole.ProseSmall, "", TextAlignmentOptions.TopLeft, true);
                Restyle();
            }

            public void SetMission(string missionName, string modeLine, string clockText, string briefText, bool hasBriefText)
            {
                name.text = missionName ?? "";
                meta.text = modeLine ?? "";
                clock.text = clockText ?? "—";
                brief.text = briefText ?? "";
                brief.fontStyle = hasBriefText ? FontStyles.Normal : FontStyles.Italic;
                if (hasBriefText != hasBrief) { hasBrief = hasBriefText; Restyle(); }
                Changed();
            }

            /// <summary>The lead objective; a null title means there is none.</summary>
            public void SetObjective(string title, string word, AvIcon icon, float fraction, string distance, AvState state)
            {
                bool has = !string.IsNullOrEmpty(title);
                hasObjective = has;
                hasFix = has && !string.IsNullOrEmpty(distance);
                objTitle.text = has ? title : "NO ACTIVE OBJECTIVE";
                objWord.text = has ? word ?? "" : "";
                objPct.text = has ? AvNum.Percent(fraction) : "";
                AvIcons.Set(objIcon, icon, AvGridTokens.IconHead);
                bar.Value = has ? fraction : 0f;
                fixValue.text = hasFix ? distance : "";
                objState = state;
                Restyle();
                Changed();
            }

            private float Lay(float w, bool place)
            {
                float iw = w - 2f * Pad, y = 10f;
                if (place)
                {
                    AvLay.Place(keyIcon.rectTransform, Pad, y + 1f, 16f, 16f);
                    AvLay.Place(key, Pad + 22f, y, iw - 22f - ClockW, 18f);
                    AvLay.Place(clock, w - Pad - ClockW, y, ClockW, 18f);
                }
                y += 22f;
                float nh = AvText.Height(name, iw);
                if (place) AvLay.Place(name, Pad, y, iw, nh);
                y += nh + 2f;
                float mh = meta.text.Length > 0 ? AvText.Height(meta, iw) : 0f;
                if (place) AvLay.Place(meta, Pad, y, iw, mh);
                y += mh + 8f;
                if (place) AvLay.Place(ruleTop.rectTransform, Pad, y, iw, 1f);
                y += 9f;

                if (hasObjective)
                {
                    if (place)
                    {
                        AvLay.Place(objIcon.rectTransform, Pad, y + 1f, 16f, 16f);
                        AvLay.Place(objWord, Pad + 22f, y, iw - 22f - PctW, 18f);
                        AvLay.Place(objPct, w - Pad - PctW, y, PctW, 18f);
                    }
                    y += 20f;
                    float th = AvText.Height(objTitle, iw);
                    if (place) AvLay.Place(objTitle, Pad, y, iw, th);
                    y += th + 6f;
                    if (place) AvLay.Place(bar.rectTransform, Pad, y, iw, 4f);
                    y += 4f + 6f;
                    if (hasFix)
                    {
                        if (place)
                        {
                            AvLay.Place(fixKey, Pad, y, iw * 0.5f, 18f);
                            AvLay.Place(fixValue, Pad + iw * 0.5f, y, iw * 0.5f, 18f);
                        }
                        y += 20f;
                    }
                }
                else
                {
                    float th = AvText.Height(objTitle, iw);
                    if (place) AvLay.Place(objTitle, Pad, y, iw, th);
                    y += th + 4f;
                }

                float bh = brief.text.Length > 0 ? AvText.Height(brief, iw) : 0f;
                if (bh > 0f)
                {
                    y += 2f;
                    if (place) AvLay.Place(ruleBottom.rectTransform, Pad, y, iw, 1f);
                    y += 9f;
                    if (place) AvLay.Place(brief, Pad, y, iw, bh);
                    y += bh;
                }
                return y + 12f;
            }

            public override float Measure(float width) => Lay(width, false);

            public override void Place(AvSlot s)
            {
                base.Place(s);
                bar.gameObject.SetActive(hasObjective);
                objIcon.gameObject.SetActive(hasObjective);
                objWord.gameObject.SetActive(hasObjective);
                objPct.gameObject.SetActive(hasObjective);
                fixKey.gameObject.SetActive(hasFix);
                fixValue.gameObject.SetActive(hasFix);
                ruleBottom.gameObject.SetActive(brief.text.Length > 0);
                Lay(s.W, true);
            }

            public override void Restyle()
            {
                AvStyle card = AvStyleHost.FuiStyle("card raised");
                frame.Paint(AvStyleHost.Resolve(card.Background, AvTheme.SurfaceRaised), AvStyleHost.Resolve(card.Border, AvTheme.Frame));
                frame.BracketColor = AvStyleHost.FuiFill("card-bracket", AvTheme.Frame);
                frame.SetVerticesDirty();
                Color keyColor = StyleColor("metric-key", AvTheme.RailInfo);
                keyIcon.color = keyColor; key.color = keyColor; fixKey.color = keyColor;
                clock.color = StyleColor("readout", AvTheme.TextPrimary);
                name.color = StyleColor("title", AvTheme.TextPrimary);
                meta.color = StyleColor("row-sub", AvTheme.Dim);
                Color hairline = AvStyleHost.FuiBorder("section", AvTheme.Hairline);
                ruleTop.color = hairline; ruleBottom.color = hairline;
                Color word = hasObjective ? StateFill(objState) : StyleColor("row-sub", AvTheme.Dim);
                objIcon.color = word; objWord.color = word;
                objPct.color = StyleColor("row-value " + AvStates.Class(objState), AvTheme.TextPrimary);
                objTitle.color = hasObjective ? StyleColor("row-name", AvTheme.TextPrimary) : StyleColor("row-sub", AvTheme.Dim);
                fixValue.color = StyleColor("row-value info", AvTheme.TextPrimary);
                bar.Track = AvStyleHost.FuiFill("gauge-track", AvTheme.Hairline);
                bar.FillColor = bar.FillEnd = StateFill(objState);
                bar.SetVerticesDirty();
                brief.color = hasBrief ? StyleColor("row-sub", AvTheme.Dim) : StyleColor("section-caption", AvTheme.Disabled);
            }
        }

        /// <summary>
        /// The objectives tally: how many issued objectives are done, as a big mono fraction over one
        /// segment per objective, with the nearest objective fix on the right.
        /// </summary>
        private sealed class ObjectiveTallyPart : AvPart
        {
            private const float Pad = 12f, SideW = 140f;
            private readonly AvFrame frame;
            private readonly TMP_Text value, caption, sideKey, sideValue;
            private readonly AvGaugeGraphic segments;
            private AvState state = AvState.Info;

            public ObjectiveTallyPart(RectTransform parent)
            {
                Rect = AvLay.Child(parent, "ObjectiveTally");
                frame = AvFrame.Add(Rect, "Frame", AvChamfer.Diagonal(8f)); AvLay.Fill(frame.rectTransform);
                frame.Bracket = 8f;
                value = AvText.Make(Rect, "Value", AvTextRole.Display, "0/0", TextAlignmentOptions.MidlineLeft);
                caption = AvText.Make(Rect, "Caption", AvTextRole.Micro, "DONE", TextAlignmentOptions.MidlineLeft);
                sideKey = AvText.Make(Rect, "SideKey", AvTextRole.Micro, "NEAREST FIX", TextAlignmentOptions.MidlineRight);
                sideValue = AvText.Make(Rect, "SideValue", AvTextRole.DataStrong, "—", TextAlignmentOptions.MidlineRight);
                segments = MakeBar(Rect, "Segments", AvGaugeShape.Segments);
                segments.SegmentGap = 3f;
                Restyle();
            }

            public void Set(int done, int total, string nearest, AvState st)
            {
                value.text = AvNum.Fixed(done, 0) + "/" + AvNum.Fixed(total, 0);
                sideValue.text = string.IsNullOrEmpty(nearest) ? "—" : nearest;
                segments.Segments = Mathf.Clamp(total, 1, 16);
                segments.Value = total > 0 ? done / (float)total : 0f;
                segments.SetVerticesDirty();
                if (st != state) { state = st; Restyle(); }
            }

            public override float Measure(float width) => 84f;

            public override void Place(AvSlot s)
            {
                base.Place(s);
                AvLay.Place(value, Pad, 8f, s.W - 2f * Pad - SideW, 32f);
                AvLay.Place(caption, Pad, 40f, s.W - 2f * Pad - SideW, 16f);
                AvLay.Place(sideKey, s.W - Pad - SideW, 12f, SideW, 16f);
                AvLay.Place(sideValue, s.W - Pad - SideW, 28f, SideW, 20f);
                AvLay.Place(segments.rectTransform, Pad, 62f, s.W - 2f * Pad, 8f);
            }

            public override void Restyle()
            {
                AvStyle card = AvStyleHost.FuiStyle("card");
                frame.Paint(AvStyleHost.Resolve(card.Background, AvTheme.Surface), AvStyleHost.Resolve(card.Border, AvTheme.Hairline));
                frame.BracketColor = AvStyleHost.FuiFill("card-bracket", AvTheme.Frame);
                frame.SetVerticesDirty();
                value.color = StyleColor("readout", AvTheme.TextPrimary);
                caption.color = StyleColor("readout-unit", AvTheme.Dim);
                sideKey.color = StyleColor("metric-key", AvTheme.RailInfo);
                sideValue.color = StyleColor("row-value info", AvTheme.TextPrimary);
                segments.Track = AvStyleHost.FuiFill("gauge-track", AvTheme.Hairline);
                segments.FillColor = segments.FillEnd = StateFill(state);
                segments.SetVerticesDirty();
            }
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
                frame = AvFrame.Add(Rect, "Frame", AvChamfer.Diagonal(8f)); AvLay.Fill(frame.rectTransform);
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
                thresholds[rung].text = threshold ?? "—";
                words[rung].text = AvStates.Glyph(state) + (word ?? "—");
                if (states[rung] != state) { states[rung] = state; Restyle(); }
                Changed();
            }

            public void SetFill(float value)
            {
                fill = Mathf.Clamp01(value);
                Restyle();
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
                    names[i].color = reached ? StyleColor("row-name", AvTheme.TextPrimary) : StyleColor("row-sub", AvTheme.Dim);
                    thresholds[i].color = reached ? StyleColor("readout", AvTheme.TextPrimary) : StyleColor("row-sub", AvTheme.Dim);
                    words[i].color = states[i] == AvState.Inert
                        ? StyleColor("section-caption", AvTheme.Disabled)
                        : StyleColor("row-value " + AvStates.Class(states[i]), AvTheme.TextPrimary);
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
            private const float PadX = 12f, PadY = 6f, IconW = 26f, ValueW = 92f, BarH = 5f;
            private readonly AvFrame frame;
            private readonly Image rail;
            private readonly TMP_Text icon, title, sub, value;
            private readonly AvGaugeGraphic bar;
            private AvState state = AvState.Inert;
            private bool hasBar;

            public ChecklistRow(RectTransform parent)
            {
                Rect = AvLay.Child(parent, "ChecklistRow");
                frame = AvFrame.Add(Rect, "Frame", default(AvChamfer)); AvLay.Fill(frame.rectTransform);
                rail = AvLay.Solid(Rect, "Rail", Color.clear);
                icon = AvIcons.Make(Rect, AvIcon.Circle, AvGridTokens.IconTool, Color.white);
                title = AvText.Make(Rect, "Title", AvTextRole.Label, "", TextAlignmentOptions.TopLeft, true);
                sub = AvText.Make(Rect, "Sub", AvTextRole.ProseSmall, "", TextAlignmentOptions.TopLeft, true);
                value = AvText.Make(Rect, "Value", AvTextRole.DataStrong, "", TextAlignmentOptions.TopRight);
                AvText.Fit(value, false);
                bar = MakeBar(Rect, "Bar", AvGaugeShape.Bar);
                Restyle();
            }

            /// <summary>Hover help shown in the console footer.</summary>
            public string Help { set { frame.raycastTarget = true; AvHelpTip.Attach(frame.gameObject, value); } }

            public void Set(string titleText, string subText, string valueText, float fraction, bool showBar, AvState st)
            {
                title.text = titleText ?? "";
                sub.text = subText ?? "";
                value.text = valueText ?? "";
                bar.Value = fraction;
                hasBar = showBar;
                bar.gameObject.SetActive(showBar);
                AvIcons.Set(icon, st == AvState.Ready ? AvIcon.CircleCheck : st == AvState.Danger ? AvIcon.X : AvIcon.Circle,
                    AvGridTokens.IconTool);
                state = st;
                Restyle();
                Changed();
            }

            private float TextWidth(float w) => w - PadX - IconW - ValueW - 8f;

            public override float Measure(float width)
            {
                float tw = TextWidth(width);
                float h = PadY + AvText.Height(title, tw) + (sub.text.Length > 0 ? 2f + AvText.Height(sub, tw) : 0f) + PadY;
                if (hasBar) h += BarH + 2f;
                return Mathf.Max(AvGridTokens.Row + 4f, h);
            }

            public override void Place(AvSlot s)
            {
                base.Place(s);
                float tw = TextWidth(s.W), th = AvText.Height(title, tw);
                AvLay.Place(rail.rectTransform, 0f, 0f, 2f, s.H);
                AvLay.Place(icon.rectTransform, PadX, PadY - 1f, 20f, 20f);
                AvLay.Place(title, PadX + IconW, PadY, tw, th);
                AvLay.Place(sub, PadX + IconW, PadY + th + 2f, tw, sub.text.Length > 0 ? AvText.Height(sub, tw) : 0f);
                AvLay.Place(value, s.W - PadX - ValueW, PadY, ValueW, 18f);
                AvLay.Place(bar.rectTransform, PadX + IconW, s.H - BarH - 5f, s.W - PadX - IconW - PadX, BarH);
            }

            public override void Restyle()
            {
                AvStyle row = AvStyleHost.FuiStyle("row " + AvStates.Class(state));
                frame.Paint(AvStyleHost.Resolve(row.Background, AvTheme.SurfaceInert), Color.clear);
                rail.color = StateRail(state);
                icon.color = state == AvState.Inert ? StyleColor("row-sub", AvTheme.Dim) : StateFill(state);
                title.color = StyleColor("row-name", AvTheme.TextPrimary);
                sub.color = StyleColor("row-sub", AvTheme.Dim);
                value.color = StyleColor("row-value " + AvStates.Class(state), AvTheme.TextPrimary);
                bar.Track = AvStyleHost.FuiFill("gauge-track", AvTheme.Hairline);
                bar.FillColor = bar.FillEnd = StateFill(state == AvState.Inert ? AvState.Info : state);
                bar.SetVerticesDirty();
            }
        }
    }
}
