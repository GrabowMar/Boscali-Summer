using NOAvionics;
using System;
using BoscaliSummer.Modules.Support.Domain.Cyber;
using BoscaliSummer.Modules.Support.Domain.Orbital;
using BoscaliSummer.Modules.Support.Presentation.Viz;
using BoscaliSummer.Modules.Support.Runtime;
using TMPro;
using UnityEngine;

namespace BoscaliSummer.Modules.Support.Presentation
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
            private const float HeaderH = 28f, ToggleW = 176f, BodyGap = 5f, Pad = AvGridTokens.Pad;
            private readonly TMP_Text icon, title;
            private readonly AvControl statusTab, actionsTab;
            private readonly RectTransform statusHost, actionsHost;
            private bool statusLaid, actionsLaid;
            private int sub;
            private AvState headlineState = AvState.Inert;

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

            /// <summary>The page's state word in the title slot ("ON STATION", "INFOCON 4"): the mode name the
            /// domain tab above no longer needs to repeat.</summary>
            public void SetHeadline(string text, AvState state)
            {
                string t = string.IsNullOrEmpty(text) ? "" : AvStates.Glyph(state) + text;
                if (title.text != t) title.text = t;
                if (state != headlineState) { headlineState = state; Restyle(); }
            }

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
                // Both nested pages know the visible height, so a page shorter than the viewport hands the rest
                // to its growing part (the chart, the roster, the heat grid) instead of ending in a blank band.
                float viewport = Owner != null ? Owner.ViewportHeight : 0f;
                float nested = viewport > 0f ? Mathf.Max(0f, viewport - HeaderH - BodyGap) : 0f;
                Status.ViewportHeight = nested;
                Actions.ViewportHeight = nested;
                AvLay.Place(statusHost, -Pad, bodyY - Pad, s.W + 2f * Pad, bodyH + 2f * Pad);
                AvLay.Place(actionsHost, -Pad, bodyY - Pad, s.W + 2f * Pad, bodyH + 2f * Pad);
            }

            public override void Restyle()
            {
                title.color = headlineState == AvState.Inert
                    ? AvStyleHost.Resolve(AvStyleHost.FuiStyle("section-title").Color, AvTheme.RailInfo)
                    : OpsInk.Word(headlineState);
                icon.color = AvStyleHost.Resolve(AvStyleHost.FuiStyle("section-icon").Color, AvTheme.RailInfo);
                statusTab.Restyle();
                actionsTab.Restyle();
            }
        }

        // ---- Trends: a short local history the pages draw as their growing chart ---------------------------
        // Presentation only: sampled while the OPS screen is visible, never sent, never read by gameplay.

        private const int TrendSamples = 90;
        private const float TrendEvery = 2f;

        private sealed class Trend
        {
            private readonly float[] ring = new float[TrendSamples];
            private readonly float[] linear = new float[TrendSamples];
            private int head, count;
            private float next;

            public float[] Series => linear;
            public float Last => count == 0 ? 0f : ring[(head + TrendSamples - 1) % TrendSamples];

            public bool Sample(float now, float value)
            {
                if (now < next) return false;
                next = now + TrendEvery;
                ring[head] = float.IsNaN(value) || float.IsInfinity(value) ? 0f : value;
                head = (head + 1) % TrendSamples;
                if (count < TrendSamples) count++;
                return true;
            }

            public int Fill(out float lo, out float hi)
            {
                lo = float.MaxValue; hi = float.MinValue;
                int start = (head + TrendSamples - count) % TrendSamples;
                for (int i = 0; i < count; i++)
                {
                    float v = ring[(start + i) % TrendSamples];
                    linear[i] = v;
                    if (v < lo) lo = v;
                    if (v > hi) hi = v;
                }
                if (count == 0) { lo = 0f; hi = 0f; }
                return count;
            }

            public void Clear() { head = count = 0; next = 0f; }
        }

        private readonly Trend allocationTrend = new Trend(), energyTrend = new Trend(),
            computingTrend = new Trend(), intelTrend = new Trend();

        private void ResetTrends()
        {
            allocationTrend.Clear(); energyTrend.Clear(); computingTrend.Clear(); intelTrend.Clear();
        }

        private void SampleTrends()
        {
            float now = UnityEngine.Time.unscaledTime;
            allocationTrend.Sample(now, support.LocalAllocation);
            OrbitalPlatform platform = support.LocalPlatform;
            if (platform != null && platform.Exists)
            {
                PlatformStats stats = platform.Stats(support.OrbitNow);
                energyTrend.Sample(now, stats.StorageKj > 0f ? platform.Energy / stats.StorageKj * 100f : 0f);
            }
            CyberNetwork network = support.LocalCyber;
            if (network != null && network.HasCommand)
            {
                computingTrend.Sample(now, network.Computing);
                intelTrend.Sample(now, network.Intel);
            }
        }

        /// <summary>A growing chart with its window labelled by its own numbers: floor, ceiling, latest.</summary>
        private static void PaintTrend(AvLineChart chart, Trend trend, string suffix)
        {
            if (chart == null) return;
            int n = trend.Fill(out float lo, out float hi);
            chart.SetSeries(trend.Series, n, AvNum.Compact(lo) + suffix, AvNum.Compact(hi) + suffix, AvNum.Compact(trend.Last) + suffix);
        }

        /// <summary>The page's history strip: it takes whatever height the page has left.</summary>
        private static AvLineChart AddTrend(AvFlow flow, float natural = 44f) =>
            flow.Add(new AvLineChart(flow.Content, natural), 1f);

        // ---- Named readiness totals; individual reasons remain on the action rows below. ----

        private readonly AvRow[] readySummaries = new AvRow[DomainCount];
        private readonly AvHazardBar[] readyBars = new AvHazardBar[DomainCount];
        private readonly float[][] readyBuf = new float[DomainCount][];
        private readonly int[] readyArmed = new int[DomainCount];

        private void ResetReadyStrips()
        {
            for (int i = 0; i < DomainCount; i++) { readySummaries[i] = null; readyBars[i] = null; readyBuf[i] = null; }
        }

        private void AddReadyStrip(AvFlow actions, int tab, int count)
        {
            count = Mathf.Clamp(count, 1, 24);
            readyBuf[tab] = new float[count];
            readySummaries[tab] = actions.Add(new AvRow(actions.Content));
            readyBars[tab] = actions.Add(new AvHazardBar(actions.Content, "NETWORK REQUEST"));
        }

        private void ReadyCell(int tab, int index, in AbilityFacts facts)
        {
            float[] buf = readyBuf[tab];
            if (buf == null || index < 0 || index >= buf.Length) return;
            buf[index] = facts.Enabled ? 1f : facts.Tone == AbilityTone.Pending ? 0.5f : 0f;
            if (facts.Armed) readyArmed[tab] = index;
        }

        private void ReadyBegin(int tab)
        {
            float[] buf = readyBuf[tab];
            if (buf != null) System.Array.Clear(buf, 0, buf.Length);
            readyArmed[tab] = -1;
        }

        private void ReadyEnd(int tab)
        {
            AvRow summary = readySummaries[tab];
            float[] buf = readyBuf[tab];
            if (summary == null || buf == null) return;
            int ready = 0, pending = 0;
            for (int i = 0; i < buf.Length; i++)
                if (buf[i] >= 1f) ready++; else if (buf[i] > 0f) pending++;
            summary.Set("ACTION AVAILABILITY", ready + " READY · " + pending + " PENDING · " +
                (buf.Length - ready - pending) + " BLOCKED", readyArmed[tab] >= 0 ? "ARMED" : "",
                ready > 0 ? AvState.Ready : AvState.Inert);

            float cooldown = support.LocalCooldownRemaining, total = support.LocalCooldownTotal;
            if (support.RequestPending || support.CommandPending) readyBars[tab].Set(1f, "WAIT", AvState.Info);
            else if (cooldown > 0.5f && total > 0f)
                readyBars[tab].Set(1f - cooldown / total, "T-" + Mathf.CeilToInt(cooldown) + "s", AvState.Caution);
            else readyBars[tab].Set(1f, "READY", AvState.Ready);
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
                banner.Set("▲ ARMED · " + name, "RIGHT-CLICK MAP", AvState.Caution);
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
