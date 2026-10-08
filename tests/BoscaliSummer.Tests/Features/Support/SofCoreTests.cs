using System;
using System.Collections.Generic;
using BoscaliSummer.Modules.Support.Domain.Calls;
using BoscaliSummer.Modules.Support.Domain.Cyber;
using BoscaliSummer.Modules.Support.Domain.Sof;
using BoscaliSummer.Modules.Support.Domain.Space;
using BoscaliSummer.Modules.Support.Runtime;

namespace BoscaliSummer.Tests.Features.Support
{
    /// <summary>M5a SOF pure core: rules and odds, camps, target ids and fog, the team desk (raise, route, exposure, pinned, missions, held, lift) (spec 2.1 to 2.3).</summary>
    internal static class SofCoreTests
    {
        private const ulong Op = 7, Pilot = 9;

        public static void Run()
        {
            CheckRules();
            CheckCampsAndIds();
            CheckRaise();
            CheckRoute();
            CheckExposureAndPin();
            CheckMissions();
            CheckHeld();
            CheckLift();
            CheckOracle();
            CheckTap();
            CheckFob();
            CheckReviewFixes();
        }

        private static void Near(float actual, float expected, string message, float tolerance = 0.05f) =>
            TestAssert.That(Math.Abs(actual - expected) <= tolerance, message + " (got " + actual + ", want " + expected + ")");

        /// <summary>A scriptable host: a clock, a wallet, one scene function and a log of every effect the desk asked for.</summary>
        private sealed class Ports : ISofPorts
        {
            public float Now { get; set; } = 100f;
            public int Humans { get; set; } = 2;
            public int Owner => 1;
            public bool Fob { get; set; }
            public int Wallet = 1000;
            public bool RollResult = true;
            public int LastChance;
            public bool CyberHolds;
            public Func<float, float, SofScene> SceneAt = (x, z) => default;
            public readonly List<string> Log = new List<string>();
            public readonly List<int> Paid = new List<int>();
            public bool Alive = true, BuildingUp = true, LaseOk = true;

            public SofOutcome TrySpend(ulong op, int cr, out int detail)
            {
                detail = 0;
                if (Wallet < cr) { detail = cr; return SofOutcome.LowCredit; }
                Wallet -= cr; return SofOutcome.None;
            }
            public void Refund(ulong op, int cr) => Wallet += cr;
            public SofScene Scene(float x, float z) => SceneAt(x, z);
            public bool CyberNear(float x, float z) => CyberHolds;
            public bool Roll(int chancePercent) { LastChance = chancePercent; return RollResult; }
            public void Reveal(float x, float z, float radius, float seconds) => Log.Add("reveal " + (int)radius + " " + (int)seconds);
            public bool LaseBegin(int slot, uint key, float x, float z) { if (LaseOk) Log.Add("lase+" + slot); return LaseOk; }
            public void LaseEnd(int slot, uint key) => Log.Add("lase-" + slot);
            public bool TargetAlive(TargetKind kind, AnchorSub sub, uint key) => Alive;
            public bool Sabotage(AnchorSub sub, uint key) { Log.Add("sabotage " + sub); return true; }
            public bool Tap(TargetKind kind, uint key, float seconds) { Log.Add("tap " + (int)seconds); return true; }
            public bool BuildingAlive(uint key) => BuildingUp;
            public bool PostCover(int slot, float x, float z) { Log.Add("cover" + slot); return true; }
            public bool PostLase(int slot, float x, float z) { Log.Add("lasepost" + slot); return true; }
            public void Pay(ulong pilot, int cr) { Paid.Add(cr); Log.Add("pay" + cr); }
        }

        private static SofDesk Desk(Ports p, int camps = 1)
        {
            var set = new SofCampSet(camps);
            for (int i = 0; i < camps; i++) { set.SetPosition(i, 0f, 0f); set.Set(i, 1f, false, 0f); }
            return new SofDesk(p, set);
        }

        private static void Advance(SofDesk d, Ports p, float seconds)
        {
            for (float t = 0; t < seconds; t += 1f) { p.Now += 1f; d.Tick(); }
        }

        private static SofTeam ReadyTeam(SofDesk d, Ports p)
        {
            SofResult r = d.Raise(Op);
            TestAssert.That(r.Outcome == SofOutcome.Raised, "raise " + r.Outcome);
            Advance(d, p, SofRules.RaiseSeconds + 1f);
            TestAssert.Eq(d.Teams[r.Slot].State, TeamState.Ready, "deployed");
            return d.Teams[r.Slot];
        }

        private static void Show(SofDesk d, params SofSeed[] seeds)
        {
            d.Refresh(seeds);
        }

        private static SofSeed Seed(TargetKind k, AnchorSub s, uint key, float x, float z, bool sighted = true) => new SofSeed(k, s, key, x, z, 1000f, sighted, false);

        // ---- Rules ---------------------------------------------------------------------------------

