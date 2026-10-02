using NOAvionics;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

using BoscaliSummer.Modules.Wing.Domain;
using BoscaliSummer.Modules.Wing.Runtime;
using BoscaliSummer.Modules.Wing.Presentation;
using BoscaliSummer.Modules.Wing.Patches;
using BoscaliSummer.Modules.Wing.Networking;
using BoscaliSummer.Modules.Wing.Configuration;
using BoscaliSummer.Core.Math;
using BoscaliSummer.Core.Util;
using BoscaliSummer.Core.Storage;
namespace BoscaliSummer.Modules.Wing.Presentation
{
    /// <summary>SQUADRON (spec bezel v2 §5; WING renamed so "wing" keeps meaning the aircraft), in two sub-pages on kit v2: ROSTER carries
    /// what used to be three tabs — the roster in join order with one status word per pilot (a badge on the row, the same word on the
    /// dossier stamp), a dossier card (portrait, rank and XP tape with rank ticks, record, radio, RELEASE), the AIRFRAME ASSIGNMENT bar
    /// with AIR SAR and LOCAL SAR, PERKS 2×2 and an inline INSPECT section (one aircraft in depth: member chips, status, fuel/hull tapes,
    /// radar, target, stores, its pilot, recent events, RTB · REFIT · RELEASE). A row inspects only (R6 ruling): SUPPLY's pilot card picks
    /// who flies. STUDIO: the saved pilots and the pilot studio (the room's SQUADRON, moved here; still the v1 page, hosted in a fixed
    /// slot until it converts). <see cref="SubInspect"/> is kept as an alias of <see cref="SubRoster"/> so old callers
    /// (<c>ShowSub(SubInspect)</c>, <c>Sub == SubInspect</c>) still land on the merged page. The roster is the host's; a client sees one
    /// card. The console body scrolls, so nothing is pinned to the floor any more.</summary>
    internal sealed partial class WmcWing : IWmcPage
    {
        /// <summary>ROSTER now also holds INSPECT (2026-09-28 merge): <see cref="SubInspect"/> aliases <see cref="SubRoster"/> so a
        /// caller that still says <c>ShowSub(SubInspect)</c> or compares <c>Sub == SubInspect</c> lands on the one merged page.</summary>
        public const int SubRoster = 0, SubStudio = 1, SubInspect = SubRoster;
        private static readonly string[] SubLabels = { "ROSTER", "STUDIO" };
        private static readonly AvIcon[] SubIcons = { AvIcon.UsersGroup, AvIcon.Pencil };
        private const int PerPage = 6;

        private readonly WmcControls ids;
        private readonly WmcStudio studioPage;
        private readonly WmcInspect inspectPage;
        private AvFlow flow, rosterFlow;
        private AvTicker ticker;
        private int pageIndex;
        private WmcSubPages subPages;
        private AvControl[] subTabs;
        private int sub = -1;
        private bool relayout, scrollPending;
        private int scrollDelay;

        // This refresh's snapshot: Refresh takes it first, everything after reuses it.
        private WmcContext last;
        private WingService wing;
        private bool client;

        // The roster as the rows show it, rebuilt only when the roster's version moves.
        private readonly List<WingPilot> roster = new List<WingPilot>();
        private PilotStatus[] status = new PilotStatus[0];
        private int[] number = new int[0];
        private int scanVersion = int.MinValue, flying, free, sar, kia, captured, lost;
        private bool scanClient;
        private WingService scanWing;
        private WingPilot upcoming;

        private WingPilot inspected;
        private int listPage;
        private string hint, alert;
        private int rowsKey = int.MinValue, alertVersion = int.MinValue;

        public WmcWing(WmcControls controls)
        {
            ids = controls;
            studioPage = new WmcStudio(controls);
            inspectPage = new WmcInspect(controls);
        }

        public WmcInspect InspectPage => inspectPage;

        public int Sub => sub;

        public string SubName => sub >= 0 && sub < SubLabels.Length ? SubLabels[sub] : "";

        public WmcStudio Studio => studioPage;

        public string Hint => sub == SubStudio ? studioPage.Hint : hint;

        public string Alert => alert;

