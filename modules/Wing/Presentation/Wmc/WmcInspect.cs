using System;
using NOAvionics;
using System.Collections.Generic;
using UnityEngine;

using BoscaliSummer.Modules.Wing.Domain;
using BoscaliSummer.Modules.Wing.Runtime;
using BoscaliSummer.Modules.Wing.Networking;
using BoscaliSummer.Core.Math;
namespace BoscaliSummer.Modules.Wing.Presentation
{
    /// <summary>INSPECT as WING's AIRCRAFT sub-page (spec bezel v2 §5 INSPECT; the 2026-10-05 redesign): one aircraft in depth — a chip per
    /// member and CENTER, a card with the damage map (the real parts over a drawn outline), FUEL with bingo, AMMO and HULL gauges and the
    /// RADAR · ALT · TASK · TARGET values, its stores by station, WHY it does what it does, its pilot (DOSSIER › opens ROSTER on them), its
    /// own recent events, and RTB · REFIT · RELEASE for that aircraft only. Inspecting never changes who orders go to (review R1 I1): the
    /// buttons act on the inspected aircraft. <see cref="Build"/> adds its parts to that sub-page's flow. Only WING uses it.</summary>
    internal sealed class WmcInspect
    {
        private const int MaxChips = WcSnapshot.MaxMembers, StoreRows = 4, RecentRows = 5;

        private readonly WmcControls ids;
        private readonly uint[] chipIds = new uint[MaxChips];
        private readonly PartMap partMap = new PartMap();
        private readonly List<LogRow> recentRows = new List<LogRow>(RecentRows);
        private readonly ConfirmGate rtbGate = new ConfirmGate(), releaseGate = new ConfirmGate();
        private MemberDetail detail = new MemberDetail { Stores = new StoreLine[8], Parts = new PartDot[PartMap.Max] };
        private readonly AircraftFace face = new AircraftFace();
        private AvFlow outer;
        private AvSection storesSection, recentSection;
        private InspectChips chips;
        private WmcSubPages pages;
        private WmcLines empty, recentLines;
        private WingAircraftCard card;
        private WingStoresList storesList;
        private WingPilotLine pilotLine;
        private AvRow whyRow;
        private AvControl rtb, refit, release;
        private int gate = -1, whyKey = int.MinValue;
        private int key = int.MinValue, chipsKey = int.MinValue, recentKey = int.MinValue;
        private uint inspected;
        private string pilotCallsign;
        private long inspectStamp = long.MinValue;
        private WmcContext last;

        public WmcInspect(WmcControls controls) => ids = controls;

        /// <summary>The page's top (the member chips; null before <see cref="Build"/>).</summary>
        public RectTransform Anchor => chips?.Rect;

        /// <summary>Show this aircraft (INSPECT › on TACTICAL, a chip, automation).</summary>
        public void Focus(uint id)
        {
            inspected = id;
            key = int.MinValue;
            recentKey = int.MinValue;
        }

        /// <summary>Adds the AIRCRAFT page to <paramref name="flow"/> (WING's AIRCRAFT sub-page).</summary>
        public void Build(AvFlow flow, AvTicker ticker, int pageIndex)
        {
            outer = flow;
            chips = flow.Add(new InspectChips(flow.Content, ids, MaxChips, PickChip, Center));
            pages = flow.Add(new WmcSubPages(flow, ticker, pageIndex, 2));

            AvFlow e = pages.Flow(0);
            empty = e.Add(new WmcLines(e.Content, 1, AvTextRole.ProseSmall, "hint"));

            AvFlow d = pages.Flow(1);
            card = d.Add(new WingAircraftCard(d.Content));
            storesSection = d.Section(AvIcon.Stack2, "STORES");
            storesList = d.Add(new WingStoresList(d.Content));
            whyRow = d.Add(new AvRow(d.Content));
            pilotLine = d.Add(new WingPilotLine(d.Content, OpenDossier));
            pilotLine.Dossier.Help = "This pilot's personnel file on ROSTER.";
            ids.Add("insp.dossier", pilotLine.Dossier);

            recentSection = d.Section(AvIcon.Clock, "RECENT");
            recentLines = d.Add(new WmcLines(d.Content, RecentRows));

            AvControl[] foot = d.Buttons(new AvControl.Spec("RTB", Rtb, AvButtonStyle.Default, AvIcon.ArrowBackUp),
                new AvControl.Spec("REFIT", Refit, AvButtonStyle.Default, AvIcon.Refresh),
                new AvControl.Spec("RELEASE", Release, AvButtonStyle.Danger, AvIcon.Unlink)).Controls;
            rtb = foot[0];
            refit = foot[1];
            release = foot[2];
            rtb.Help = "This aircraft goes home to the reserve (press twice).";
            refit.Help = "This aircraft refuels and rearms, then comes back.";
            release.Help = "This aircraft leaves the wing to the game's AI (press twice).";
            ids.Add("insp.rtb", rtb);
            ids.Add("insp.refit", refit);
            ids.Add("insp.release", release);
            Gate(0);
        }

