using System;
using NOAvionics;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;

namespace BoscaliSummer.Features.Support.Presentation
{
    /// <summary>
    /// What every OPS page shares inside the MFD shell, on kit v2 (spec §9.2): a compact
    /// STATUS / ACTIONS toggle beside the domain icon and title instead of a second full-width
    /// tab row, one always-visible hint/armed line (status is a word, never colour alone), and a
    /// small newest-first log bound to the caller-owned ring buffer (SPACE/CYBER/SPEC OPS loops,
    /// which the OPS window rooms also take by reference — the array identity must not change).
    /// </summary>
    internal sealed partial class SupportPanel
    {
        /// <summary>
        /// Per-domain STATUS / ACTIONS switch. Kit v2 has no "page inside a page" component for
        /// this (a console's own <see cref="AvTabBar"/> would be a second full-width tab row,
        /// which spec §9.2 forbids), so this hosts two independent nested <see cref="AvFlow"/>s —
        /// built once, toggled by GameObject activity — behind a compact two-segment header. Noted
        /// as a kit gap in the slice report.
        /// </summary>
        private sealed class OpsSubPage : AvPart
        {
            private const float HeaderH = 26f, ToggleW = 176f;
            private readonly TMP_Text icon, title;
            private readonly AvControl statusTab, actionsTab;
            private readonly RectTransform statusHost, actionsHost;
            private int sub;

            public AvFlow Status { get; }
            public AvFlow Actions { get; }

            public OpsSubPage(RectTransform parent, AvTicker ticker, float width, AvIcon domainIcon, string titleText,
                System.Action<int> onSelect, string statusHelp = null, string actionsHelp = null)
            {
                Rect = AvLay.Child(parent, "SubPage " + titleText);
                icon = AvIcons.Make(Rect, domainIcon, AvGridTokens.IconHead, Color.white);
                title = AvText.Make(Rect, "Title", AvTextRole.Head, titleText);
                AvText.Fit(title, false);
                statusTab = AvControl.Make(Rect, new AvControl.Spec("STATUS", () => Select(0, onSelect), AvButtonStyle.Default, AvIcon.InfoCircle), "tab");
                actionsTab = AvControl.Make(Rect, new AvControl.Spec("ACTIONS", () => Select(1, onSelect), AvButtonStyle.Default, AvIcon.Bolt), "tab");
                statusTab.Help = statusHelp;
                actionsTab.Help = actionsHelp;
                statusHost = AvLay.Child(Rect, "Status"); Status = new AvFlow(statusHost, ticker, width, 0f);
                actionsHost = AvLay.Child(Rect, "Actions"); Actions = new AvFlow(actionsHost, ticker, width, 0f);
                Select(0, null);
                Restyle();
            }

            public int Sub => sub;

            private void Select(int index, System.Action<int> onSelect)
            {
                sub = Mathf.Clamp(index, 0, 1);
                statusTab.Latched = sub == 0;
                actionsTab.Latched = sub == 1;
                statusHost.gameObject.SetActive(sub == 0);
                actionsHost.gameObject.SetActive(sub == 1);
                onSelect?.Invoke(sub);
            }

            public override float Measure(float width)
            {
                AvFlow active = sub == 0 ? Status : Actions;
                active.Relayout();
                return HeaderH + active.ContentHeight;
            }

            public override void Place(AvSlot s)
            {
                base.Place(s);
                AvLay.Place(icon.rectTransform, 0f, 5f, 16f, 16f);
                AvLay.Place(title.rectTransform, 22f, 0f, Mathf.Max(0f, s.W - 22f - ToggleW), HeaderH);
                float half = (ToggleW - 2f) * 0.5f;
                AvLay.Place(statusTab.Rect, s.W - ToggleW, 0f, half, HeaderH);
                AvLay.Place(actionsTab.Rect, s.W - ToggleW + half + 2f, 0f, half, HeaderH);
                float bodyY = HeaderH + 4f, bodyH = Mathf.Max(0f, s.H - bodyY);
                AvLay.Place(statusHost, 0f, bodyY, s.W, bodyH);
                AvLay.Place(actionsHost, 0f, bodyY, s.W, bodyH);
            }

            public override void Restyle()
            {
                title.color = AvStyleHost.Resolve(AvStyleHost.FuiStyle("section-title").Color, AvTheme.RailInfo);
                icon.color = AvStyleHost.Resolve(AvStyleHost.FuiStyle("section-icon").Color, AvTheme.RailInfo);
                statusTab.Restyle();
                actionsTab.Restyle();
            }
        }

        /// <summary>
        /// One always-visible status/hint line: the page's normal hint, or (when an ability of
        /// this domain is armed) "ARMED · name · right-click the map". Fixed height regardless of
        /// text so switching between the two never reflows the page around it (rows stay put).
        /// </summary>
        private sealed class HintLine : AvPart
        {
            private readonly TMP_Text text;
            private AvState state = AvState.Info;

            public HintLine(RectTransform parent)
            {
                Rect = AvLay.Child(parent, "Hint");
                text = AvText.Make(Rect, "Text", AvTextRole.Label, "", TextAlignmentOptions.MidlineLeft, true);
                Restyle();
            }

