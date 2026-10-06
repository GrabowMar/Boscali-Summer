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
    /// <summary>SQUADRON (spec bezel v2 §5; WING renamed so "wing" keeps meaning the aircraft), in three sub-pages on kit v2 (the 2026-10-05
    /// Personnel File redesign): ROSTER is the squadron header and table (rank insignia, callsign, name, kills, sorties, a status tag) above a
    /// personnel file card for the selected pilot (portrait, rotated status stamp, rank ladder with XP, record, ribbon rack, the assignment
    /// line, four perk badges, AIR SAR · LOCAL SAR · RELEASE); AIRCRAFT is INSPECT given its own page (one aircraft in depth: member chips, the
    /// damage map, gauges, stores, its pilot, RTB · REFIT · RELEASE); STUDIO is the pilot studio with a live aircrew ID card. A row inspects
    /// only (R6 ruling): SUPPLY's pilot card picks who flies. <see cref="SubInspect"/> aliases <see cref="SubAircraft"/> so old callers
    /// (<c>ShowSub(SubInspect)</c>) land on AIRCRAFT. The roster is the host's; a client sees one card.</summary>
    internal sealed partial class WmcWing : IWmcPage
    {
        public const int SubRoster = 0, SubAircraft = 1, SubStudio = 2, SubInspect = SubAircraft;
        private static readonly string[] SubLabels = { "ROSTER", "AIRCRAFT", "STUDIO" };
        private static readonly AvIcon[] SubIcons = { AvIcon.UsersGroup, AvIcon.Plane, AvIcon.Pencil };
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
        private bool relayout;

        // This refresh's snapshot: Refresh takes it first, everything after reuses it.
        private WmcContext last;
        private WingService wing;
        private bool client;

        // The roster as the rows show it, rebuilt only when the roster's version moves.
        private readonly List<WingPilot> roster = new List<WingPilot>();
        private PilotStatus[] status = new PilotStatus[0];
        private int[] number = new int[0];
        private int scanVersion = int.MinValue, flying, free, sar, mia, kia, captured, lost;
        private float[] localLeft = new float[0];
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
            subTabs[SubRoster].Help = "This mission's pilots and the selected pilot's personnel file: rank, record, ribbons, perks, SAR.";
            subTabs[SubAircraft].Help = "One aircraft in depth: damage, fuel, ammo, stores, why it does what it does, and its pilot.";
            subTabs[SubStudio].Help = "The saved pilots and the pilot studio: identity, look, radio and bio.";
            for (int i = 0; i < subTabs.Length; i++) ids.Add("sq.sub." + SubLabels[i].ToLowerInvariant(), subTabs[i]);

            subPages = flow.Add(new WmcSubPages(flow, ticker, pageIndex, SubLabels.Length));
            BuildRoster(subPages.Flow(SubRoster));
            inspectPage.Build(subPages.Flow(SubAircraft), ticker, pageIndex);
            BuildStudio(subPages.Flow(SubStudio));
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
            if (k == SubAircraft && sub != SubAircraft && last != null) inspectPage.Shown(last);
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
            if (id.StartsWith("insp.", System.StringComparison.Ordinal)) ShowSub(SubAircraft);
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
            if (localLeft.Length < roster.Count) localLeft = new float[roster.Count + 8];
            flying = free = sar = mia = kia = captured = 0;
            for (int i = 0; i < roster.Count; i++)
            {
                status[i] = StatusOf(roster[i], out number[i]);
                localLeft[i] = status[i] == PilotStatus.LocalSar ? WingSearchAndRescue.LocalRecoveryRemaining(roster[i]) : -1f;
                switch (status[i])
                {
                    case PilotStatus.Free: free++; break;
                    case PilotStatus.Flying: flying++; break;
                    case PilotStatus.Inbound: break;
                    case PilotStatus.Kia: kia++; break;
                    case PilotStatus.Captured: captured++; break;
                    case PilotStatus.Missing: mia++; break;
                    default: sar++; break;
                }
            }
            lost = sar + mia + kia + captured;
            upcoming = client ? null : WingPilotRoster.Upcoming;
            // The dossier follows its pilot; a pilot who left gives way to the next up, else the first.
            if (inspected == null || !roster.Contains(inspected)) inspected = upcoming ?? (roster.Count > 0 ? roster[0] : null);
            int at = inspected != null ? roster.IndexOf(inspected) : -1;
            listPage = at >= 0 ? Pages.Of(at, PerPage) : Pages.Clamp(listPage, roster.Count, PerPage);
        }

        private PilotStatus StatusOf(WingPilot p, out int n) => WmcPilots.StatusOf(p, wing, out n);

        private WingMember MemberOf(WingPilot p) => WmcPilots.MemberOf(p, wing);

        private int IndexOf(WingPilot p) => p != null ? roster.IndexOf(p) : -1;

        /// <summary>A sub-page by its label (automation). "INSPECT" is the old name of AIRCRAFT.</summary>
        public bool ShowSubNamed(string name)
        {
            if (string.Equals(name, "INSPECT", System.StringComparison.OrdinalIgnoreCase)) name = "AIRCRAFT";
            for (int i = 0; i < SubLabels.Length; i++)
                if (string.Equals(SubLabels[i], name, System.StringComparison.OrdinalIgnoreCase))
                {
                    ShowSub(i);
                    return true;
                }
            return false;
        }

        public void Shown(WmcContext c)
        {
            Snapshot(c);
            if (sub == SubAircraft) inspectPage.Shown(c);
        }

        public void Refresh(WmcContext c)
        {
            Snapshot(c);
            if (sub == SubStudio)
            {
                RefreshAlert();
                studioPage.Refresh(c);
                return;
            }
            if (sub == SubAircraft)
            {
                RefreshAlert();
                inspectPage.Refresh(c);
                return;
            }
            // ROSTER: the table, the personnel file and what hangs on it. Review U1-U2: every refresh (the panel passes the same context
            // each time; Snapshot and each Refresh below skip their own work when nothing moved).
            RefreshRoster();
            RefreshFile();
            RefreshPerks();
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

        // ---------------------------------------------------------------- ROSTER: head, table, footer

        private WingSquadronHead head;
        private WingPilotList list;
        private WingRosterFoot foot;

        // The row's NAME column.
        private const int RowNameChars = 19;

        private void BuildRoster(AvFlow f)
        {
            rosterFlow = f;
            head = f.Add(new WingSquadronHead(f.Content));
            list = f.Add(new WingPilotList(f.Content, PerPage, PressRow, ids));
            foot = f.Add(new WingRosterFoot(f.Content, ids, TurnPage, Recruit, OpenStudio));
            BuildFile(f);
        }

        /// <summary>Rows, head, pager and footer: rebuilt when the roster, the page, the file's pilot, the next up or a local search's second moved.</summary>
        private void RefreshRoster()
        {
            int key;
            unchecked
            {
                key = scanVersion * 31 + listPage * 7 + (client ? 3 : 0) + IndexOf(inspected) * 131 + IndexOf(upcoming) * 1009;
                for (int i = 0; i < roster.Count; i++)
                    if (localLeft[i] >= 0f) key = key * 17 + (int)localLeft[i];
            }
            if (key == rowsKey) return;
            rowsKey = key;
            head.SetCounts(client ? WmcText.Unknown : SquadronWords.CountLine(roster.Count, flying, upcoming != null ? 1 : 0, sar, mia, captured, kia));
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
                v.SetIdentity(p.Rank, WmcText.Cut(p.Callsign, PilotPick.CallsignChars), WmcText.Cut(p.Name, RowNameChars), p.Kills, p.Sorties);
                v.SetState(SquadronWords.Tag(status[at], next, number[at], localLeft[at]), SquadronWords.Rail(status[at], next));
                v.SetSelected(ReferenceEquals(p, inspected));
            }
            foot.Set(listPage, Pages.Count(roster.Count, PerPage));
            foot.SetEnabled(!client, SquadronWords.ClientWhy);
            foot.Recruit.Interactable = !client;
            foot.Recruit.Help = client ? SquadronWords.ClientWhy : SquadronWords.RecruitTip;
            relayout = true;
        }

        /// <summary>A row opens its pilot's personnel file (inspect only: R6 ruling).</summary>
        private void PressRow(int index)
        {
            WingPilot p = index >= 0 && index < list.Count ? list[index].Pilot : null;
            if (p == null) return;
            Inspect(p);
        }

        /// <summary>Show this pilot's personnel file; the page follows it.</summary>
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