        private void Gate(int g)
        {
            if (gate == g) return;
            gate = g;
            pages.Show(g);
        }

        public void Shown(WmcContext c)
        {
            key = chipsKey = recentKey = int.MinValue;
        }

        public void Refresh(WmcContext c)
        {
            last = c;
            // The inspected aircraft left the wing: the first member stands in, never a stale id.
            if (inspected == 0u || WingRows.IndexOf(c.Rows, c.Count, inspected) < 0)
                inspected = c.Selection.Single != 0u ? c.Selection.Single : c.Count > 0 ? c.Rows[0].Id : 0u;
            RefreshChips(c);
            WingMember m = c.Client ? null : c.MemberOf(inspected);
            bool show = m != null && (object)m.Aircraft != null;
            Gate(show ? 1 : 0);
            chips.Center.Interactable = inspected != 0u;
            if (!show)
            {
                if (empty.Set(0, InspectWords.Empty(c.Client))) outer.RequestRelayout();
                return;
            }
            RefreshDetail(c, m);
            RefreshWhy(c, m);
            RefreshRecent(c);
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
                chip.Help = "Inspect " + WingRows.Number(c.Rows[i].Slot) + ".";
            }
        }

        private void RefreshDetail(WmcContext c, WingMember m)
        {
            WmcDetail.Gather(m, ref detail);
            partMap.Gather(m, inspected, ref detail);
            int row = WingRows.IndexOf(c.Rows, c.Count, inspected);
            int e = c.Wing.ElementOf(m);
            WingPlanner p = c.Wing.Roster.InUse(e) ? c.Wing.PlannerOf(e) : null;
            unchecked
            {
                int k = (int)inspected * 7 + R(detail.Fuel * 100f) * 31 + R(detail.Ammo * 100f) * 131 + R(detail.Damage * 100f) * 17
                    + detail.Radar * 3 + R(m.Last.RadarAlt / 50f) * 1009 + R(m.Last.Speed * 0.36f) * 97 + R(detail.BingoSeconds / 10f)
                    + m.Seat * 7919 + e * 104729
                    + (detail.Target?.GetHashCode() ?? 0) + (row >= 0 ? c.Rows[row].Duty * 13 + c.Rows[row].Flags * 19 : 0)
                    + (p != null && p.Active ? (int)p.Current.Kind * 23 + p.Leg * 29 : -1) + detail.StoreCount * 41
                    + detail.PartsLost * 53 + detail.PartsHit * 59 + detail.PartCount * 61;
                for (int i = 0; i < detail.StoreCount && i < detail.Stores.Length; i++) k = k * 3 + detail.Stores[i].Ammo;
                if (k == key) return;
                key = k;
            }
            Aircraft a = m.Aircraft;
            string type = a.definition != null && !string.IsNullOrEmpty(a.definition.code) ? a.definition.code : WmcText.Unknown;
            string state = row >= 0 ? WingRows.State(c.Rows[row]) : WmcText.Unknown;
            string name = c.Wing.Roster.Name(e);
            string letter = ElementRoster.Letter(e);
            string b = WingHudText.BingoTime(detail.BingoSeconds);
            float hullFrac = float.IsNaN(detail.Damage) ? float.NaN : 1f - detail.Damage;
            int loaded = 0;
            for (int i = 0; i < detail.StoreCount && i < detail.Stores.Length; i++)
                if (detail.Stores[i].Ammo > 0) loaded++;
            face.Title = InspectWords.Title(m.Seat, null, type);
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
            face.HullSub = detail.PartsLost > 0 ? AvNum.Fixed(detail.PartsLost, 0) + (detail.PartsLost == 1 ? " PART LOST" : " PARTS LOST")
                : detail.PartsHit > 0 ? AvNum.Fixed(detail.PartsHit, 0) + (detail.PartsHit == 1 ? " PART HIT" : " PARTS HIT") : "NO DAMAGE";
            face.HullRail = LevelClass(WmcStyle.Level(hullFrac));
            face.Radar = detail.Radar < 0 ? "NONE" : detail.Radar > 0 ? "ON" : "OFF";
            face.Alt = WmcWords.Altitude(m.Last.RadarAlt) + " · " + WmcWords.Speed(m.Last.Speed);
            face.Task = WmcText.Cut(p == null || !p.Active ? (e == 0 ? "FORM · on you" : "FORM")
                : TaskCard.Short(p.Current, p.Leg, p.Lead != null ? p.Lead.Position : Vec3.Zero, p.Lead != null ? p.Lead.Speed : 0f), 26);
            face.Target = string.IsNullOrEmpty(detail.Target) ? "NONE" : WmcText.Cut(detail.Target, 24);
            face.PartCount = detail.PartCount;
            Array.Copy(detail.Parts, face.Parts, detail.PartCount);
            face.MapNote = detail.PartCount > 0
                ? AvNum.Fixed(detail.PartCount, 0) + " PARTS · " + AvNum.Fixed(detail.PartsHit, 0) + " HIT · " + AvNum.Fixed(detail.PartsLost, 0) + " LOST"
                : "PART MAP UNAVAILABLE";
            card.Show(face);
            storesSection.SetCaption(AvNum.Fixed(detail.StoreCount, 0) + (detail.StoreCount == 1 ? " STATION" : " STATIONS"));
            storesList.Show(detail);
            WingPilot wp = WingPilotRoster.Of(a);
            pilotCallsign = wp?.Callsign;
            pilotLine.SetPilot(wp, wp != null ? "\"" + WmcText.Cut(wp.Callsign, PilotPick.CallsignChars) + "\"" : "NO PILOT RECORD",
                wp != null ? (detail.Rank ?? WmcText.Unknown) + " · XP " + AvNum.Fixed(wp.Xp, 0) + " · " + AvNum.Fixed(wp.Kills, 0) + (wp.Kills == 1 ? " KILL" : " KILLS") : "");
            pilotLine.Dossier.Interactable = wp != null;
            outer.RequestRelayout();
        }