        private static void CheckRules()
        {
            TestAssert.Eq(SofRules.Odds(0f, 0, false, false, false), 70, "base odds 70");
            TestAssert.Eq(SofRules.Odds(50f, 0, false, false, false), 45, "exposure halves into the odds");
            TestAssert.Eq(SofRules.Odds(0f, 2, false, false, false), 50, "10 per armoured unit within 1 km");
            TestAssert.Eq(SofRules.Odds(0f, 0, true, false, false), 90, "+20 helicopter insertion");
            TestAssert.Eq(SofRules.Odds(0f, 0, false, true, false), 95, "+25 EXPLOIT clamps at 95");
            TestAssert.Eq(SofRules.Odds(0f, 0, true, true, true), 95, "everything clamps at 95");
            TestAssert.Eq(SofRules.Odds(100f, 9, false, false, false), 10, "a bad spot clamps at 10");
            TestAssert.Eq(SofRules.Odds(0f, 0, false, false, true), 85, "+15 when CYBER holds a node within 12 km");
            TestAssert.Eq(SofRules.Odds(float.NaN, 0, false, false, false), 20, "an unreadable exposure reads as 100 %");
            Near(SofRules.ExposureDelta(2, false, false, false, 1f), 0.25f, "two enemies add 1 %/s (0.5 each) against 0.75 %/s of recovery");
            Near(SofRules.ExposureDelta(1, true, false, false, 1f), -0.125f, "a bird's stare x1.25 on one defender");
            Near(SofRules.ExposureDelta(3, true, false, false, 1f), 1.125f, "three defenders under a bird: 1.875 - 0.75");
            Near(SofRules.ExposureDelta(1, false, true, false, 1f), 0f, "PUSH +50 % on one defender is a stand-off");
            Near(SofRules.ExposureDelta(2, false, true, false, 1f), 0.75f, "PUSH +50 % on two");
            Near(SofRules.ExposureDelta(1, false, false, false, 1f), -0.25f, "one defender: net recovery 0.25 %/s");
            Near(SofRules.ExposureDelta(0, false, false, false, 2f), -1.5f, "nothing near falls 0.75 %/s");
            Near(SofRules.ExposureDelta(0, false, false, true, 1f), -1.5f, "HOLD falls twice as fast");
            TestAssert.Eq(SofRules.ExposureRadius, 1000f, "exposure counts enemies within 1 km"); TestAssert.Eq(SofRules.CoverRadius, 2000f, "the COVER kill radius stays 2 km");
            Near(SofRules.SpeedMetresPerSecond * 3.6f, 35f, "35 km/h");
            TestAssert.Eq(SofRules.TeamCap(2, false), 2, "two teams"); TestAssert.Eq(SofRules.TeamCap(5, false), 3, "three at 5 humans"); TestAssert.Eq(SofRules.TeamCap(5, true), 4, "FOB adds one");
            TestAssert.Eq(SofRules.Callsign(0), "A-1", "callsign A"); TestAssert.Eq(SofRules.Callsign(3), "D-1", "callsign D");
            TestAssert.Eq(SofRules.CostOf(MissionKind.Sabotage, false), 4, "a mission by a player costs 4 allocation"); TestAssert.Eq(SofRules.CostOf(MissionKind.Sabotage, true), 3, "EXPLOIT -25 %");
            TestAssert.Eq(SofRules.CostOf(MissionKind.Recon, false), 4, "recon 4"); TestAssert.Eq(SofRules.CostOf(MissionKind.None, false), 0, "no mission, no cost");
            Near(SofRules.ExposureDelta(2, false, false, false, 1f, 1.5f), 0.75f, "the enemy SPACE counter scales the climb x1.5 (1.5 - 0.75)");
            Near(SofRules.ExposureDelta(0, false, false, false, 1f, 1.5f), -0.75f, "and never the recovery");
            TestAssert.That(SofRules.Valid(MissionKind.Lase, TargetKind.Ground, AnchorSub.Uplink) && !SofRules.Valid(MissionKind.Lase, TargetKind.Building, AnchorSub.Uplink), "lase takes ground only");
            TestAssert.That(SofRules.Valid(MissionKind.Tap, TargetKind.Relay, AnchorSub.Uplink) && SofRules.Valid(MissionKind.Tap, TargetKind.Anchor, AnchorSub.DataCenter) && !SofRules.Valid(MissionKind.Tap, TargetKind.Anchor, AnchorSub.EwTruck), "tap takes a relay or a data center");
            TestAssert.That(!SofRules.Valid(MissionKind.Recon, TargetKind.Ground, AnchorSub.Uplink), "recon takes a point, never a target id");
            TestAssert.That(SofRules.Exploit(TargetKind.Anchor, AnchorSub.EwTruck) && SofRules.Exploit(TargetKind.Relay, AnchorSub.Uplink) && !SofRules.Exploit(TargetKind.Anchor, AnchorSub.Camp), "SOF beats CYBER only");
            TestAssert.That(SofRules.Resisted(TargetKind.Anchor, AnchorSub.Uplink) && !SofRules.Resisted(TargetKind.Ground, AnchorSub.Uplink), "SPACE resists SOF");
            TestAssert.Eq(CampRules.RaiseFactor(AnchorHealth.Damaged), 1.5f, "damaged camp x1.5");
        }

        private static void CheckCampsAndIds()
        {
            var camps = new SofCampSet(5);
            TestAssert.Eq(camps.Count, 2, "two camps at most");
            camps.SetPosition(0, 100f, 0f); camps.SetPosition(1, 5000f, 0f);
            camps.Set(0, 1f, false, 0f); camps.Set(1, 0.4f, false, 0f);
            TestAssert.Eq(camps.Health(1), AnchorHealth.Damaged, "damaged");
            TestAssert.That(camps.TryBest(out int best, out float bx, out _, out AnchorHealth h) && best == 0 && h == AnchorHealth.Live && bx == 100f, "a live camp is preferred");
            camps.Set(0, 0f, true, 50f);
            TestAssert.That(camps.TryBest(out best, out _, out _, out h) && best == 1 && h == AnchorHealth.Damaged, "a damaged camp raises when none is live");
            TestAssert.That(!camps.PastGrace(0, 100f) && camps.PastGrace(0, 171f), "120 s grace");
            camps.Set(1, 0f, true, 60f);
            TestAssert.That(!camps.TryBest(out _, out _, out _, out _), "no camp standing");
            camps.Restore(0, 120f, 0f, 200f);
            TestAssert.Eq(camps.Health(0), AnchorHealth.Live, "restored");
            Near(camps.NearestStanding(1120f, 0f, out float nx, out _), 1000f, "nearest standing camp"); TestAssert.Eq(nx, 120f, "its x");
            var ids = new SofTargetIds();
            int a = ids.GetOrAdd(TargetKind.Ground, AnchorSub.Uplink, 5), b = ids.GetOrAdd(TargetKind.Anchor, AnchorSub.Camp, 5);
            TestAssert.That(a == 1 && b == 2 && ids.GetOrAdd(TargetKind.Ground, AnchorSub.Uplink, 5) == 1, "ids are stable per (kind, sub, key)");
            TestAssert.That(ids.TryKey(b, out TargetKind k, out AnchorSub s, out uint key) && k == TargetKind.Anchor && s == AnchorSub.Camp && key == 5u, "id resolves");
            TestAssert.That(!ids.TryKey(0, out _, out _, out _) && !ids.TryKey(99, out _, out _, out _), "unknown ids resolve to nothing");
            for (uint i = 0; i < 300; i++) ids.GetOrAdd(TargetKind.Ground, AnchorSub.Uplink, 1000 + i);
            TestAssert.Eq(ids.Count, SofTargetIds.Capacity, "the table is bounded");
            TestAssert.Eq(ids.GetOrAdd(TargetKind.Ground, AnchorSub.Uplink, 99999), 0, "a full table answers 0");
        }

        // ---- Raise ---------------------------------------------------------------------------------

