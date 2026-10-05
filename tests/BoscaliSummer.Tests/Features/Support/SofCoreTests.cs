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
        }

        private static void Eq<T>(T actual, T expected, string message) =>
            TestAssert.That(Equals(actual, expected), message + " (got " + actual + ", want " + expected + ")");

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
            public bool Alive = true, BuildingUp = true;

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
            public void LaseBegin(int slot, uint key, float x, float z) => Log.Add("lase+" + slot);
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
            Eq(d.Teams[r.Slot].State, TeamState.Ready, "deployed");
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
            Eq(SofRules.Odds(0f, 0, false, false, false), 70, "base odds 70");
            Eq(SofRules.Odds(50f, 0, false, false, false), 45, "exposure halves into the odds");
            Eq(SofRules.Odds(0f, 2, false, false, false), 50, "10 per armoured unit within 1 km");
            Eq(SofRules.Odds(0f, 0, true, false, false), 90, "+20 helicopter insertion");
            Eq(SofRules.Odds(0f, 0, false, true, false), 95, "+25 EXPLOIT clamps at 95");
            Eq(SofRules.Odds(0f, 0, true, true, true), 95, "everything clamps at 95");
            Eq(SofRules.Odds(100f, 9, false, false, false), 10, "a bad spot clamps at 10");
            Eq(SofRules.Odds(0f, 0, false, false, true), 85, "+15 when CYBER holds a node within 12 km");
            Eq(SofRules.Odds(float.NaN, 0, false, false, false), 20, "an unreadable exposure reads as 100 %");
            Near(SofRules.ExposureDelta(2, false, false, false, 1f), 2f, "two enemies add 2 %/s");
            Near(SofRules.ExposureDelta(1, true, false, false, 1f), 1.25f, "a bird's stare x1.25");
            Near(SofRules.ExposureDelta(1, false, true, false, 1f), 1.5f, "PUSH +50 %");
            Near(SofRules.ExposureDelta(0, false, false, false, 2f), -1f, "nothing near falls 0.5 %/s");
            Near(SofRules.ExposureDelta(0, false, false, true, 1f), -1f, "HOLD falls twice as fast");
            Near(SofRules.SpeedMetresPerSecond * 3.6f, 35f, "35 km/h");
            Eq(SofRules.TeamCap(2, false), 2, "two teams"); Eq(SofRules.TeamCap(5, false), 3, "three at 5 humans"); Eq(SofRules.TeamCap(5, true), 4, "FOB adds one");
            Eq(SofRules.Callsign(0), "A-1", "callsign A"); Eq(SofRules.Callsign(3), "D-1", "callsign D");
            Eq(SofRules.CostOf(MissionKind.Sabotage, false), 60, "sabotage 60"); Eq(SofRules.CostOf(MissionKind.Sabotage, true), 45, "EXPLOIT -25 %");
            Eq(SofRules.CostOf(MissionKind.Recon, false), 25, "recon 25");
            TestAssert.That(SofRules.Valid(MissionKind.Lase, TargetKind.Ground, AnchorSub.Uplink) && !SofRules.Valid(MissionKind.Lase, TargetKind.Building, AnchorSub.Uplink), "lase takes ground only");
            TestAssert.That(SofRules.Valid(MissionKind.Tap, TargetKind.Relay, AnchorSub.Uplink) && SofRules.Valid(MissionKind.Tap, TargetKind.Anchor, AnchorSub.DataCenter) && !SofRules.Valid(MissionKind.Tap, TargetKind.Anchor, AnchorSub.EwTruck), "tap takes a relay or a data center");
            TestAssert.That(!SofRules.Valid(MissionKind.Recon, TargetKind.Ground, AnchorSub.Uplink), "recon takes a point, never a target id");
            TestAssert.That(SofRules.Exploit(TargetKind.Anchor, AnchorSub.EwTruck) && SofRules.Exploit(TargetKind.Relay, AnchorSub.Uplink) && !SofRules.Exploit(TargetKind.Anchor, AnchorSub.Camp), "SOF beats CYBER only");
            TestAssert.That(SofRules.Resisted(TargetKind.Anchor, AnchorSub.Uplink) && !SofRules.Resisted(TargetKind.Ground, AnchorSub.Uplink), "SPACE resists SOF");
            Eq(CampRules.RaiseFactor(AnchorHealth.Damaged), 1.5f, "damaged camp x1.5");
        }

        private static void CheckCampsAndIds()
        {
            var camps = new SofCampSet(5);
            Eq(camps.Count, 2, "two camps at most");
            camps.SetPosition(0, 100f, 0f); camps.SetPosition(1, 5000f, 0f);
            camps.Set(0, 1f, false, 0f); camps.Set(1, 0.4f, false, 0f);
            Eq(camps.Health(1), AnchorHealth.Damaged, "damaged");
            TestAssert.That(camps.TryBest(out int best, out float bx, out _, out AnchorHealth h) && best == 0 && h == AnchorHealth.Live && bx == 100f, "a live camp is preferred");
            camps.Set(0, 0f, true, 50f);
            TestAssert.That(camps.TryBest(out best, out _, out _, out h) && best == 1 && h == AnchorHealth.Damaged, "a damaged camp raises when none is live");
            TestAssert.That(!camps.PastGrace(0, 100f) && camps.PastGrace(0, 171f), "120 s grace");
            camps.Set(1, 0f, true, 60f);
            TestAssert.That(!camps.TryBest(out _, out _, out _, out _), "no camp standing");
            camps.Restore(0, 120f, 0f, 200f);
            Eq(camps.Health(0), AnchorHealth.Live, "restored");
            Near(camps.NearestStanding(1120f, 0f, out float nx, out _), 1000f, "nearest standing camp"); Eq(nx, 120f, "its x");
            var ids = new SofTargetIds();
            int a = ids.GetOrAdd(TargetKind.Ground, AnchorSub.Uplink, 5), b = ids.GetOrAdd(TargetKind.Anchor, AnchorSub.Camp, 5);
            TestAssert.That(a == 1 && b == 2 && ids.GetOrAdd(TargetKind.Ground, AnchorSub.Uplink, 5) == 1, "ids are stable per (kind, sub, key)");
            TestAssert.That(ids.TryKey(b, out TargetKind k, out AnchorSub s, out uint key) && k == TargetKind.Anchor && s == AnchorSub.Camp && key == 5u, "id resolves");
            TestAssert.That(!ids.TryKey(0, out _, out _, out _) && !ids.TryKey(99, out _, out _, out _), "unknown ids resolve to nothing");
            for (uint i = 0; i < 300; i++) ids.GetOrAdd(TargetKind.Ground, AnchorSub.Uplink, 1000 + i);
            Eq(ids.Count, SofTargetIds.Capacity, "the table is bounded");
            Eq(ids.GetOrAdd(TargetKind.Ground, AnchorSub.Uplink, 99999), 0, "a full table answers 0");
        }

        // ---- Raise ---------------------------------------------------------------------------------

        private static void CheckRaise()
        {
            var p = new Ports();
            Eq(new SofDesk(p, new SofCampSet(0)).Raise(Op).Outcome, SofOutcome.NoCamp, "no camp, no SOF");
            SofDesk d = Desk(p);
            d.Camps.Set(0, 0f, true, 0f);
            Eq(d.Raise(Op).Outcome, SofOutcome.CampDown, "a down camp raises nothing");
            d.Camps.Set(0, 1f, false, 0f);
            SofResult r = d.Raise(Op);
            Eq(r.Outcome, SofOutcome.Raised, "raised"); Eq(r.Charged, 60, "60 CR"); Eq(p.Wallet, 940, "wallet charged"); Eq(r.Detail, 90, "90 s");
            Eq(d.Teams[0].State, TeamState.Raising, "raising");
            Eq(d.Send(Op, 0, MissionKind.Recon, 0, 100f, 100f).Outcome, SofOutcome.Raising, "orders wait for the deploy");
            Advance(d, p, 89f);
            Eq(d.Teams[0].State, TeamState.Raising, "still raising at 89 s");
            Advance(d, p, 2f);
            Eq(d.Teams[0].State, TeamState.Ready, "ready after 90 s");
            Eq(d.Raise(Op).Outcome, SofOutcome.Raised, "second team"); Eq(d.Raise(Op).Outcome, SofOutcome.TeamCap, "cap 2 at two humans");
            p.Humans = 5;
            Eq(d.Raise(Op).Outcome, SofOutcome.Raised, "third team at 5 humans");
            var poor = new Ports { Wallet = 59 };
            SofDesk d2 = Desk(poor);
            SofResult low = d2.Raise(Op);
            Eq(low.Outcome, SofOutcome.LowCredit, "short wallet"); Eq(low.Detail, 60, "need 60"); Eq(d2.ActiveCount, 0, "no team for a refused raise"); Eq(poor.Wallet, 59, "nothing taken");
            var slow = new Ports();
            SofDesk d3 = Desk(slow);
            d3.Camps.Set(0, 0.5f, false, 0f);
            Eq(d3.Raise(Op).Detail, 135, "a damaged camp raises 1.5x slower");
        }

        // ---- Route ---------------------------------------------------------------------------------

        private static void CheckRoute()
        {
            var p = new Ports();
            SofDesk d = Desk(p);
            SofTeam t = ReadyTeam(d, p);
            Eq(d.Divert(Op, 0, 3500f, 0f).Outcome, SofOutcome.Diverted, "divert from camp");
            Advance(d, p, 100f);
            Near(t.X, 972.2f, "35 km/h for 100 s", 15f);
            TestAssert.That(d.Order(Op, 0, TeamVerb.Push).Ok, "push");
            float before = t.X; Advance(d, p, 10f);
            Near(t.X - before, 145.8f, "PUSH x1.5 for 10 s", 15f);
            TestAssert.That(d.Order(Op, 0, TeamVerb.Hold).Ok && t.HoldOn && !t.PushOn, "HOLD replaces PUSH");
            before = t.X; Advance(d, p, 10f);
            Eq(t.X, before, "HOLD stops the team");
            d.Order(Op, 0, TeamVerb.Hold);
            Advance(d, p, 400f);
            Eq(t.State, TeamState.Ready, "arrived and idle"); Near(t.X, 3500f, "at the point", 1f);
            Eq(d.Order(Op, 0, TeamVerb.Exfil).Outcome, SofOutcome.Ordered, "exfil");
            Eq(t.State, TeamState.Returning, "returning");
            Advance(d, p, 400f);
            Eq(t.State, TeamState.Ready, "home again"); Near(t.X, 0f, "back at the camp", 1f);
            Eq(d.Order(Op, 0, TeamVerb.Exfil).Outcome, SofOutcome.BadOrder, "no exfil from the camp");
            Eq(d.Divert(Op, 0, float.NaN, 0f).Outcome, SofOutcome.OutOfTheater, "a NaN point is refused");
            Eq(d.Divert(Op, 0, 9e9f, 0f).Outcome, SofOutcome.OutOfTheater, "a far point is refused");
            Eq(d.Divert(Op, 3, 1f, 1f).Outcome, SofOutcome.NoTeam, "no such team");
            Eq(d.Order(Op, 0, TeamVerb.Push).Outcome, SofOutcome.BadOrder, "push needs a moving team");
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
            Advance(d, p, 40f);
            Eq(t.State, TeamState.Pinned, "pinned at 100 %"); Near(t.Exposure, 100f, "full exposure");
            TestAssert.That(p.Log.Contains("cover0"), "the pin posts a COVER request");
            float x0 = t.X; Advance(d, p, 5f);
            Eq(t.X, x0, "a pinned team does not move");
            Eq(d.Divert(Op, 0, 1f, 1f).Outcome, SofOutcome.Pinned, "no orders while pinned");
            TestAssert.That(!d.CoverClaimed(2, Pilot) && d.CoverClaimed(0, Pilot), "only a pinned team can be covered");
            p.SceneAt = (x, z) => new SofScene(0, 3, 4, 1, false);
            Advance(d, p, 80f);
            Eq(t.State, TeamState.Pinned, "cover extends the lost timer");
            for (int i = 0; i < 2; i++) Eq(d.CoverKill(t.X, t.Z), 1, "a kill within 2 km covers the team");
            Eq(d.CoverKill(t.X + 5000f, t.Z), 0, "a kill far away does not");
            Near(t.Exposure, 50f, "each kill relieves 25 points", 6f);
            p.SceneAt = (x, z) => default;
            Advance(d, p, 5f);
            Eq(t.State, TeamState.Moving, "below 60 % the team breaks contact and goes on");
            TestAssert.That(p.Paid.Contains(SofRules.CoverPay), "the covering pilot is paid");

            var q = new Ports();
            SofDesk e = Desk(q);
            SofTeam u = ReadyTeam(e, q);
            e.Divert(Op, 0, 20000f, 0f);
            q.SceneAt = (x, z) => new SofScene(0, 9, 60, 0, false);
            Advance(e, q, 3f);
            Eq(u.State, TeamState.Pinned, "pinned fast");
            Advance(e, q, 110f);
            Eq(u.State, TeamState.Pinned, "not yet lost");
            Advance(e, q, 15f);
            Eq(u.State, TeamState.Lost, "120 s pinned without cover loses the team");
            Advance(e, q, 31f);
            Eq(e.Teams[0].Active, false, "the slot frees after the lost marker");
        }

        // ---- Missions ---------------------------------------------------------------------------------

        private static void CheckMissions()
        {
            // RECON: a point, 60 s on site, reveals 2 km for 5 minutes.
            var p = new Ports();
            SofDesk d = Desk(p);
            SofTeam t = ReadyTeam(d, p);
            Eq(d.Send(Op, 0, MissionKind.Recon, 0, 900f, 0f).Outcome, SofOutcome.Sent, "recon sent"); Eq(p.Wallet, 940 - 25, "25 CR");
            Eq(d.Send(Op, 0, MissionKind.Recon, 0, 1f, 1f).Outcome, SofOutcome.NotReady, "a tasked team is not ready");
            Advance(d, p, 100f);
            Eq(t.State, TeamState.OnSite, "on site");
            Advance(d, p, 61f);
            Eq(t.State, TeamState.Ready, "done");
            TestAssert.That(p.Log.Contains("reveal 2000 300"), "recon reveals 2 km for 300 s");
            Near(t.Ammo, 70f, "a mission costs 30 ammo");

            // SABOTAGE on an EW truck is an EXPLOIT target: cheaper and stronger; odds roll once.
            var s = new Ports();
            SofDesk e = Desk(s);
            SofTeam u = ReadyTeam(e, s);
            Show(e, Seed(TargetKind.Anchor, AnchorSub.EwTruck, 11, 700f, 0f));
            int truck = e.Visible[0].Id;
            Eq(e.Send(Op, 0, MissionKind.Sabotage, truck, 0f, 0f).Charged, 45, "EXPLOIT -25 % (60 -> 45)");
            Advance(e, s, 80f);
            Advance(e, s, 91f);
            TestAssert.That(s.Log.Contains("sabotage EwTruck"), "the anchor goes down through the host call");
            Eq(s.LastChance, 95, "odds 70 + 25 EXPLOIT clamp 95");
            Eq(u.State, TeamState.Ready, "ready again");

            // A failed roll: wounded, returns, recovers 60 s; high exposure loses it.
            var f = new Ports { RollResult = false };
            SofDesk g = Desk(f);
            SofTeam w = ReadyTeam(g, f);
            Show(g, Seed(TargetKind.Anchor, AnchorSub.Uplink, 12, 700f, 0f));
            g.Send(Op, 0, MissionKind.Sabotage, g.Visible[0].Id, 0f, 0f);
            Advance(g, f, 180f);
            Eq(w.State, TeamState.Returning, "a failed raid sends the team home"); TestAssert.That(w.Wounded, "wounded");
            Advance(g, f, 80f);
            Eq(w.State, TeamState.Recovering, "WIA recovery at the camp");
            Eq(g.Send(Op, 0, MissionKind.Recon, 0, 5f, 5f).Outcome, SofOutcome.Wounded, "no orders while wounded");
            Advance(g, f, 61f);
            Eq(w.State, TeamState.Ready, "recovered after 60 s");
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
            Eq(z.State, TeamState.Lost, "a failure at exposure >= 90 loses the team");

            // LASE: continuous, posts a LASE request, ends at the cap or when the target dies; no roll.
            var l = new Ports();
            SofDesk m = Desk(l);
            SofTeam y = ReadyTeam(m, l);
            Show(m, Seed(TargetKind.Ground, AnchorSub.Uplink, 21, 400f, 0f));
            Eq(m.Send(Op, 0, MissionKind.Lase, m.Visible[0].Id, 0f, 0f).Outcome, SofOutcome.Sent, "lase sent");
            Advance(m, l, 60f);
            Eq(y.State, TeamState.OnSite, "lasing"); TestAssert.That(l.Log.Contains("lase+0") && l.Log.Contains("lasepost0"), "laser on and a LASE post");
            TestAssert.That(m.TryLase(out int slot, out float lx, out _) && slot == 0 && lx == 400f, "the lased point is the AIM: TEAM source");
            Advance(m, l, 200f);
            Eq(y.State, TeamState.OnSite, "still lasing at 200 s");
            l.Alive = false;
            Advance(m, l, 2f);
            Eq(y.State, TeamState.Ready, "the target died: the mission ends"); TestAssert.That(l.Log.Contains("lase-0"), "laser off"); Eq(l.LastChance, 0, "lase never rolls");
            Eq(m.Send(Op, 0, MissionKind.Lase, 12345, 0f, 0f).Outcome, SofOutcome.NoTarget, "a made-up target id");

            // TAP: 10 minutes, x1.5 on a data center (EXPLOIT); a relay too.
            var k = new Ports();
            SofDesk n = Desk(k);
            ReadyTeam(n, k);
            Show(n, Seed(TargetKind.Relay, AnchorSub.Uplink, 31, 300f, 0f));
            Eq(n.Send(Op, 0, MissionKind.Tap, n.Visible[0].Id, 0f, 0f).Charged, 30, "tap 40 -> 30 EXPLOIT");
            Advance(n, k, 140f);
            TestAssert.That(k.Log.Contains("tap 900"), "tap 10 min x1.5 EXPLOIT");
            Eq(n.Send(Op, 0, MissionKind.Seize, n.Visible[0].Id, 0f, 0f).Outcome, SofOutcome.NoTarget, "a relay is no building");

            // No ammo.
            var a = new Ports();
            SofDesk o = Desk(a);
            SofTeam q = ReadyTeam(o, a);
            q.Ammo = 10f;
            Eq(o.Send(Op, 0, MissionKind.Recon, 0, 5f, 5f).Outcome, SofOutcome.NoAmmo, "no ammo, no mission");
            Advance(o, a, 1f);
            q.Ammo = 100f;
            a.Wallet = 10;
            Eq(o.Send(Op, 0, MissionKind.Recon, 0, 5f, 5f).Outcome, SofOutcome.LowCredit, "low credit");
            Eq(a.Wallet, 10, "nothing taken");
        }

        // ---- Held buildings ---------------------------------------------------------------------------

        private static void CheckHeld()
        {
            var p = new Ports();
            SofDesk d = Desk(p);
            SofTeam t = ReadyTeam(d, p);
            Show(d, Seed(TargetKind.Building, AnchorSub.Uplink, 41, 400f, 0f));
            Eq(d.Send(Op, 0, MissionKind.Seize, d.Visible[0].Id, 0f, 0f).Charged, 50, "seize 50 CR");
            Advance(d, p, 40f);
            Advance(d, p, 125f);
            Eq(d.Held.Count, 1, "the building is held"); Near(d.Held[0].X, 400f, "at its place");
            Eq(t.State, TeamState.Ready, "the team is idle at the building");
            Near(d.Held[0].Until - p.Now, 600f - 5f, "held 10 minutes", 15f);
            // It is a base: exfil returns to the nearest of camp and building.
            // Retaken: ground forces within 300 m for 60 s.
            p.SceneAt = (x, z) => Math.Abs(x - 400f) < 5f ? new SofScene(2, 2, 2, 0, false) : default;
            Advance(d, p, 59f);
            Eq(d.Held.Count, 1, "59 s is not enough");
            Advance(d, p, 3f);
            Eq(d.Held.Count, 0, "retaken after 60 s with ground forces on it");
            // Expiry and loss.
            var q = new Ports();
            SofDesk e = Desk(q);
            ReadyTeam(e, q);
            Show(e, Seed(TargetKind.Building, AnchorSub.Uplink, 42, 300f, 0f));
            e.Send(Op, 0, MissionKind.Seize, e.Visible[0].Id, 0f, 0f);
            Advance(e, q, 200f);
            Eq(e.Held.Count, 1, "held again");
            q.BuildingUp = false;
            Advance(e, q, 2f);
            Eq(e.Held.Count, 0, "a destroyed building is no longer held");
            q.BuildingUp = true;
            e.Teams[0].Ammo = 100f;
            e.Send(Op, 0, MissionKind.Seize, e.Visible[0].Id, 0f, 0f);
            Advance(e, q, 200f);
            Eq(e.Held.Count, 1, "seized a second time");
            Advance(e, q, 601f);
            Eq(e.Held.Count, 0, "10 minutes and it lapses");
        }

        // ---- Helicopter lift -----------------------------------------------------------------------------

        private static void CheckLift()
        {
            var p = new Ports();
            SofDesk d = Desk(p);
            SofTeam t = ReadyTeam(d, p);
            Eq(d.Order(Op, 0, TeamVerb.Lift).Outcome, SofOutcome.Ordered, "lift requested");
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
            Eq(d.Send(Op, 0, MissionKind.Sabotage, d.Visible[0].Id, 0f, 0f).Outcome, SofOutcome.Sent, "tasked from the air");
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
            Eq(p.Paid.Count, 1, "one delivery pay"); Eq(p.Paid[0], 40, "40 CR per delivery");
            Eq(t.Exposure < 10f, true, "dropped with exposure about zero");
            // Odds with helicopter insertion.
            Eq(SofRules.Odds(0f, 0, true, false, false), 90, "+20 % for the helicopter");

            // Extraction: a team standing in the field is lifted back to the camp: 40 CR to the pilot.
            var q = new Ports();
            SofDesk e = Desk(q);
            SofTeam u = ReadyTeam(e, q);
            e.Divert(Op, 0, 700f, 0f);
            Advance(e, q, 100f);
            Eq(u.State, TeamState.Ready, "idle in the field");
            e.Order(Op, 0, TeamVerb.Lift);
            for (int i = 0; i < 12; i++) { q.Now += 1f; e.BeginHeliPass(); e.NoteHeli(3, Pilot, 700f, 20f, true); e.EndHeliPass(); e.Tick(); }
            TestAssert.That(u.Carried, "boarded in the field");
            for (int i = 0; i < 3; i++) { q.Now += 1f; e.BeginHeliPass(); e.NoteHeli(3, Pilot, 400f, 0f, false); e.EndHeliPass(); e.Tick(); }
            for (int i = 0; i < 12; i++) { q.Now += 1f; e.BeginHeliPass(); e.NoteHeli(3, Pilot, 50f, 0f, true); e.EndHeliPass(); e.Tick(); }
            TestAssert.That(!u.Carried && u.State == TeamState.Ready, "landed at the camp");
            Eq(q.Paid.Count, 1, "still one pay");
            Eq(q.Paid.Count, 1, "one extraction pay"); Eq(q.Paid[0], 40, "40 CR extraction");
            // A helicopter that vanishes with a team aboard loses it.
            e.Order(Op, 0, TeamVerb.Lift);
            for (int i = 0; i < 12; i++) { q.Now += 1f; e.BeginHeliPass(); e.NoteHeli(4, Pilot, 50f, 0f, true); e.EndHeliPass(); e.Tick(); }
            TestAssert.That(u.Carried, "boarded again");
            Advance(e, q, 6f);
            Eq(u.State, TeamState.Lost, "a vanished carrier loses the team");
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
            Eq(desk.Effects.Count, 1, "a second tap of the same network refreshes, not stacks");
        }

        // ---- Anti-oracle ---------------------------------------------------------------------------------

        private static void CheckOracle()
        {
            var p = new Ports();
            SofDesk d = Desk(p);
            ReadyTeam(d, p);
            // A hidden target (never sighted) is not listed; an id for it, a wrong-kind id and a made-up id all answer the same.
            d.Refresh(new[] { Seed(TargetKind.Anchor, AnchorSub.Camp, 61, 500f, 0f, false), Seed(TargetKind.Anchor, AnchorSub.Camp, 62, 600f, 0f, true) });
            Eq(d.Visible.Count, 1, "only the sighted one is listed");
            int hidden = d.Ids.GetOrAdd(TargetKind.Anchor, AnchorSub.Camp, 61);
            SofResult a = d.Send(Op, 0, MissionKind.Sabotage, hidden, 0f, 0f);
            SofResult b = d.Send(Op, 0, MissionKind.Sabotage, 9999, 0f, 0f);
            SofResult c = d.Send(Op, 0, MissionKind.Seize, d.Visible[0].Id, 0f, 0f);
            Eq(a.Outcome, SofOutcome.NoTarget, "hidden"); Eq(b.Outcome, SofOutcome.NoTarget, "made up"); Eq(c.Outcome, SofOutcome.NoTarget, "wrong mission for the kind");
            TestAssert.That(a.Charged == 0 && b.Charged == 0 && c.Charged == 0 && p.Wallet == 940, "a refused order takes nothing");
            Eq(a.Words, b.Words, "identical words");
            // A target stays listed while a team is engaged on it, even without a fresh sighting.
            Show(d, Seed(TargetKind.Anchor, AnchorSub.Camp, 62, 600f, 0f, false));
            TestAssert.That(d.Visible.Count == 1, "the sighting lingers 90 s");
            p.Now += 100f;
            Show(d, Seed(TargetKind.Anchor, AnchorSub.Camp, 62, 600f, 0f, false));
            Eq(d.Visible.Count, 0, "then it is gone");
            // Gone seeds drop the mission.
            Show(d, Seed(TargetKind.Anchor, AnchorSub.Camp, 62, 600f, 0f, true));
            d.Send(Op, 0, MissionKind.Sabotage, d.Visible[0].Id, 0f, 0f);
            d.Refresh(new[] { new SofSeed(TargetKind.Anchor, AnchorSub.Camp, 62, 600f, 0f, 0f, false, true) });
            Eq(d.Teams[0].Mission, MissionKind.None, "a target that dies ends the mission");
            // Scene reset.
            d.Reset();
            Eq(d.ActiveCount, 0, "reset clears the teams");
        }
    }
}