        /// <summary>The pilot the dossier shows (R7's STUDIO › opens the studio on it).</summary>
        public WingPilot Inspected => inspected;

        public void Build(AvFlow pageFlow, AvTicker pageTicker, int index)
        {
            flow = pageFlow;
            ticker = pageTicker;
            pageIndex = index;
            var specs = new AvControl.Spec[SubLabels.Length];
            for (int i = 0; i < specs.Length; i++)
            {
                int k = i;
                specs[i] = new AvControl.Spec(SubLabels[i], () => ShowSub(k), AvButtonStyle.Default, SubIcons[i]);
            }
            subTabs = flow.Buttons(specs).Controls;
            subTabs[SubRoster].Help = "This mission's pilots, the dossier, one aircraft in depth, and SAR.";
            subTabs[SubStudio].Help = "The saved pilots and the pilot studio: identity, look, radio and bio.";
            for (int i = 0; i < subTabs.Length; i++) ids.Add("sq.sub." + SubLabels[i].ToLowerInvariant(), subTabs[i]);

            subPages = flow.Add(new WmcSubPages(flow, ticker, pageIndex, SubLabels.Length));
            BuildRoster(subPages.Flow(SubRoster));
            BuildStudio(subPages.Flow(SubStudio));
            ticker?.Add(pageIndex, AvTickRate.Fast, TickScroll);
            ShowSub(SubRoster);
        }

        /// <summary>The pilot studio (kit v2) builds into its sub-page's flow; its picker's list opens the page's popup.</summary>
        private void BuildStudio(AvFlow f)
        {
            studioPage.Build(f, ticker, pageIndex);
            // Last, so its list draws over the page.
            studioPage.UsePopup(new AvPopup(flow.Content, flow.Width), flow.Content);
        }

        /// <summary>A sub-page (the tabs, STUDIO ›, automation). Leaving STUDIO lets go of the keyboard; its draft stays.</summary>
        public void ShowSub(int k)
        {
            if (subPages == null || k < 0 || k >= SubLabels.Length) return;
            if (sub == SubStudio && k != SubStudio) studioPage.Hide();
            if (k == SubInspect && sub != SubInspect && last != null) inspectPage.Shown(last);
            sub = k;
            subPages.Show(k);
            for (int i = 0; i < subTabs.Length; i++) subTabs[i].Latched = i == k;
            AvPopup.CloseAny();
            if (last != null) Refresh(last);
        }

        /// <summary>A control of a sub-page shows that sub-page first (automation).</summary>
        public void ShowSubFor(string id)
        {
            if (id == null) return;
            if (id.StartsWith("sq.sub.", System.StringComparison.Ordinal)) return;
            if (id.StartsWith("insp.", System.StringComparison.Ordinal)) ShowSub(SubInspect);
            else if (id.StartsWith("sq.", System.StringComparison.Ordinal)) ShowSub(SubStudio);
            else if (id.StartsWith("wing.", System.StringComparison.Ordinal)) ShowSub(SubRoster);
        }

        /// <summary>The wing and the roster's statuses, once a panel refresh; the statuses are rebuilt only when the roster's version
        /// moved (every change WING shows bumps it: R6 T2/T3).</summary>
        private void Snapshot(WmcContext c)
        {
            last = c;
            client = c.Client;
            wing = client ? null : c.Wing;
            int v = WingPilotRoster.Version;
            if (v == scanVersion && client == scanClient && ReferenceEquals(wing, scanWing)) return;
            scanVersion = v;
            scanClient = client;
            scanWing = wing;
            roster.Clear();
            if (!client) WingPilotRoster.Roster(roster);
            if (status.Length < roster.Count)
            {
                status = new PilotStatus[roster.Count + 8];
                number = new int[roster.Count + 8];
            }
            flying = free = sar = kia = captured = 0;
            for (int i = 0; i < roster.Count; i++)
            {
                status[i] = StatusOf(roster[i], out number[i]);
                switch (status[i])
                {
                    case PilotStatus.Free: free++; break;
                    case PilotStatus.Flying: flying++; break;
                    case PilotStatus.Inbound: break;
                    case PilotStatus.Kia: kia++; break;
                    case PilotStatus.Captured: captured++; break;
                    default: sar++; break;
                }
            }
            lost = sar + kia + captured;
            upcoming = client ? null : WingPilotRoster.Upcoming;
            // The dossier follows its pilot; a pilot who left gives way to the next up, else the first.
            if (inspected == null || !roster.Contains(inspected)) inspected = upcoming ?? (roster.Count > 0 ? roster[0] : null);
            int at = inspected != null ? roster.IndexOf(inspected) : -1;
            listPage = at >= 0 ? Pages.Of(at, PerPage) : Pages.Clamp(listPage, roster.Count, PerPage);
        }