        private static void CheckRaise()
        {
            var p = new Ports();
            TestAssert.Eq(new SofDesk(p, new SofCampSet(0)).Raise(Op).Outcome, SofOutcome.NoCamp, "no camp, no SOF");
            SofDesk d = Desk(p);
            d.Camps.Set(0, 0f, true, 0f);
            TestAssert.Eq(d.Raise(Op).Outcome, SofOutcome.CampDown, "a down camp raises nothing");
            d.Camps.Set(0, 1f, false, 0f);
            SofResult r = d.Raise(Op);
            TestAssert.Eq(r.Outcome, SofOutcome.Raised, "raised"); TestAssert.Eq(r.Charged, 60, "60 CR"); TestAssert.Eq(p.Wallet, 940, "wallet charged"); TestAssert.Eq(r.Detail, 90, "90 s");
            TestAssert.Eq(d.Teams[0].State, TeamState.Raising, "raising");
            TestAssert.Eq(d.Send(Op, 0, MissionKind.Recon, 0, 100f, 100f).Outcome, SofOutcome.Raising, "orders wait for the deploy");
            Advance(d, p, 89f);
            TestAssert.Eq(d.Teams[0].State, TeamState.Raising, "still raising at 89 s");
            Advance(d, p, 2f);
            TestAssert.Eq(d.Teams[0].State, TeamState.Ready, "ready after 90 s");
            TestAssert.Eq(d.Raise(Op).Outcome, SofOutcome.Raised, "second team"); TestAssert.Eq(d.Raise(Op).Outcome, SofOutcome.TeamCap, "cap 2 at two humans");
            p.Humans = 5;
            TestAssert.Eq(d.Raise(Op).Outcome, SofOutcome.Raised, "third team at 5 humans");
            var poor = new Ports { Wallet = 59 };
            SofDesk d2 = Desk(poor);
            SofResult low = d2.Raise(Op);
            TestAssert.Eq(low.Outcome, SofOutcome.LowCredit, "short wallet"); TestAssert.Eq(low.Detail, 60, "need 60"); TestAssert.Eq(d2.ActiveCount, 0, "no team for a refused raise"); TestAssert.Eq(poor.Wallet, 59, "nothing taken");
            var slow = new Ports();
            SofDesk d3 = Desk(slow);
            d3.Camps.Set(0, 0.5f, false, 0f);
            TestAssert.Eq(d3.Raise(Op).Detail, 135, "a damaged camp raises 1.5x slower");
        }

        // ---- FOB (M6a) -----------------------------------------------------------------------------

        private static void CheckFob()
        {
            var p = new Ports();
            SofDesk d = Desk(p);
            d.Camps.Set(0, 0f, true, 0f);
            TestAssert.Eq(d.Raise(Op).Outcome, SofOutcome.CampDown, "no FOB, a down camp raises nothing");
            d.FobActive = true; d.FobX = 500f; d.FobZ = 700f;
            SofResult r = d.Raise(Op);
            TestAssert.Eq(r.Outcome, SofOutcome.Raised, "a FOB raises with the camp down");
            TestAssert.Eq(d.Teams[r.Slot].X, 500f, "the team starts at the FOB"); TestAssert.Eq(d.Teams[r.Slot].HomeZ, 700f, "and calls it home");
            TestAssert.Eq(r.Detail, 90, "a FOB deploys at the normal 90 s");
            p.Fob = true;
            TestAssert.Eq(d.Raise(Op).Outcome, SofOutcome.Raised, "second team"); TestAssert.Eq(d.Raise(Op).Outcome, SofOutcome.Raised, "the FOB's +1 team cap: three at two humans");
            TestAssert.Eq(d.Raise(Op).Outcome, SofOutcome.TeamCap, "and no more");
            d.FobActive = false;
            var q = new Ports();
            SofDesk e = Desk(q);
            TestAssert.Eq(e.Raise(Op).Outcome, SofOutcome.Raised, "without a FOB the camp raises as before"); TestAssert.Eq(e.Teams[0].X, 0f, "at the camp");
        }

        // ---- Route ---------------------------------------------------------------------------------

        private static void CheckRoute()
        {
            var p = new Ports();
            SofDesk d = Desk(p);
            SofTeam t = ReadyTeam(d, p);
            TestAssert.Eq(d.Divert(Op, 0, 3500f, 0f).Outcome, SofOutcome.Diverted, "divert from camp");
            Advance(d, p, 100f);
            Near(t.X, 972.2f, "35 km/h for 100 s", 15f);
            TestAssert.That(d.Order(Op, 0, TeamVerb.Push).Ok, "push");
            float before = t.X; Advance(d, p, 10f);
            Near(t.X - before, 145.8f, "PUSH x1.5 for 10 s", 15f);
            TestAssert.That(d.Order(Op, 0, TeamVerb.Hold).Ok && t.HoldOn && !t.PushOn, "HOLD replaces PUSH");
            before = t.X; Advance(d, p, 10f);
            TestAssert.Eq(t.X, before, "HOLD stops the team");
            d.Order(Op, 0, TeamVerb.Hold);
            Advance(d, p, 400f);
            TestAssert.Eq(t.State, TeamState.Ready, "arrived and idle"); Near(t.X, 3500f, "at the point", 1f);
            TestAssert.Eq(d.Order(Op, 0, TeamVerb.Exfil).Outcome, SofOutcome.Ordered, "exfil");
            TestAssert.Eq(t.State, TeamState.Returning, "returning");
            Advance(d, p, 400f);
            TestAssert.Eq(t.State, TeamState.Ready, "home again"); Near(t.X, 0f, "back at the camp", 1f);
            TestAssert.Eq(d.Order(Op, 0, TeamVerb.Exfil).Outcome, SofOutcome.BadOrder, "no exfil from the camp");
            TestAssert.Eq(d.Divert(Op, 0, float.NaN, 0f).Outcome, SofOutcome.OutOfTheater, "a NaN point is refused");
            TestAssert.Eq(d.Divert(Op, 0, 9e9f, 0f).Outcome, SofOutcome.OutOfTheater, "a far point is refused");
            TestAssert.Eq(d.Divert(Op, 3, 1f, 1f).Outcome, SofOutcome.NoTeam, "no such team");
            TestAssert.Eq(d.Order(Op, 0, TeamVerb.Push).Outcome, SofOutcome.BadOrder, "push needs a moving team");
        }

        // ---- Exposure, pinned, cover -----------------------------------------------------------------

