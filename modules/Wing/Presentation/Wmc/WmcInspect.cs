using NOAvionics;
using UnityEngine;

using BoscaliSummer.Modules.Wing.Domain;
using BoscaliSummer.Modules.Wing.Runtime;
using BoscaliSummer.Modules.Wing.Networking;
namespace BoscaliSummer.Modules.Wing.Presentation
{
    /// <summary>WING's AIRCRAFT sub-page (spec bezel v2 §5; the 2026-10-05 redesign): one aircraft in depth — a chip per member and CENTER, a
    /// card with FUEL (with bingo), AMMO and HULL gauges, its stores by station, its pilot (DOSSIER › opens ROSTER on them) and RELEASE for
    /// that aircraft only. Inspecting never changes who orders go to (review R1 I1): RELEASE acts on the inspected aircraft. An aircraft
    /// that leaves the wing stays named until a chip picks another (the page says it is gone). <see cref="Build"/> adds its parts to that
    /// sub-page's flow. Only WING uses it.</summary>
    internal sealed class WmcInspect
    {
        private const int MaxChips = WcSnapshot.MaxMembers;

        private readonly uint[] chipIds = new uint[MaxChips];
        private readonly ConfirmGate releaseGate = new ConfirmGate();
        private MemberDetail detail = new MemberDetail { Stores = new StoreLine[8] };
        private readonly AircraftFace face = new AircraftFace();
        private AvFlow outer;
        private AvSection storesSection;
        private InspectChips chips;
        private WmcSubPages pages;
        private WmcLines empty;
        private WingAircraftCard card;
        private WingStoresList storesList;
        private WingPilotLine pilotLine;
        private AvControl release;
        private int gate = -1;
        private int key = int.MinValue, chipsKey = int.MinValue;
        private uint inspected;
        private WingPilot pilot;
        private WmcContext last;

        /// <summary>Adds the AIRCRAFT page to <paramref name="flow"/> (WING's AIRCRAFT sub-page).</summary>
        public void Build(AvFlow flow, AvTicker ticker, int pageIndex)
        {
            outer = flow;
            chips = flow.Add(new InspectChips(flow.Content, MaxChips, PickChip, Center));
            pages = flow.Add(new WmcSubPages(flow, ticker, pageIndex, 2));

            AvFlow e = pages.Flow(0);
            empty = e.Add(new WmcLines(e.Content, 1, AvTextRole.ProseSmall, "hint"));

            AvFlow d = pages.Flow(1);
            card = d.Add(new WingAircraftCard(d.Content));
            storesSection = d.Section(AvIcon.Stack2, "STORES");
            storesList = d.Add(new WingStoresList(d.Content));
            pilotLine = d.Add(new WingPilotLine(d.Content, OpenDossier));
            pilotLine.Dossier.Help = "This pilot's personnel file on ROSTER.";

            release = d.Buttons(new AvControl.Spec("RELEASE", Release, AvButtonStyle.Danger, AvIcon.Unlink)).Controls[0];
            release.Help = "This aircraft leaves the wing to the game's AI (press twice).";
            Gate(0);
        }

        private void Gate(int g)
        {
            if (gate == g) return;
            gate = g;
            pages.Show(g);
        }

        public void Shown()
        {
            key = chipsKey = int.MinValue;
        }

        public void Refresh(WmcContext c)
        {
            last = c;
            // The first look picks the selected member (else the first); after that the page keeps its aircraft, gone or not.
            if (inspected == 0u) inspected = c.Selection.Single != 0u ? c.Selection.Single : c.Count > 0 ? c.Rows[0].Id : 0u;
            bool present = inspected != 0u && WingRows.IndexOf(c.Rows, c.Count, inspected) >= 0;
            RefreshChips(c);
            WingMember m = c.Client || !present ? null : c.MemberOf(inspected);
            bool show = m != null && (object)m.Aircraft != null;
            Gate(show ? 1 : 0);
            chips.Center.Interactable = present;
            if (!show)
            {
                if (empty.Set(0, c.Client || present || inspected == 0u ? InspectWords.Empty(c.Client) : InspectWords.Lost)) outer.RequestRelayout();
                return;
            }
            RefreshDetail(c, m);
            RefreshFoot(c);
        }