        private PilotStatus StatusOf(WingPilot p, out int n) => WmcPilots.StatusOf(p, wing, out n);

        private WingMember MemberOf(WingPilot p) => WmcPilots.MemberOf(p, wing);

        private int IndexOf(WingPilot p) => p != null ? roster.IndexOf(p) : -1;

        /// <summary>A sub-page by its label (automation). "INSPECT" is gone as its own page (2026-09-28 merge): it shows ROSTER and
        /// scrolls down to the inline inspect section.</summary>
        public bool ShowSubNamed(string name)
        {
            if (string.Equals(name, "INSPECT", System.StringComparison.OrdinalIgnoreCase))
            {
                ShowSub(SubRoster);
                ScrollToInspect();
                return true;
            }
            for (int i = 0; i < SubLabels.Length; i++)
                if (string.Equals(SubLabels[i], name, System.StringComparison.OrdinalIgnoreCase))
                {
                    ShowSub(i);
                    return true;
                }
            return false;
        }

        /// <summary>Scrolls the console body down to the inline INSPECT section (best-effort: <see cref="ShowSubNamed"/> on "INSPECT").
        /// The layout settles first, so it runs a couple of ticks later.</summary>
        private void ScrollToInspect()
        {
            scrollPending = true;
            scrollDelay = 2;
            rosterFlow?.RequestRelayout();
        }

        private void TickScroll()
        {
            if (!scrollPending || --scrollDelay > 0) return;
            scrollPending = false;
            RectTransform anchor = inspectPage.Anchor;
            ScrollRect scroll = flow?.Content != null ? flow.Content.GetComponentInParent<ScrollRect>() : null;
            if (anchor == null || scroll == null || scroll.viewport == null) return;
            float max = flow.Content.rect.height - scroll.viewport.rect.height;
            if (max <= 1f) return;
            float depth = -WmcKit.RectIn(flow.Content, anchor).y;
            scroll.verticalNormalizedPosition = 1f - Mathf.Clamp01(depth / max);
        }

        public void Shown(WmcContext c)
        {
            Snapshot(c);
            if (sub == SubRoster) inspectPage.Shown(c);
        }

        public void Refresh(WmcContext c)
        {
            if (sub == SubStudio)
            {
                Snapshot(c);
                RefreshAlert();
                studioPage.Refresh(c);
                return;
            }
            // ROSTER, now also INSPECT's inline section (2026-09-28 merge). Review U1-U2: every refresh (the panel passes the same
            // context each time; Snapshot and inspectPage.Refresh each skip their own work when nothing moved).
            Snapshot(c);
            RefreshRoster();
            RefreshDossier();
            RefreshPerks();
            inspectPage.Refresh(c);
            RefreshAssignment();
            RefreshAlert();
            if (relayout)
            {
                relayout = false;
                rosterFlow.RequestRelayout();
            }
        }

        /// <summary>The page's hint and alert, again only when the roster moved (STUDIO keeps it current too).</summary>
        private void RefreshAlert()
        {
            if (alertVersion == scanVersion) return;
            alertVersion = scanVersion;
            hint = SquadronWords.Hint(client, roster.Count);
            alert = null;
            for (int i = 0; i < roster.Count && alert == null; i++)
                alert = SquadronWords.Alert(status[i], WmcText.Cut(roster[i].Callsign, PilotPick.CallsignChars));
        }

        // ---------------------------------------------------------------- SQUADRON: head, rows, footer

        private AvSection headSection;
        private WingPilotList list;
        private WingRosterFoot foot;