        private static void CheckExposureAndPin()
        {
            var p = new Ports();
            SofDesk d = Desk(p);
            SofTeam t = ReadyTeam(d, p);
            d.Divert(Op, 0, 20000f, 0f);
            p.SceneAt = (x, z) => x > 200f ? new SofScene(0, 3, 4, 1, false) : default;
            Advance(d, p, 30f);
            TestAssert.That(t.Exposure > 0f && t.Exposure < 100f, "exposure rises near the enemy (" + t.Exposure + ")");
            Advance(d, p, 90f);
            TestAssert.Eq(t.State, TeamState.Pinned, "pinned at 100 %"); Near(t.Exposure, 100f, "full exposure");
            TestAssert.That(p.Log.Contains("cover0"), "the pin posts a COVER request");
            float x0 = t.X; Advance(d, p, 5f);
            TestAssert.Eq(t.X, x0, "a pinned team does not move");
            TestAssert.Eq(d.Divert(Op, 0, 1f, 1f).Outcome, SofOutcome.Pinned, "no orders while pinned");
            TestAssert.That(!d.CoverClaimed(2, Pilot) && d.CoverClaimed(0, Pilot), "only a pinned team can be covered");
            p.SceneAt = (x, z) => new SofScene(0, 3, 4, 1, false);
            Advance(d, p, 80f);
            TestAssert.Eq(t.State, TeamState.Pinned, "cover extends the lost timer");
            for (int i = 0; i < 2; i++) TestAssert.Eq(d.CoverKill(t.X, t.Z), 1, "a kill within 2 km covers the team");
            TestAssert.Eq(d.CoverKill(t.X + 5000f, t.Z), 0, "a kill far away does not");
            TestAssert.Eq(d.CoverKillBy(Pilot, t.X, t.Z), 1, "the claimant's own kill within 2 km is remembered");
            Near(t.Exposure, 50f, "each kill relieves 25 points", 6f);
            p.SceneAt = (x, z) => default;
            Advance(d, p, 5f);
            TestAssert.Eq(t.State, TeamState.Moving, "below 60 % the team breaks contact and goes on");
            TestAssert.That(p.Paid.Contains(SofRules.CoverPay), "the covering pilot is paid");

            var q = new Ports();
            SofDesk e = Desk(q);
            SofTeam u = ReadyTeam(e, q);
            e.Divert(Op, 0, 20000f, 0f);
            q.SceneAt = (x, z) => new SofScene(0, 9, 60, 0, false);
            Advance(e, q, 4f);
            TestAssert.Eq(u.State, TeamState.Pinned, "pinned fast");
            Advance(e, q, 110f);
            TestAssert.Eq(u.State, TeamState.Pinned, "not yet lost");
            Advance(e, q, 15f);
            TestAssert.Eq(u.State, TeamState.Lost, "120 s pinned without cover loses the team");
            Advance(e, q, 31f);
            TestAssert.Eq(e.Teams[0].Active, false, "the slot frees after the lost marker");

            // The mission target's own unit never adds exposure: a lone defender is free, a second one costs 0.5 %/s.
            var r = new Ports();
            SofDesk g = Desk(r);
            SofTeam w = ReadyTeam(g, r);
            Show(g, Seed(TargetKind.Ground, AnchorSub.Uplink, 21, 900f, 0f));
            TestAssert.Eq(g.Send(Op, 0, MissionKind.Lase, g.Visible[0].Id, 0f, 0f).Outcome, SofOutcome.Sent, "lase sent");
            r.SceneAt = (x, z) => new SofScene(0, 1, 1, 0, false);
            Advance(g, r, 60f);
            Near(w.Exposure, 0f, "the target alone adds no exposure");
            r.SceneAt = (x, z) => new SofScene(0, 4, 4, 0, false);
            Advance(g, r, 10f);
            TestAssert.That(w.Exposure > 6f && w.Exposure < 9f, "three escorts add 1.5 %/s against 0.75 %/s of recovery (" + w.Exposure + ")");
            // The exemption holds on the way home: the old target adds nothing after EXFIL either.
            g.Divert(Op, 0, 0f, 0f);
            TestAssert.Eq(w.Mission, MissionKind.None, "the mission is over");
            r.SceneAt = (x, z) => new SofScene(0, 2, 2, 0, false); // the old target plus one escort
            float before = w.Exposure;
            Advance(g, r, 10f);
            TestAssert.That(w.Exposure < before, "after the order the former target still adds nothing: one escort falls " + before + " -> " + w.Exposure);
            var keep = new HashSet<uint>();
            g.CollectKeep(keep);
            TestAssert.That(keep.Contains(21u), "the exempt unit stays in the keep set after the mission ends, so the fog cannot lapse the exemption");
            r.Alive = false; // truly dead
            Advance(g, r, 2f);
            TestAssert.Eq(w.ExemptKey, 0u, "a dead unit releases the exemption");
            g.CollectKeep(keep);
            TestAssert.That(!keep.Contains(21u), "and the keep set");
        }

        // ---- Missions ---------------------------------------------------------------------------------