        /// <summary>What holds the aircraft back and for how long; when the game's combat or landing AI flies it, that says so.</summary>
        private void RefreshWhy(WmcContext c, WingMember m)
        {
            float now = c.MissionTime;
            // Review I1: only the landing itself is the game's; the approach home and a settle are Wing Command's (no formation
            // limits to say then), and a report that stopped coming says nothing stale.
            RecoveryPhase phase = m.Recovery != null ? m.Recovery.Phase : RecoveryPhase.Done;
            bool ours = !m.Engaged && m.Recovery == null && !m.OnGround && m.Why.Live(now);
            int k = ours ? m.Why.Key(now) : m.Engaged ? -2 : m.OnGround ? -3 : m.Recovery != null ? -4 - (int)phase : m.Settle != null ? -20 : -21;
            if (k == whyKey) return;
            whyKey = k;
            whyRow.Set("WHY", ours ? m.Why.Line(now) : m.Engaged ? "THE GAME'S COMBAT AI FLIES IT (ENGAGED)"
                : m.OnGround ? "ON THE GROUND · TAXI AND TAKE-OFF ARE WING COMMAND'S"
                : m.Recovery != null ? (phase == RecoveryPhase.Landing ? "THE GAME'S LANDING FLIES IT" : "FLYING HOME TO LAND · WING COMMAND")
                : m.Settle != null ? "SETTLING AT ITS POINT · WING COMMAND" : "NOT IN FORMATION FLIGHT", "", AvState.Inert);
            outer.RequestRelayout();
        }

