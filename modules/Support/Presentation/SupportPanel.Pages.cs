using System;
using BoscaliSummer.Features.Support.Presentation.Viz;
using BoscaliSummer.Features.Support.Runtime;
using NOAvionics;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;

namespace BoscaliSummer.Features.Support.Presentation
{
    /// <summary>
    /// What every OPS page shares inside the MFD shell, on kit v2: a compact STATUS / ACTIONS toggle
    /// beside the domain icon and title (no second full-width tab row), the armed-ability banner, the
    /// chip rows, and the helpers that put an ability's help sentence on its tile and control. The
    /// hero parts, meters, tiles and the log tape live in <c>Viz/</c>.
    /// </summary>
    internal sealed partial class SupportPanel
    {
        /// <summary>
        /// Per-domain STATUS / ACTIONS switch. Kit v2 has no "page inside a page" component for
        /// this (a console's own <see cref="AvTabBar"/> would be a second full-width tab row), so this
        /// hosts two independent nested <see cref="AvFlow"/>s, built once and toggled by GameObject
        /// activity, behind a compact two-segment header. The nested flows are widened by the outer
        /// pad and shifted back, so their content lines up with the header and the tab bar above and a
        /// nested page does not lose 28 px to a second inset.
        /// </summary>
        private sealed class OpsSubPage : AvPart
        {
            private const float HeaderH = 26f, ToggleW = 176f, BodyGap = 8f, Pad = AvGridTokens.Pad;
            private readonly TMP_Text icon, title;
            private readonly AvControl statusTab, actionsTab;
            private readonly RectTransform statusHost, actionsHost;
            private bool statusLaid, actionsLaid;
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
                statusHost = AvLay.Child(Rect, "Status");
                Status = new AvFlow(statusHost, ticker, width + 2f * Pad, 0f) { Host = this };
                actionsHost = AvLay.Child(Rect, "Actions");
                Actions = new AvFlow(actionsHost, ticker, width + 2f * Pad, 0f) { Host = this };
                // PAW S1: ACTIONS first — the Tier-1 matrix must arm with zero sub-navigation.
            Select(1, null);
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
                if (Owner != null)
                {
                    (sub == 0 ? Status : Actions).Relayout();
                    Changed();
                }
                onSelect?.Invoke(sub);
            }

            public override float Measure(float width)
            {
                AvFlow active = sub == 0 ? Status : Actions;
                if (sub == 0 ? !statusLaid : !actionsLaid)
                {
                    active.Relayout();
                    if (sub == 0) statusLaid = true; else actionsLaid = true;
                }
                // The nested flow carries its own 14 px pad above and below; the header has none.
                return HeaderH + BodyGap + Mathf.Max(0f, active.ContentHeight - 2f * Pad);
            }

            public override void Place(AvSlot s)
            {
                base.Place(s);
                AvLay.Place(icon.rectTransform, 0f, 5f, 16f, 16f);
                AvLay.Place(title.rectTransform, 22f, 0f, Mathf.Max(0f, s.W - 22f - ToggleW), HeaderH);
                float half = (ToggleW - 2f) * 0.5f;
                AvLay.Place(statusTab.Rect, s.W - ToggleW, 0f, half, HeaderH);
                AvLay.Place(actionsTab.Rect, s.W - ToggleW + half + 2f, 0f, half, HeaderH);
                float bodyY = HeaderH + BodyGap, bodyH = Mathf.Max(0f, s.H - bodyY);
                AvLay.Place(statusHost, -Pad, bodyY - Pad, s.W + 2f * Pad, bodyH + 2f * Pad);
                AvLay.Place(actionsHost, -Pad, bodyY - Pad, s.W + 2f * Pad, bodyH + 2f * Pad);
            }

            public override void Restyle()
            {
                title.color = AvStyleHost.Resolve(AvStyleHost.FuiStyle("section-title").Color, AvTheme.RailInfo);
                icon.color = AvStyleHost.Resolve(AvStyleHost.FuiStyle("section-icon").Color, AvTheme.RailInfo);
                statusTab.Restyle();
                actionsTab.Restyle();
            }
        }

        /// <summary>Hover help for an ability tile: on the trailing control and the tile body, re-set only
        /// when the sentence changes (the help follows state, e.g. the readiness word).</summary>
        private static void SetTileHelp(ActionTile tile, AvControl button, string text)
        {
            if (button != null)
            {
                if (button.Help == text) return;
                button.Help = text;
            }
            tile.Help = text;
        }

        /// <summary>
        /// The armed-ability banner every ACTIONS page pins at its top: the page's own state line, or,
        /// while an ability of this domain is armed, "ARMED · name" with ABORT on the right.
        /// </summary>
        private BriefCard BuildArmedBanner(AvFlow actions)
        {
            BriefCard banner = actions.Add(new BriefCard(actions.Content));
            AvControl abort = banner.AddControl(new AvControl.Spec("ABORT", () =>
            {
                support.Disarm();
                nextRefresh = 0f;
            }, AvButtonStyle.Danger));
            abort.Help = "Disarm the armed ability. No allocation is spent.";
            return banner;
        }

        /// <summary>Paint a page's banner: armed beats the page's own state line.</summary>
        private void PaintBanner(BriefCard banner, int tab, string headline, string text, AvState tone)
        {
            SupportActionId? armed = support.ArmedAction;
            bool mine = false;
            string name = null;
            if (armed.HasValue)
            {
                foreach (SupportActionDefinition action in support.Actions)
                {
                    if (action.Id != armed.Value || HomeTab(action) != tab) continue;
                    mine = true;
                    name = action.Name;
                    break;
                }
            }
            if (mine)
            {
                banner.Set("▲ ARMED · " + name, "Right-click the map to fire. ABORT spends nothing.", AvState.Caution);
                banner.ShowControl(true);
            }
            else
            {
                banner.Set(headline, text, tone);
                banner.ShowControl(false);
            }
        }

        /// <summary>
        /// A small bank of read-only annunciators (health, resources, holdings), reusing kit v2's own
        /// <see cref="AvChip"/> (rail + word, R1 glyph) in rows of <paramref name="perRow"/>.
        /// </summary>
        private static AvChip[] BuildChipRow(AvFlow flow, string[] keys, int perRow = 4)
        {
            var chips = new AvChip[keys.Length];
            for (int i = 0; i < chips.Length; i++) chips[i] = new AvChip(flow.Content);
            int at = 0;
            while (at < chips.Length)
            {
                int n = Mathf.Min(perRow, chips.Length - at);
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