        private static void CheckMissions()
        {
            // RECON: a point, 60 s on site, reveals 2 km for 5 minutes.
            var p = new Ports();
            SofDesk d = Desk(p);
            SofTeam t = ReadyTeam(d, p);
            TestAssert.Eq(d.Send(Op, 0, MissionKind.Recon, 0, 900f, 0f).Outcome, SofOutcome.Sent, "recon sent"); TestAssert.Eq(p.Wallet, 940 - 4, "4 allocation");
            TestAssert.Eq(d.Send(Op, 0, MissionKind.Recon, 0, 1f, 1f).Outcome, SofOutcome.NotReady, "a tasked team is not ready");
            Advance(d, p, 100f);
            TestAssert.Eq(t.State, TeamState.OnSite, "on site");
            Advance(d, p, 61f);
            TestAssert.Eq(t.State, TeamState.Ready, "done");
            TestAssert.That(p.Log.Contains("reveal 2000 300"), "recon reveals 2 km for 300 s");
            Near(t.Ammo, 70f, "a mission costs 30 ammo");

            // SABOTAGE on an EW truck is an EXPLOIT target: cheaper and stronger; odds roll once.
            var s = new Ports();
            SofDesk e = Desk(s);
            SofTeam u = ReadyTeam(e, s);
            Show(e, Seed(TargetKind.Anchor, AnchorSub.EwTruck, 11, 700f, 0f));
            int truck = e.Visible[0].Id;
            TestAssert.Eq(e.Send(Op, 0, MissionKind.Sabotage, truck, 0f, 0f).Charged, 3, "EXPLOIT -25 % (4 -> 3)");
            Advance(e, s, 80f);
            Advance(e, s, 91f);
            TestAssert.That(s.Log.Contains("sabotage EwTruck"), "the anchor goes down through the host call");
            TestAssert.Eq(s.LastChance, 95, "odds 70 + 25 EXPLOIT clamp 95");
            TestAssert.Eq(u.State, TeamState.Ready, "ready again");

            // A failed roll: wounded, returns, recovers 60 s; high exposure loses it.
            var f = new Ports { RollResult = false };
            SofDesk g = Desk(f);
            SofTeam w = ReadyTeam(g, f);
            Show(g, Seed(TargetKind.Anchor, AnchorSub.Uplink, 12, 700f, 0f));
            g.Send(Op, 0, MissionKind.Sabotage, g.Visible[0].Id, 0f, 0f);
            Advance(g, f, 180f);
            TestAssert.Eq(w.State, TeamState.Returning, "a failed raid sends the team home"); TestAssert.That(w.Wounded, "wounded");
            Advance(g, f, 80f);
            TestAssert.Eq(w.State, TeamState.Recovering, "WIA recovery at the camp");
            TestAssert.Eq(g.Send(Op, 0, MissionKind.Recon, 0, 5f, 5f).Outcome, SofOutcome.Wounded, "no orders while wounded");
            Advance(g, f, 61f);
            TestAssert.Eq(w.State, TeamState.Ready, "recovered after 60 s");
            var h = new Ports { RollResult = false };
            SofDesk i2 = Desk(h);
            SofTeam z = ReadyTeam(i2, h);
            Show(i2, Seed(TargetKind.Anchor, AnchorSub.Uplink, 13, 300f, 0f));
            i2.Send(Op, 0, MissionKind.Sabotage, i2.Visible[0].Id, 0f, 0f);
            Advance(i2, h, 40f);
            z.Exposure = 95f; // a hot site at the roll
            h.SceneAt = (x, zz) => new SofScene(0, 0, 0, 0, false);
            z.Exposure = 95f;
            i2.Teams[0].OnSiteEnd = h.Now + 1f;
            i2.Teams[0].Exposure = 95f;
            h.SceneAt = (x, zz) => new SofScene(0, 1, 1, 0, false);
            Advance(i2, h, 3f);
            TestAssert.Eq(z.State, TeamState.Lost, "a failure at exposure >= 90 loses the team");

            // LASE: continuous, posts a LASE request, ends at the cap or when the target dies; no roll.
            var l = new Ports();
            SofDesk m = Desk(l);
            SofTeam y = ReadyTeam(m, l);
            Show(m, Seed(TargetKind.Ground, AnchorSub.Uplink, 21, 400f, 0f));
            TestAssert.Eq(m.Send(Op, 0, MissionKind.Lase, m.Visible[0].Id, 0f, 0f).Outcome, SofOutcome.Sent, "lase sent");
            Advance(m, l, 60f);
            TestAssert.Eq(y.State, TeamState.OnSite, "lasing"); TestAssert.That(l.Log.Contains("lase+0") && l.Log.Contains("lasepost0"), "laser on and a LASE post");
            TestAssert.That(m.TryLase(out int slot, out float lx, out _) && slot == 0 && lx == 400f, "the lased point is the AIM: TEAM source");
            Advance(m, l, 200f);
            TestAssert.Eq(y.State, TeamState.OnSite, "still lasing at 200 s");
            l.Alive = false;
            Advance(m, l, 2f);
            TestAssert.Eq(y.State, TeamState.Ready, "the target died: the mission ends"); TestAssert.That(l.Log.Contains("lase-0"), "laser off"); TestAssert.Eq(l.LastChance, 0, "lase never rolls");
            TestAssert.Eq(m.Send(Op, 0, MissionKind.Lase, 12345, 0f, 0f).Outcome, SofOutcome.NoTarget, "a made-up target id");

            // TAP: 10 minutes, x1.5 on a data center (EXPLOIT); a relay too.
            var k = new Ports();
            SofDesk n = Desk(k);
            ReadyTeam(n, k);
            Show(n, Seed(TargetKind.Relay, AnchorSub.Uplink, 31, 300f, 0f));
            TestAssert.Eq(n.Send(Op, 0, MissionKind.Tap, n.Visible[0].Id, 0f, 0f).Charged, 3, "tap 4 -> 3 EXPLOIT");
            Advance(n, k, 140f);
            TestAssert.That(k.Log.Contains("tap 900"), "tap 10 min x1.5 EXPLOIT");
            TestAssert.Eq(n.Send(Op, 0, MissionKind.Seize, n.Visible[0].Id, 0f, 0f).Outcome, SofOutcome.NoTarget, "a relay is no building");

            // No ammo.
            var a = new Ports();
            SofDesk o = Desk(a);
            SofTeam q = ReadyTeam(o, a);
            q.Ammo = 10f;
            TestAssert.Eq(o.Send(Op, 0, MissionKind.Recon, 0, 5f, 5f).Outcome, SofOutcome.NoAmmo, "no ammo, no mission");
            Advance(o, a, 1f);
            q.Ammo = 100f;
            a.Wallet = 3;
            TestAssert.Eq(o.Send(Op, 0, MissionKind.Recon, 0, 5f, 5f).Outcome, SofOutcome.LowCredit, "low credit");
            TestAssert.Eq(a.Wallet, 3, "nothing taken");
        }

        // ---- Held buildings ---------------------------------------------------------------------------

        private static void CheckHeld()
        {
            var p = new Ports();
            SofDesk d = Desk(p);
            SofTeam t = ReadyTeam(d, p);
            Show(d, Seed(TargetKind.Building, AnchorSub.Uplink, 41, 400f, 0f));
            TestAssert.Eq(d.Send(Op, 0, MissionKind.Seize, d.Visible[0].Id, 0f, 0f).Charged, 4, "seize 4 allocation");
            Advance(d, p, 40f);
            Advance(d, p, 125f);
            TestAssert.Eq(d.Held.Count, 1, "the building is held"); Near(d.Held[0].X, 400f, "at its place");
            TestAssert.Eq(t.State, TeamState.Ready, "the team is idle at the building");
            Near(d.Held[0].Until - p.Now, 600f - 5f, "held 10 minutes", 15f);
            // It is a base: exfil returns to the nearest of camp and building.
            // Retaken: ground forces within 300 m for 60 s.
            p.SceneAt = (x, z) => Math.Abs(x - 400f) < 5f ? new SofScene(2, 2, 2, 0, false) : default;
            Advance(d, p, 59f);
            TestAssert.Eq(d.Held.Count, 1, "59 s is not enough");
            Advance(d, p, 3f);
            TestAssert.Eq(d.Held.Count, 0, "retaken after 60 s with ground forces on it");
            // Expiry and loss.
            var q = new Ports();
            SofDesk e = Desk(q);
            ReadyTeam(e, q);
            Show(e, Seed(TargetKind.Building, AnchorSub.Uplink, 42, 300f, 0f));
            e.Send(Op, 0, MissionKind.Seize, e.Visible[0].Id, 0f, 0f);
            Advance(e, q, 200f);
            TestAssert.Eq(e.Held.Count, 1, "held again");
            q.BuildingUp = false;
            Advance(e, q, 2f);
            TestAssert.Eq(e.Held.Count, 0, "a destroyed building is no longer held");
            q.BuildingUp = true;
            e.Teams[0].Ammo = 100f;
            e.Send(Op, 0, MissionKind.Seize, e.Visible[0].Id, 0f, 0f);
            Advance(e, q, 200f);
            TestAssert.Eq(e.Held.Count, 1, "seized a second time");
            Advance(e, q, 601f);
            TestAssert.Eq(e.Held.Count, 0, "10 minutes and it lapses");
        }