            public void Set(string value, AvState s)
            {
                string composed = AvStates.Glyph(s) + (value ?? "");
                if (text.text == composed && state == s) return;
                text.text = composed;
                state = s;
                Restyle();
            }

            public override float Measure(float width) => Mathf.Max(20f, AvText.Height(text, width));
            public override void Place(AvSlot s) { base.Place(s); AvLay.Fill(text.rectTransform); }

            public override void Restyle() =>
                text.color = state == AvState.Inert
                    ? AvStyleHost.Resolve(AvStyleHost.FuiStyle("row-sub").Color, AvTheme.Dim)
                    : AvStyleHost.Resolve(AvStyleHost.FuiStyle("chip " + AvStates.Class(state)).Color, AvTheme.Dim);
        }

        /// <summary>
        /// A small fixed-height newest-first log bound to a caller-owned ring buffer (the
        /// SPACE/CYBER/SPEC OPS loops the OPS window rooms take by array reference); written only
        /// when a line actually changes.
        /// </summary>
        private sealed class LogLines : AvPart
        {
            private readonly TMP_Text[] lines;
            private readonly string[] shown;

            public LogLines(RectTransform parent, int count)
            {
                Rect = AvLay.Child(parent, "Log");
                lines = new TMP_Text[count];
                shown = new string[count];
                for (int i = 0; i < count; i++)
                    lines[i] = AvText.Make(Rect, "Line" + i, AvTextRole.ProseSmall, "", TextAlignmentOptions.TopLeft, true);
                Restyle();
            }

            public void Write(string[] source)
            {
                for (int i = 0; i < lines.Length; i++)
                {
                    string v = source != null && i < source.Length ? source[i] : null;
                    if (shown[i] == v) continue;
                    shown[i] = v;
                    lines[i].text = v ?? "";
                }
            }

            public override float Measure(float width) => lines.Length * 17f;

            public override void Place(AvSlot s)
            {
                base.Place(s);
                for (int i = 0; i < lines.Length; i++)
                    AvLay.Place(lines[i].rectTransform, 0f, i * 17f, s.W, 17f);
            }

            public override void Restyle()
            {
                for (int i = 0; i < lines.Length; i++)
                    lines[i].color = i == 0
                        ? AvStyleHost.Resolve(AvStyleHost.FuiStyle("row-name").Color, AvTheme.TextPrimary)
                        : AvStyleHost.Resolve(AvStyleHost.FuiStyle("row-sub").Color, AvTheme.Dim);
            }
        }

        /// <summary>Hover help for an ability row: on the trailing control and the row body, re-set only
        /// when the sentence changes (the help follows state, e.g. the readiness word).</summary>
        private static void SetRowHelp(AvRow row, AvControl button, string text)
        {
            if (button != null)
            {
                if (button.Help == text) return;
                button.Help = text;
            }
            row.Help = text;
        }

        /// <summary>Monospaced text for the terminal-flavoured CYBER shell log.</summary>
        private static string Mono(string text) => string.IsNullOrEmpty(text) ? "" : "<mspace=0.6em>" + text;

        /// <summary>A wrapped paragraph (briefing copy, advice text) that grows the flow rather than
        /// clipping. Sentence case is the caller's, per spec §5.2 (prose keeps its authored case).</summary>
        private sealed class NoteText : AvPart
        {
            private readonly TMP_Text text;
            private AvState state = AvState.Inert;

            public NoteText(RectTransform parent, AvTextRole role = AvTextRole.Prose)
            {
                Rect = AvLay.Child(parent, "Note");
                text = AvText.Make(Rect, "Text", role, "", TextAlignmentOptions.TopLeft, true);
                Restyle();
            }

            public void Set(string value, AvState s = AvState.Inert)
            {
                string v = value ?? "";
                if (text.text == v && state == s) return;
                text.text = v;
                state = s;
                Restyle();
            }

            public override float Measure(float width) => text.text.Length == 0 ? 0f : AvText.Height(text, width);
            public override void Place(AvSlot s) { base.Place(s); AvLay.Fill(text.rectTransform); }

            public override void Restyle() =>
                text.color = state == AvState.Inert
                    ? AvStyleHost.Resolve(AvStyleHost.FuiStyle("row-sub").Color, AvTheme.Dim)
                    : AvStyleHost.Resolve(AvStyleHost.FuiStyle("chip " + AvStates.Class(state)).Color, AvTheme.Dim);
        }

        /// <summary>
        /// A small bank of read-only annunciators (health, resources, holdings), reusing kit v2's
        /// own <see cref="AvChip"/> (rail + word, R1 glyph) in rows of up to four rather than a
        /// bespoke tile widget.
        /// </summary>
        private static AvChip[] BuildChipRow(AvFlow flow, string[] keys)
        {
            var chips = new AvChip[keys.Length];
            for (int i = 0; i < chips.Length; i++) chips[i] = new AvChip(flow.Content);
            int at = 0;
            while (at < chips.Length)
            {
                int n = Mathf.Min(4, chips.Length - at);
                var line = new AvPart[n];
                for (int k = 0; k < n; k++) line[k] = chips[at + k];
                flow.Row(line);
                at += n;
            }
            return chips;
        }

        private static void SetChip(AvChip chip, string key, string word, AvState state) =>
            chip.Set(key + " " + word, state);
    }
}