        private void RefreshRecent(WmcContext c)
        {
            long stamp = LogRows.Stamp(c.Wing?.Events, null) * 31L + inspected;
            if (stamp == inspectStamp && recentKey != int.MinValue) return;
            inspectStamp = stamp;
            var filter = new LogFilter { Element = -1, ById = true, Id = inspected, Rows = c.Rows, Count = c.Count };
            int n = LogRows.Fill(c.Wing?.Events, null, recentRows, RecentRows, filter);
            int k = n * 7919 + (int)(inspected % 1009u) + (n > 0 ? (int)(recentRows[0].Time * 10f) : 0);
            if (k == recentKey) return;
            recentKey = k;
            recentSection.SetCaption(n > 0 ? WingRows.Number(Seat()) : "NOTHING LOGGED");
            for (int i = 0; i < RecentRows; i++)
                recentLines.Set(i, i < n ? WmcText.Clock(recentRows[i].Time) + "  " + recentRows[i].Text : "");
            outer.RequestRelayout();
        }

        private void RefreshFoot(WmcContext c)
        {
            if (inspected != footFor)
            {
                footFor = inspected;
                footId = AvNum.Fixed(inspected, 0);
            }
            bool asking = rtbGate.IsArmed(footId, Time.unscaledTime);
            if (asking != rtbAsking)
            {
                rtbAsking = asking;
                rtb.Label = asking ? "RTB?" : "RTB";
            }
            asking = releaseGate.IsArmed(footId, Time.unscaledTime);
            if (asking != releaseAsking)
            {
                releaseAsking = asking;
                release.Label = asking ? "RELEASE?" : "RELEASE";
            }
            rtb.Interactable = c.CanOrder;
            refit.Interactable = c.CanOrder;
            release.Interactable = c.CanOrder;
        }

        private uint footFor;
        private string footId = "";
        private bool rtbAsking, releaseAsking;

        private void PickChip(int i)
        {
            if (chipIds[i] == 0u) return;
            Focus(chipIds[i]);
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
            if (pilotCallsign == null || panel == null || panel.WingPage == null) return;
            panel.Show(WmcTabs.Squadron);
            panel.WingPage.ShowSub(WmcWing.SubRoster);
            panel.Refresh();
            panel.WingPage.Inspect(pilotCallsign);
            panel.Refresh();
        }

        private void Rtb() => Confirmed(rtbGate, OrderKind.Rtb, "Send " + WingRows.Number(Seat()) + " home? Press RTB again");

        private void Release() => Confirmed(releaseGate, OrderKind.Release, "Release " + WingRows.Number(Seat()) + " to the game's AI? Press RELEASE again");

        private void Refit() => WmcUi.Order(last, () => WingOrders.Run(WingOrder.Of(OrderKind.Refit, WingScope.OfMembers(inspected))));

        private void Confirmed(ConfirmGate gateFor, OrderKind kind, string ask)
        {
            if (last == null || inspected == 0u) return;
            uint id = inspected;
            WmcUi.Order(last, () =>
            {
                if (!gateFor.Press(AvNum.Fixed(id, 0), Time.unscaledTime))
                {
                    WingToast.Show(ask);
                    return;
                }
                WingOrders.Run(WingOrder.Of(kind, WingScope.OfMembers(id)));
            });
            WmcPanel.Instance?.Refresh();
        }

        private int Seat()
        {
            int i = last != null ? WingRows.IndexOf(last.Rows, last.Count, inspected) : -1;
            return i >= 0 ? last.Rows[i].Slot : 0;
        }

        /// <summary>Automation: the page's state.</summary>
        public void Report(Dictionary<string, object> into)
        {
            into["inspect_member"] = inspected != 0u && last != null && WingRows.IndexOf(last.Rows, last.Count, inspected) >= 0
                ? last.Rows[WingRows.IndexOf(last.Rows, last.Count, inspected)].Slot + 2 : 0;
            into["inspect_stores"] = storesList != null ? storesList.Shown : 0;
            into["inspect_parts"] = detail.PartCount;
            into["inspect_parts_lost"] = detail.PartsLost;
        }

        /// <summary>A tape's level word (ok, warn, bad, none) as the state class the rail colours read.</summary>
        private static string LevelClass(string level) => level == "bad" ? "danger" : level == "warn" ? "warn" : level == "ok" ? "live" : "inert";

        private static int R(float v) => float.IsNaN(v) || float.IsInfinity(v) ? -1 : Mathf.RoundToInt(v);
    }
}