        // ---- Helicopter lift -----------------------------------------------------------------------------

        private static void CheckLift()
        {
            var p = new Ports();
            SofDesk d = Desk(p);
            SofTeam t = ReadyTeam(d, p);
            TestAssert.Eq(d.Order(Op, 0, TeamVerb.Lift).Outcome, SofOutcome.Ordered, "lift requested");
            TestAssert.That(t.LiftWaiting, "waiting for a helicopter");
            // A helicopter lands 200 m away: too far.
            for (int i = 0; i < 12; i++) { p.Now += 1f; d.BeginHeliPass(); d.NoteHeli(1, Pilot, 200f, 0f, true); d.EndHeliPass(); }
            TestAssert.That(!t.Carried, "200 m is out of the pickup circle");
            // 100 m: boards after 10 s on the ground.
            for (int i = 0; i < 9; i++) { p.Now += 1f; d.BeginHeliPass(); d.NoteHeli(2, Pilot, 100f, 0f, true); d.EndHeliPass(); }
            TestAssert.That(!t.Carried, "9 s is not enough");
            p.Now += 1f; d.BeginHeliPass(); d.NoteHeli(2, Pilot, 100f, 0f, true); d.EndHeliPass();
            p.Now += 1f; d.BeginHeliPass(); d.NoteHeli(2, Pilot, 100f, 0f, true); d.EndHeliPass();
            TestAssert.That(t.Carried && t.Insert == Insertion.Helicopter, "the team rides the helicopter");
            // Tasked while carried: helicopter insertion adds 20 % odds.
            Show(d, Seed(TargetKind.Anchor, AnchorSub.Camp, 51, 5000f, 0f));
            TestAssert.Eq(d.Send(Op, 0, MissionKind.Sabotage, d.Visible[0].Id, 0f, 0f).Outcome, SofOutcome.Sent, "tasked from the air");
            // Flying: position follows the aircraft.
            p.Now += 1f; d.BeginHeliPass(); d.NoteHeli(2, Pilot, 2500f, 0f, false); d.EndHeliPass(); d.Tick();
            Near(t.X, 2500f, "the team rides along");
            // Lands 600 m away: no drop.
            for (int i = 0; i < 12; i++) { p.Now += 1f; d.BeginHeliPass(); d.NoteHeli(2, Pilot, 4400f, 0f, true); d.EndHeliPass(); d.Tick(); }
            TestAssert.That(t.Carried, "700 m from the objective is too far to drop");
            // Lands 200 m away: drops after 10 s with exposure 0, delivery pay.
            t.Exposure = 80f;
            for (int i = 0; i < 12; i++) { p.Now += 1f; d.BeginHeliPass(); d.NoteHeli(2, Pilot, 4900f, 0f, true); d.EndHeliPass(); d.Tick(); }
            TestAssert.That(!t.Carried, "dropped within 300 m of the objective");
            TestAssert.Eq(p.Paid.Count, 1, "one delivery pay"); TestAssert.Eq(p.Paid[0], 40, "40 CR per delivery");
            TestAssert.Eq(t.Exposure < 10f, true, "dropped with exposure about zero");
            // Odds with helicopter insertion.
            TestAssert.Eq(SofRules.Odds(0f, 0, true, false, false), 90, "+20 % for the helicopter");

            // Extraction: a team standing in the field is lifted back to the camp: 40 CR to the pilot.
            var q = new Ports();
            SofDesk e = Desk(q);
            SofTeam u = ReadyTeam(e, q);
            e.Divert(Op, 0, 700f, 0f);
            Advance(e, q, 100f);
            TestAssert.Eq(u.State, TeamState.Ready, "idle in the field");
            e.Order(Op, 0, TeamVerb.Lift);
            for (int i = 0; i < 12; i++) { q.Now += 1f; e.BeginHeliPass(); e.NoteHeli(3, Pilot, 700f, 20f, true); e.EndHeliPass(); e.Tick(); }
            TestAssert.That(u.Carried, "boarded in the field");
            for (int i = 0; i < 3; i++) { q.Now += 1f; e.BeginHeliPass(); e.NoteHeli(3, Pilot, 400f, 0f, false); e.EndHeliPass(); e.Tick(); }
            for (int i = 0; i < 12; i++) { q.Now += 1f; e.BeginHeliPass(); e.NoteHeli(3, Pilot, 50f, 0f, true); e.EndHeliPass(); e.Tick(); }
            TestAssert.That(!u.Carried && u.State == TeamState.Ready, "landed at the camp");
            TestAssert.Eq(q.Paid.Count, 0, "a 650 m hop is no extraction: nothing is paid");
            // A real extraction: 2.6 km out, lifted back to the camp: 40 CR to the pilot.
            e.Divert(Op, 0, 2700f, 0f);
            Advance(e, q, 300f);
            Near(u.X, 2700f, "out in the field", 5f);
            e.Order(Op, 0, TeamVerb.Lift);
            for (int i = 0; i < 12; i++) { q.Now += 1f; e.BeginHeliPass(); e.NoteHeli(5, Pilot, 2700f, 20f, true); e.EndHeliPass(); e.Tick(); }
            TestAssert.That(u.Carried, "boarded far out");
            for (int i = 0; i < 3; i++) { q.Now += 1f; e.BeginHeliPass(); e.NoteHeli(5, Pilot, 1400f, 0f, false); e.EndHeliPass(); e.Tick(); }
            for (int i = 0; i < 12; i++) { q.Now += 1f; e.BeginHeliPass(); e.NoteHeli(5, Pilot, 50f, 0f, true); e.EndHeliPass(); e.Tick(); }
            TestAssert.That(!u.Carried && u.State == TeamState.Ready, "landed at the camp again");
            TestAssert.Eq(q.Paid.Count, 1, "one extraction pay"); TestAssert.Eq(q.Paid[0], 40, "40 CR extraction");
            // A helicopter that vanishes with a team aboard loses it.
            e.Order(Op, 0, TeamVerb.Lift);
            for (int i = 0; i < 12; i++) { q.Now += 1f; e.BeginHeliPass(); e.NoteHeli(4, Pilot, 50f, 0f, true); e.EndHeliPass(); e.Tick(); }
            TestAssert.That(u.Carried, "boarded again");
            Advance(e, q, 6f);
            TestAssert.Eq(u.State, TeamState.Lost, "a vanished carrier loses the team");
        }