        // The row's NAME column narrowed to make room for the kills count and the status badge.
        private const int RowNameChars = 12;

        private void BuildRoster(AvFlow f)
        {
            rosterFlow = f;
            headSection = f.Section(AvIcon.User, SquadronWords.Title);
            list = f.Add(new WingPilotList(f.Content, PerPage, PressRow, ids));
            foot = f.Add(new WingRosterFoot(f.Content, ids, TurnPage, Recruit, OpenStudio));
            BuildDossier(f);
            BuildAssignment(f);
            BuildPerks(f);
            inspectPage.Build(f, ticker, pageIndex);
        }

        /// <summary>Rows, head, pager and footer: rebuilt only when the roster, the page, the dossier's pilot or the next up changed.</summary>
        private void RefreshRoster()
        {
            int key;
            unchecked
            {
                key = scanVersion * 31 + listPage * 7 + (client ? 3 : 0) + IndexOf(inspected) * 131 + IndexOf(upcoming) * 1009;
            }
            if (key == rowsKey) return;
            rowsKey = key;
            headSection.SetCaption(client ? WmcText.Unknown : SquadronWords.Head(roster.Count - kia, flying, free, lost - kia));
            bool none = client || roster.Count == 0;
            int first = Pages.First(listPage, PerPage);
            int shown = none ? 0 : Mathf.Clamp(roster.Count - first, 0, list.Count);
            list.Show(shown, none, client ? SquadronWords.ClientWhy : SquadronWords.Empty);
            for (int i = 0; i < shown; i++)
            {
                int at = first + i;
                WingPilotRow v = list[i];
                WingPilot p = roster[at];
                bool next = ReferenceEquals(p, upcoming);
                v.Pilot = p;
                v.SetIdentity(SquadronWords.Badge(p.Rank), p.Rank, WmcText.Cut(p.Callsign, PilotPick.CallsignChars),
                    WmcText.Cut(p.Name, RowNameChars), p.Kills > 0 ? "K" + AvNum.Fixed(p.Kills, 0) : "");
                v.SetState(SquadronWords.Row(status[at], next, number[at]), SquadronWords.Rail(status[at], next));
                v.SetSelected(ReferenceEquals(p, inspected));
            }
            foot.Set(listPage, Pages.Count(roster.Count, PerPage));
            foot.SetEnabled(!client, SquadronWords.ClientWhy);
            foot.Recruit.Interactable = !client;
            foot.Recruit.Help = client ? SquadronWords.ClientWhy : SquadronWords.RecruitTip;
            relayout = true;
        }

        /// <summary>A row opens its pilot's dossier (inspect only: R6 ruling).</summary>
        private void PressRow(int index)
        {
            WingPilot p = index >= 0 && index < list.Count ? list[index].Pilot : null;
            if (p == null) return;
            Inspect(p);
        }

        /// <summary>Show this pilot in the dossier and the bar; the page follows it.</summary>
        public void Inspect(WingPilot p)
        {
            if (p == null) return;
            inspected = p;
            int at = roster.IndexOf(p);
            if (at >= 0) listPage = Pages.Of(at, PerPage);
            WmcPanel.Instance?.Refresh();
        }

        /// <summary>‹ ›: the next page, and the dossier moves to its first pilot (the dossier is never off the page).</summary>
        private void TurnPage(int dir)
        {
            if (client || roster.Count == 0) return;
            listPage = Pages.Turn(listPage, dir, roster.Count, PerPage);
            inspected = roster[Pages.First(listPage, PerPage)];
            WmcPanel.Instance?.Refresh();
        }

        /// <summary>STUDIO ›: the studio on the dossier's pilot (R7; it opened the room's SQUADRON).</summary>
        private void OpenStudio()
        {
            if (!client && inspected != null) studioPage.Focus(inspected);
            ShowSub(SubStudio);
        }

        private void Recruit()
        {
            if (client) return;
            WingPilot p = WingPilotRoster.RecruitManual();
            if (p == null) return;
            WingToast.Show(SquadronWords.Recruited(p.Callsign, p.Name));
            if (last != null) Snapshot(last);
            Inspect(p);
        }
    }
}