        private void RefreshChips(WmcContext c)
        {
            int k = c.Count;
            for (int i = 0; i < c.Count; i++) k = k * 31 + c.Rows[i].Slot + (int)(c.Rows[i].Id % 997u);
            k = k * 7 + (int)(inspected % 1009u);
            if (k == chipsKey) return;
            chipsKey = k;
            for (int i = 0; i < MaxChips; i++)
            {
                bool on = i < c.Count;
                AvControl chip = chips[i];
                if (chip.gameObject.activeSelf != on) chip.gameObject.SetActive(on);
                chipIds[i] = on ? c.Rows[i].Id : 0u;
                if (!on) continue;
                chip.Label = InspectWords.Chip(c.Rows[i].Slot);
                chip.Latched = c.Rows[i].Id == inspected;
                chip.Help = "Show " + WingRows.Number(c.Rows[i].Slot) + ".";
            }
        }

        private void RefreshDetail(WmcContext c, WingMember m)
        {
            WmcDetail.Gather(m, ref detail);
            int row = WingRows.IndexOf(c.Rows, c.Count, inspected);
            int e = c.Wing.ElementOf(m);
            string name = c.Wing.Roster.Name(e);
            unchecked
            {
                // The pilot line reads XP, kills and the portrait: the roster's versions move with each.
                int k = (int)inspected * 7 + R(detail.Fuel * 100f) * 31 + R(detail.Ammo * 100f) * 131 + R(detail.Damage * 100f) * 17
                    + R(detail.BingoSeconds / 10f) + m.Seat * 7919 + e * 104729
                    + (row >= 0 ? c.Rows[row].Duty * 13 + c.Rows[row].Flags * 19 + c.Rows[row].Behaviour * 23 : 0) + detail.StoreCount * 41
                    + (name != null ? name.GetHashCode() * 29 : 0)
                    + (m.Damage.Damaged ? 71 : 0) + WingPilotRoster.Version * 61 + WingPilotRoster.LookVersion * 67;
                for (int i = 0; i < detail.StoreCount && i < detail.Stores.Length; i++) k = k * 3 + detail.Stores[i].Ammo;
                if (k == key) return;
                key = k;
            }
            Aircraft a = m.Aircraft;
            string type = a.definition != null && !string.IsNullOrEmpty(a.definition.code) ? a.definition.code : WmcText.Unknown;
            string state = row >= 0 ? WingRows.State(c.Rows[row]) : WmcText.Unknown;
            string letter = ElementRoster.Letter(e);
            string b = WingHudText.BingoTime(detail.BingoSeconds);
            float hullFrac = float.IsNaN(detail.Damage) ? float.NaN : 1f - detail.Damage;
            int loaded = 0;
            for (int i = 0; i < detail.StoreCount && i < detail.Stores.Length; i++)
                if (detail.Stores[i].Ammo > 0) loaded++;
            face.Title = InspectWords.Title(m.Seat, type);
            face.Sub = state + " · ELEMENT " + (string.IsNullOrEmpty(name) || name == letter ? letter : letter + " " + name);
            face.Fuel = detail.Fuel;
            face.FuelText = WmcText.Percent(detail.Fuel);
            face.FuelSub = b.Length > 0 ? b : "NO BINGO CALL";
            face.FuelRail = LevelClass(WmcStyle.Level(detail.Fuel));
            face.Ammo = detail.Ammo;
            face.AmmoText = WmcText.Percent(detail.Ammo);
            face.AmmoSub = detail.StoreCount > 0 ? AvNum.Fixed(loaded, 0) + " OF " + AvNum.Fixed(detail.StoreCount, 0) + " STATIONS LOADED" : "NO STORES";
            face.AmmoRail = LevelClass(WmcStyle.Level(detail.Ammo));
            face.Hull = hullFrac;
            face.HullText = WmcText.Percent(hullFrac);
            face.HullSub = m.Damage.Damaged ? "DAMAGED" : "NO DAMAGE";
            face.HullRail = LevelClass(WmcStyle.Level(hullFrac));
            card.Show(face);
            storesSection.SetCaption(AvNum.Fixed(detail.StoreCount, 0) + (detail.StoreCount == 1 ? " STATION" : " STATIONS"));
            storesList.Show(detail);
            pilot = WingPilotRoster.Of(a);
            pilotLine.SetPilot(pilot, pilot != null ? "\"" + WmcText.Cut(pilot.Callsign, PilotPick.CallsignChars) + "\"" : "NO PILOT RECORD",
                pilot != null ? WingPilotRoster.RankName(pilot.Rank) + " · XP " + AvNum.Fixed(pilot.Xp, 0) + " · " + AvNum.Fixed(pilot.Kills, 0)
                    + (pilot.Kills == 1 ? " KILL" : " KILLS") : "");
            pilotLine.Dossier.Interactable = pilot != null;
            outer.RequestRelayout();
        }