        // ---- NETWORK TAP -> CYBER trace ---------------------------------------------------------------

        private sealed class NoCyber : ICyberPorts
        {
            public float Now { get; set; } = 10f;
            public int Humans => 2;
            public int Owner => 1;
            public CyberOutcome TrySpend(ulong op, int cr, out int detail) { detail = 0; return CyberOutcome.None; }
            public void Refund(ulong op, int cr) { }
            public CyberOutcome PostPackage(ulong op, in CyberNode node, in PackageDef def, bool exploit, float effort) => CyberOutcome.None;
        }

        private static void CheckTap()
        {
            var ports = new NoCyber();
            var desk = new CyberDesk(ports, new CyberAnchorSet(1, 1));
            Near(desk.TraceFactor(ports.Now), 0.8f, "own data center -20 % trace");
            TestAssert.That(desk.Effects.Add(CyberPackages.Tap(1, 77, 600f, ports.Now)), "a tap is an effect");
            Near(desk.TraceFactor(ports.Now), 0.8f * 0.7f, "a tap cuts another 30 %");
            Near(desk.TraceFactor(ports.Now + 601f), 0.8f, "and lapses after 10 minutes");
            var other = new CyberDesk(new NoCyber { }, new CyberAnchorSet(1, 0));
            Near(other.TraceFactor(10f), 1f, "an untapped faction is unchanged");
            Near(desk.Effects.TraceCut(2, ports.Now), 1f, "the cut is the owner's only");
            desk.Effects.Add(CyberPackages.Tap(1, 77, 600f, ports.Now + 300f));
            TestAssert.Eq(desk.Effects.Count, 1, "a second tap of the same network refreshes, not stacks");
        }

        // ---- Review fixes (C1, C2, I1, I3, I4) ---------------------------------------------------------------

        private static void CheckReviewFixes()
        {
            // C2: a team engaged on a target keeps it through a lapsed sighting; only the unit dying (no seed / Gone) ends the mission, and then it refunds the order.
            var p = new Ports();
            SofDesk d = Desk(p);
            ReadyTeam(d, p);
            Show(d, Seed(TargetKind.Anchor, AnchorSub.Camp, 71, 6000f, 0f));
            int wallet = p.Wallet;
            TestAssert.Eq(d.Send(Op, 0, MissionKind.Sabotage, d.Visible[0].Id, 0f, 0f).Outcome, SofOutcome.Sent, "sent on a sighted camp");
            TestAssert.Eq(p.Wallet, wallet - 4, "the order is paid");
            for (int i = 0; i < 20; i++) { p.Now += 10f; d.Refresh(new[] { Seed(TargetKind.Anchor, AnchorSub.Camp, 71, 6000f, 0f, false) }); d.Tick(); }
            TestAssert.Eq(d.Teams[0].Mission, MissionKind.Sabotage, "an unsighted but engaged target keeps its mission");
            TestAssert.Eq(d.Visible.Count, 1, "and stays listed");
            var keep = new HashSet<uint>();
            d.CollectKeep(keep);
            TestAssert.That(keep.Contains(71u) && keep.Count == 1, "the runtime is told to keep resolving the engaged key");
            d.Refresh(new SofSeed[0]);
            TestAssert.Eq(d.Teams[0].Mission, MissionKind.None, "the unit vanishing ends the mission");
            TestAssert.Eq(p.Wallet, wallet, "refunded: the team never arrived");
            TestAssert.Eq(d.Ids.Count, 0, "the id is released");

            // The same loss after arrival is not refunded and never counts as a success.
            var q = new Ports();
            SofDesk e = Desk(q);
            ReadyTeam(e, q);
            Show(e, Seed(TargetKind.Anchor, AnchorSub.Camp, 72, 300f, 0f));
            var events = new List<SofEventKind>();
            e.Happened += ev => events.Add(ev.Kind);
            int before = q.Wallet;
            e.Send(Op, 0, MissionKind.Sabotage, e.Visible[0].Id, 0f, 0f);
            Advance(e, q, 40f);
            TestAssert.Eq(e.Teams[0].State, TeamState.OnSite, "on site");
            q.Alive = false;
            Advance(e, q, 3f);
            TestAssert.Eq(e.Teams[0].State, TeamState.Ready, "the target is gone: the team stands down");
            TestAssert.That(!events.Contains(SofEventKind.Success), "a target that vanished is never a success");
            TestAssert.That(!q.Log.Contains("sabotage Camp"), "and no effect ran");
            TestAssert.Eq(q.Wallet, before - 4, "no refund once on site");

            // C2: a LASE whose designation cannot be placed sets no LASE state and posts nothing.
            var l = new Ports { LaseOk = false };
            SofDesk g = Desk(l);
            ReadyTeam(g, l);
            Show(g, Seed(TargetKind.Ground, AnchorSub.Uplink, 73, 300f, 0f));
            var gEvents = new List<SofEventKind>();
            g.Happened += ev => gEvents.Add(ev.Kind);
            g.Send(Op, 0, MissionKind.Lase, g.Visible[0].Id, 0f, 0f);
            Advance(g, l, 40f);
            TestAssert.That(!g.Teams[0].LaseActive && !g.IsLasing(0), "no laser, no LASE state");
            TestAssert.That(!l.Log.Contains("lasepost0"), "no LASE post");
            TestAssert.That(gEvents.Contains(SofEventKind.Failed) && !gEvents.Contains(SofEventKind.Success), "the mission failed, it did not succeed");
            TestAssert.Eq(g.Teams[0].State, TeamState.Ready, "the team is free again");

            // I1: ids exist only for visible targets, and are recycled.
            var r = new Ports();
            SofDesk h = Desk(r);
            var hidden = new List<SofSeed>();
            for (uint i = 0; i < 300; i++) hidden.Add(Seed(TargetKind.Ground, AnchorSub.Uplink, 5000 + i, 100f * i, 0f, false));
            h.Refresh(hidden);
            TestAssert.Eq(h.Ids.Count, 0, "300 hidden units take no id");
            TestAssert.Eq(h.Visible.Count, 0, "and none is listed");
            var seen = new List<SofSeed>();
            for (uint i = 0; i < 40; i++) seen.Add(Seed(TargetKind.Ground, AnchorSub.Uplink, 6000 + i, 100f * i, 0f, true));
            h.Refresh(seen);
            TestAssert.Eq(h.Ids.Count, 40, "a sighted unit takes an id");
            TestAssert.Eq(h.Visible.Count, SofDesk.MaxVisible, "the list stays capped");
            r.Now += 200f;
            h.Refresh(hidden);
            TestAssert.Eq(h.Ids.Count, 0, "ids of targets no longer visible are released");
            for (int round = 0; round < 10; round++)
            {
                var wave = new List<SofSeed>();
                for (uint i = 0; i < 100; i++) wave.Add(Seed(TargetKind.Ground, AnchorSub.Uplink, 10000u + (uint)round * 1000u + i, 50f * i, 0f, true));
                h.Refresh(wave);
                r.Now += 200f;
            }
            TestAssert.Eq(h.Ids.Count <= SofTargetIds.Capacity, true, "ten waves of 100 never exhaust the table");
            h.Refresh(new[] { Seed(TargetKind.Ground, AnchorSub.Uplink, 99999, 0f, 0f, true) });
            TestAssert.Eq(h.Visible.Count, 1, "a new target still gets an id after the churn");
            // A recycled id must not carry the old target's sighting.
            var ids = new SofTargetIds();
            int first = ids.GetOrAdd(TargetKind.Ground, AnchorSub.Uplink, 1);
            TestAssert.That(ids.Release(first) && ids.GetOrAdd(TargetKind.Ground, AnchorSub.Uplink, 2) == first, "a released id is reused");
            TestAssert.That(!ids.TryKey(99, out _, out _, out _) && ids.Find(TargetKind.Ground, AnchorSub.Uplink, 1) == 0, "the old key no longer resolves");

            // I4: a COVER pay needs the claimant's own kill near the team while it was pinned.
            var c = new Ports();
            SofDesk k = Desk(c);
            SofTeam t = ReadyTeam(k, c);
            k.Divert(Op, 0, 20000f, 0f);
            c.SceneAt = (x, z) => new SofScene(0, 3, 90, 1, false);
            Advance(k, c, 8f);
            TestAssert.Eq(t.State, TeamState.Pinned, "pinned");
            TestAssert.That(k.CoverClaimed(0, Pilot), "claimed");
            k.CoverKill(t.X, t.Z); k.CoverKill(t.X, t.Z); k.CoverKill(t.X, t.Z); k.CoverKill(t.X, t.Z); // somebody else's kills relieve the team
            c.SceneAt = (x, z) => default;
            Advance(k, c, 5f);
            TestAssert.Eq(t.State, TeamState.Moving, "relieved: the team goes on");
            TestAssert.Eq(c.Paid.Count, 0, "but a claimant with no kill is not paid");
            TestAssert.Eq(k.CoverKillBy(Pilot, t.X, t.Z), 0, "a kill while the team is not pinned counts for nothing");

            // I3 is covered in CheckLift (a 650 m hop pays nothing, a 2.6 km extraction pays 40).
        }

        // ---- Anti-oracle ---------------------------------------------------------------------------------

        private static void CheckOracle()
        {
            var p = new Ports();
            SofDesk d = Desk(p);
            ReadyTeam(d, p);
            // A hidden target (never sighted) is not listed; an id for it, a wrong-kind id and a made-up id all answer the same.
            d.Refresh(new[] { Seed(TargetKind.Anchor, AnchorSub.Camp, 61, 500f, 0f, false), Seed(TargetKind.Anchor, AnchorSub.Camp, 62, 600f, 0f, true) });
            TestAssert.Eq(d.Visible.Count, 1, "only the sighted one is listed");
            int hidden = d.Ids.GetOrAdd(TargetKind.Anchor, AnchorSub.Camp, 61);
            SofResult a = d.Send(Op, 0, MissionKind.Sabotage, hidden, 0f, 0f);
            SofResult b = d.Send(Op, 0, MissionKind.Sabotage, 9999, 0f, 0f);
            SofResult c = d.Send(Op, 0, MissionKind.Seize, d.Visible[0].Id, 0f, 0f);
            TestAssert.Eq(a.Outcome, SofOutcome.NoTarget, "hidden"); TestAssert.Eq(b.Outcome, SofOutcome.NoTarget, "made up"); TestAssert.Eq(c.Outcome, SofOutcome.NoTarget, "wrong mission for the kind");
            TestAssert.That(a.Charged == 0 && b.Charged == 0 && c.Charged == 0 && p.Wallet == 940, "a refused order takes nothing");
            TestAssert.Eq(a.Words, b.Words, "identical words");
            // A target stays listed while a team is engaged on it, even without a fresh sighting.
            Show(d, Seed(TargetKind.Anchor, AnchorSub.Camp, 62, 600f, 0f, false));
            TestAssert.That(d.Visible.Count == 1, "the sighting lingers 90 s");
            p.Now += 100f;
            Show(d, Seed(TargetKind.Anchor, AnchorSub.Camp, 62, 600f, 0f, false));
            TestAssert.Eq(d.Visible.Count, 0, "then it is gone");
            // Gone seeds drop the mission.
            Show(d, Seed(TargetKind.Anchor, AnchorSub.Camp, 62, 600f, 0f, true));
            d.Send(Op, 0, MissionKind.Sabotage, d.Visible[0].Id, 0f, 0f);
            d.Refresh(new[] { new SofSeed(TargetKind.Anchor, AnchorSub.Camp, 62, 600f, 0f, 0f, false, true) });
            TestAssert.Eq(d.Teams[0].Mission, MissionKind.None, "a target that dies ends the mission");
            TestAssert.Eq(p.Wallet, 940 - 4 + 4, "and a target lost before the team arrived refunds the order");
            // Scene reset.
            d.Reset();
            TestAssert.Eq(d.ActiveCount, 0, "reset clears the teams");
        }
    }
}