        private void RefreshFoot(WmcContext c)
        {
            if (inspected != footFor)
            {
                footFor = inspected;
                footId = AvNum.Fixed(inspected, 0);
            }
            bool asking = releaseGate.IsArmed(footId, Time.unscaledTime);
            if (asking != releaseAsking)
            {
                releaseAsking = asking;
                release.Label = asking ? "RELEASE?" : "RELEASE";
            }
            release.Interactable = c.CanOrder;
        }

        private uint footFor;
        private string footId = "";
        private bool releaseAsking;

        private void PickChip(int i)
        {
            if (chipIds[i] == 0u) return;
            inspected = chipIds[i];
            key = int.MinValue;
            WmcPanel.Instance?.Refresh();
        }

        private void Center()
        {
            Unit u = WmcContext.UnitOf(inspected);
            if (u != null) WmcMap.Center(u);
        }

        /// <summary>SQUADRON's ROSTER on this pilot (review U3-U4: it could land on STUDIO, and before SQUADRON had ever been shown the
        /// empty roster made it open on the next free pilot instead).</summary>
        private void OpenDossier()
        {
            WmcPanel panel = WmcPanel.Instance;
            if (pilot == null || panel == null || panel.WingPage == null) return;
            panel.Show(WmcTabs.Wing);
            panel.WingPage.ShowSub(WmcWing.SubRoster);
            panel.Refresh();
            panel.WingPage.Inspect(pilot);
            panel.Refresh();
        }

        /// <summary>RELEASE, pressed twice: the first press asks, the second hands this aircraft to the game's AI.</summary>
        private void Release()
        {
            if (last == null || inspected == 0u) return;
            uint id = inspected;
            WmcUi.Order(last, () =>
            {
                if (!releaseGate.Press(AvNum.Fixed(id, 0), Time.unscaledTime))
                {
                    WingToast.Show("Release " + WingRows.Number(Seat()) + " to the game's AI? Press RELEASE again");
                    return;
                }
                WingOrders.Run(WingOrder.Of(OrderKind.Release, WingScope.OfMembers(id)));
            });
            WmcPanel.Instance?.Refresh();
        }

        private int Seat()
        {
            int i = last != null ? WingRows.IndexOf(last.Rows, last.Count, inspected) : -1;
            return i >= 0 ? last.Rows[i].Slot : 0;
        }

        /// <summary>A tape's level word (ok, warn, bad, none) as the state class the rail colours read.</summary>
        private static string LevelClass(string level) => level == "bad" ? "danger" : level == "warn" ? "warn" : level == "ok" ? "live" : "inert";

        private static int R(float v) => float.IsNaN(v) || float.IsInfinity(v) ? -1 : Mathf.RoundToInt(v);
    }
}
