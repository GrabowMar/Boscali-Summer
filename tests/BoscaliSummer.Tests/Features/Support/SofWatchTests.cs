using System;
using System.Collections.Generic;
using BoscaliSummer.Modules.Support.Domain.Cyber;
using BoscaliSummer.Modules.Support.Domain.Ops;
using BoscaliSummer.Modules.Support.Domain.Sof;
using BoscaliSummer.Modules.Support.Domain.Space;

namespace BoscaliSummer.Tests.Features.Support
{
    /// <summary>M6b WATCH OFFICER OVERLORD for SOF: one team in the field, RAISE on a LIVE camp, LASE and RECON on high-value revealed contacts, SABOTAGE at 55 % odds, PUSH / HOLD / EXFIL by exposure, no lift, the idle rule and the pacing bound (spec section 4).</summary>
    internal static class SofWatchTests
    {
        private const ulong Human = 111UL, Overlord = SpaceContacts.WatchOfficerId;

        public static void Run()
        {
            CheckRaiseGates();
            CheckMissionChoice();
            CheckSteering();
            CheckIdleReserveAndPacing();
            CheckNeverLifts();
        }

        private static void Eq<T>(T actual, T expected, string message) =>
            TestAssert.That(Equals(actual, expected), message + " (got " + actual + ", want " + expected + ")");

        private sealed class Ports : ISofPorts
        {
            public float Clock = 100f; public int HumanCount = 1, Wallet = 1000, Charged;
            public bool Fob { get; set; }
            public Func<float, float, SofScene> SceneAt = (x, z) => default;
            public readonly List<string> Log = new List<string>();
            public float Now => Clock;
            public int Humans => HumanCount;
            public int Owner => 1;
            public SofOutcome TrySpend(ulong op, int cr, out int detail)
            {
                detail = 0;
                if (op == Overlord) return SofOutcome.None; // the reserved identity has no wallet: free, as the host seam does
                if (Wallet < cr) { detail = cr; return SofOutcome.LowCredit; }
                Wallet -= cr; Charged += cr;
                return SofOutcome.None;
            }
            public void Refund(ulong op, int cr) { if (op != Overlord) Wallet += cr; }
            public SofScene Scene(float x, float z) => SceneAt(x, z);
            public bool CyberNear(float x, float z) => false;
            public bool Roll(int chancePercent) => true;
            public void Reveal(float x, float z, float radius, float seconds) => Log.Add("reveal");
            public bool LaseBegin(int slot, uint key, float x, float z) { Log.Add("lase+"); return true; }
            public void LaseEnd(int slot, uint key) => Log.Add("lase-");
            public bool TargetAlive(TargetKind kind, AnchorSub sub, uint key) => true;
            public bool Sabotage(AnchorSub sub, uint key) { Log.Add("sabotage " + sub); return true; }
            public bool Tap(TargetKind kind, uint key, float seconds) => true;
            public bool BuildingAlive(uint key) => true;
            public bool PostCover(int slot, float x, float z) { Log.Add("cover"); return true; }
            public bool PostLase(int slot, float x, float z) { Log.Add("lasepost"); return true; }
            public void Pay(ulong pilot, int cr) { }
        }

        private sealed class World : ISofWatchWorld
        {
            public int HumanCount = 1;
            public bool Recon = true;
            public readonly Dictionary<uint, (float value, WatchKind kind)> Ground = new Dictionary<uint, (float, WatchKind)>();
            public int Humans => HumanCount;
            public bool AllowRecon => Recon;
            public bool TryGround(in SofTarget target, out float value, out WatchKind kind)
            {
                if (Ground.TryGetValue(target.Key, out var g)) { value = g.value; kind = g.kind; return true; }
                value = 0f; kind = WatchKind.Other; return false;
            }
            public bool CyberNear(float x, float z) => false;
        }

        private sealed class Rig
        {
            public readonly Ports Ports = new Ports();
            public readonly World World = new World();
            public readonly SofDesk Desk;
            public readonly SofWatchBrain Brain = new SofWatchBrain();
            public readonly WatchPacer Pacer = new WatchPacer();
            public readonly List<float> ActionTimes = new List<float>();
            public readonly List<SofWatchPlan> Acts = new List<SofWatchPlan>();
            public readonly List<SofEvent> Events = new List<SofEvent>();
            public bool Ai;

            public Rig(int camps = 1, float health = 1f)
            {
                var set = new SofCampSet(camps);
                for (int i = 0; i < camps; i++) { set.SetPosition(i, 0f, 0f); set.Set(i, health, health <= 0f, 0f); }
                Desk = new SofDesk(Ports, set);
                Desk.Happened += Events.Add;
            }

            public void Show(params SofSeed[] seeds) => Desk.Refresh(seeds);

            public void Tick(float seconds = 1f)
            {
                for (float t = 0; t < seconds; t += 1f)
                {
                    Ports.Clock += 1f;
                    Ports.HumanCount = World.HumanCount;
                    Desk.Tick();
                    SofWatchPlan p = Brain.Step(Desk, World, Pacer, Ai, Ports.Clock);
                    if (p.Action != SofWatchAction.None) { ActionTimes.Add(Ports.Clock); Acts.Add(p); }
                }
            }

            public int Count(SofEventKind kind)
            {
                int n = 0;
                foreach (SofEvent e in Events) if (e.Kind == kind) n++;
                return n;
            }

            public SofTeam Mine()
            {
                foreach (SofTeam t in Desk.Teams) if (t.Active && t.Raiser == Overlord) return t;
                return null;
            }
        }

        private static SofSeed Ground(uint key, float x, float z, float front = 4000f) => new SofSeed(TargetKind.Ground, AnchorSub.Uplink, key, x, z, front, true, false);
        private static SofSeed Anchor(AnchorSub sub, uint key, float x, float z, float front = 4000f) => new SofSeed(TargetKind.Anchor, sub, key, x, z, front, true, false);

        private static int IdOf(Rig rig, TargetKind kind, AnchorSub sub, uint key) => rig.Desk.Ids.Find(kind, sub, key);

        private static void CheckRaiseGates()
        {
            // No work: no team (a team with nothing to do would only sit there).
            var idle = new Rig();
            idle.World.HumanCount = 0; idle.Ai = true;
            idle.Tick(40f);
            Eq(idle.Acts.Count, 0, "no revealed target: no team is raised");
            Eq(idle.Brain.Last.Why, SofWatchWhy.NoWork, "and it says why");

            var rig = new Rig();
            rig.World.HumanCount = 0; rig.Ai = true; rig.World.Recon = false;
            rig.World.Ground[501] = (14f, WatchKind.AirDefence);
            rig.Show(Ground(501, 3000f, 0f));
            rig.Tick(12f);
            Eq(rig.Acts[0].Action, SofWatchAction.Raise, "a LIVE camp and a high-value contact: RAISE");
            Eq(rig.Acts[0].Code, WatchCode.SofRaise, "reason code");
            TestAssert.That(rig.Acts[0].Reason.StartsWith("RAISE TEAM", StringComparison.Ordinal), rig.Acts[0].Reason);
            Eq(rig.Ports.Charged, 0, "OVERLORD pays nothing");
            Eq(rig.Mine() != null, true, "its team stands");
            rig.Tick(120f);
            int teams = 0;
            foreach (SofTeam t in rig.Desk.Teams) if (t.Active) teams++;
            Eq(teams, 1, "never more than one team in the field");

            var damaged = new Rig(1, 0.4f);
            damaged.World.HumanCount = 0; damaged.Ai = true;
            damaged.World.Ground[501] = (14f, WatchKind.AirDefence);
            damaged.Show(Ground(501, 3000f, 0f));
            damaged.Tick(30f);
            Eq(damaged.Acts.Count, 0, "a DAMAGED camp raises nothing: OVERLORD raises only when the camp is LIVE");

            var down = new Rig(1, 0f);
            down.World.HumanCount = 0; down.Ai = true;
            down.World.Ground[501] = (14f, WatchKind.AirDefence);
            down.Show(Ground(501, 3000f, 0f));
            down.Tick(30f);
            Eq(down.Acts.Count, 0, "a DOWN camp raises nothing");
        }

        private static void CheckMissionChoice()
        {
            // LASE the best high-value contact near the front; the low-value one and the one far from the front are left alone.
            var rig = new Rig();
            rig.World.HumanCount = 0; rig.Ai = true; rig.World.Recon = false;
            rig.World.Ground[501] = (3f, WatchKind.Other); rig.World.Ground[502] = (16f, WatchKind.AirDefence); rig.World.Ground[503] = (20f, WatchKind.AirDefence);
            rig.Show(Ground(501, 2000f, 0f), Ground(502, 3000f, 500f), Ground(503, 4000f, 0f, front: 60000f));
            rig.Tick(200f);
            SofWatchPlan lase = rig.Acts.Find(p => p.Action == SofWatchAction.Mission);
            Eq(lase.Mission, MissionKind.Lase, "a high-value contact: LASE");
            Eq(lase.TargetId, IdOf(rig, TargetKind.Ground, AnchorSub.Uplink, 502), "the best one near the front (the 60 km one is out of the front's reach, the 3 k one is not worth it)");
            TestAssert.That(lase.Reason.Contains("LASE") && lase.Reason.Contains("KM FROM THE FRONT"), lase.Reason);

            // Low value only: RECON when allowed, nothing when not (an AI faction never recons).
            var recon = new Rig();
            recon.World.HumanCount = 1;
            recon.World.Ground[501] = (3f, WatchKind.Other);
            recon.Show(Ground(501, 2000f, 0f));
            recon.Tick(200f);
            SofWatchPlan scout = recon.Acts.Find(p => p.Action == SofWatchAction.Mission);
            Eq(scout.Mission, MissionKind.Recon, "a low-value contact near the front is a RECON");
            TestAssert.That(Math.Abs(SofRules.Distance(scout.X, scout.Z, 2000f, 0f) - 900f) < 1f, "from 900 m short of it, not on top of it (point " + scout.X + "," + scout.Z + ")");
            TestAssert.That(scout.Reason.StartsWith("RECON — A-1 FROM STAND-OFF", StringComparison.Ordinal), scout.Reason);
            var noRecon = new Rig();
            noRecon.World.HumanCount = 0; noRecon.Ai = true; noRecon.World.Recon = false;
            noRecon.World.Ground[501] = (3f, WatchKind.Other);
            noRecon.Show(Ground(501, 2000f, 0f));
            noRecon.Tick(200f);
            Eq(noRecon.Acts.Exists(p => p.Action == SofWatchAction.Mission), false, "AllowRecon false: no RECON, and no team is raised for nothing");

            // SABOTAGE only at odds of 55 % or better.
            var sab = new Rig();
            sab.World.HumanCount = 0; sab.Ai = true; sab.World.Recon = false;
            sab.Show(Anchor(AnchorSub.Uplink, 700, 3500f, 0f));
            sab.Tick(260f);
            SofWatchPlan s = sab.Acts.Find(p => p.Action == SofWatchAction.Mission);
            Eq(s.Mission, MissionKind.Sabotage, "an uplink at 70 % odds (nothing near): SABOTAGE");
            TestAssert.That(s.B >= 55, "the reason carries the odds (" + s.B + " %)");
            TestAssert.That(s.Reason.StartsWith("SABOTAGE UPLINK", StringComparison.Ordinal), s.Reason);

            var tough = new Rig();
            tough.World.HumanCount = 0; tough.Ai = true; tough.World.Recon = false;
            // Three revealed armoured units within 1 km of the anchor: 70 - 30 = 40 %.
            tough.World.Ground[601] = (9f, WatchKind.Armour); tough.World.Ground[602] = (9f, WatchKind.Armour); tough.World.Ground[603] = (9f, WatchKind.Armour);
            tough.Show(Anchor(AnchorSub.Uplink, 700, 3500f, 0f), Ground(601, 3800f, 0f), Ground(602, 3900f, 100f), Ground(603, 3600f, -200f));
            tough.Tick(260f);
            Eq(tough.Acts.Exists(p => p.Mission == MissionKind.Sabotage), false, "three enemy armoured units near the anchor: 40 % odds, no sabotage");

            // An EW truck is an EXPLOIT target (+25): it clears the bar where the same armour would stop an uplink.
            var ew = new Rig();
            ew.World.HumanCount = 0; ew.Ai = true; ew.World.Recon = false;
            ew.World.Ground[601] = (9f, WatchKind.Armour); ew.World.Ground[602] = (9f, WatchKind.Armour); ew.World.Ground[603] = (9f, WatchKind.Armour);
            ew.Show(Anchor(AnchorSub.EwTruck, 701, 3500f, 0f), Ground(601, 3800f, 0f), Ground(602, 3900f, 100f), Ground(603, 3600f, -200f));
            ew.Tick(260f);
            Eq(ew.Acts.Exists(p => p.Mission == MissionKind.Sabotage), true, "an EW truck at 40 + 25 = 65 %: sabotage");

            // A sabotage is not repeated within ten minutes.
            sab.Show(Anchor(AnchorSub.Uplink, 700, 3500f, 0f), Anchor(AnchorSub.Uplink, 701, 3600f, 0f));
            sab.Tick(400f);
            int sabotages = 0;
            foreach (SofWatchPlan p in sab.Acts) if (p.Mission == MissionKind.Sabotage) sabotages++;
            Eq(sabotages, 1, "one sabotage per ten minutes");

            // The enemy never sees what the faction has not revealed: an unsighted contact is not in the desk, so it is never a target.
            var fog = new Rig();
            fog.World.HumanCount = 0; fog.Ai = true;
            fog.World.Ground[501] = (30f, WatchKind.AirDefence);
            fog.Show(new SofSeed(TargetKind.Ground, AnchorSub.Uplink, 501, 3000f, 0f, 0f, false, false));
            fog.Tick(60f);
            Eq(fog.Acts.Count, 0, "an unsighted contact is never a target (the camp stays quiet)");
        }

        private static void CheckSteering()
        {
            // The team walks past two more enemies besides its target (the target's own unit adds nothing): exposure climbs 1 %/s (0.5 each) (three defenders on the mission, two on the way home: the model keeps both phases at 1 %/s). HOLD would only pin it where it stands, so OVERLORD withdraws once the exposure is high and still climbing.
            var rig = new Rig();
            rig.World.HumanCount = 1; rig.World.Recon = false;
            rig.World.Ground[501] = (18f, WatchKind.AirDefence);
            rig.Show(Ground(501, 3000f, 0f));
            rig.Ports.SceneAt = (x, z) => x > 1500f ? (rig.Mine() != null && rig.Mine().Mission == MissionKind.None ? new SofScene(0, 2, 2, 0, false) : new SofScene(0, 3, 3, 0, false)) : default;
            rig.Tick(400f);
            SofWatchPlan exfil = rig.Acts.Find(p => p.Code == WatchCode.SofExfil);
            Eq(exfil.Action, SofWatchAction.Order, "OVERLORD withdraws a team whose exposure is climbing");
            TestAssert.That(exfil.B >= 40 && exfil.Reason.StartsWith("EXFIL A-1", StringComparison.Ordinal), "from 40 % at the earliest and the reason says so: " + exfil.Reason);
            Eq(rig.Acts.Exists(p => p.Code == WatchCode.SofHold), false, "it never holds still inside a fight");
            Eq(rig.Count(SofEventKind.Pinned), 0, "and the team was not pinned");
            TestAssert.That(rig.Acts.TrueForAll(a => a.Reason.Length > 0), "every action carries a reason string");

            // A human working SOF suspends OVERLORD, but never its safety: a team in the field is still withdrawn by exposure.
            var sus = new Rig();
            sus.World.HumanCount = 1; sus.World.Recon = false;
            sus.World.Ground[501] = (18f, WatchKind.AirDefence);
            sus.Show(Ground(501, 3000f, 0f));
            sus.Ports.SceneAt = (x, z) => x > 1500f ? (sus.Mine() != null && sus.Mine().Mission == MissionKind.None ? new SofScene(0, 2, 2, 0, false) : new SofScene(0, 3, 3, 0, false)) : default;
            sus.Tick(160f);
            for (int i = 0; i < 40; i++) { sus.Brain.RecordHuman(sus.Ports.Clock); sus.Tick(10f); }
            Eq(sus.Acts.Exists(a => a.Code == WatchCode.SofExfil), true, "a suspended OVERLORD still withdraws a team whose exposure is climbing");
            Eq(sus.Acts.Exists(a => a.Code == WatchCode.SofPush && a.Reason.Length > 0 && sus.Acts.IndexOf(a) > sus.Acts.FindIndex(b => b.Code == WatchCode.SofExfil)), false, "and starts nothing else while suspended");

            // A hot team that is not climbing (the enemy left): HOLD lets the exposure fall twice as fast, then RESUME.
            var cool = new Rig();
            cool.World.HumanCount = 1; cool.World.Recon = false;
            cool.World.Ground[501] = (18f, WatchKind.AirDefence);
            cool.Show(Ground(501, 9000f, 0f));
            int phase = 0;
            cool.Ports.SceneAt = (x, z) => phase == 1 ? new SofScene(0, 1, 1, 0, false) : default;
            for (int i = 0; i < 400 && !cool.Acts.Exists(a => a.Code == WatchCode.SofPush) && !cool.Acts.Exists(a => a.Code == WatchCode.SofHold); i++) cool.Tick(1f);
            SofTeam mine = cool.Mine();
            TestAssert.That(mine != null && mine.State == TeamState.Moving, "the team is on its way");
            mine.Exposure = 88f; // enemies that were near have gone: the exposure is high but flat
            cool.Tick(40f);
            Eq(cool.Acts.Exists(a => a.Code == WatchCode.SofHold && a.B >= 50), true, "a flat 50 %+ exposure with nobody near: HOLD");
            cool.Tick(60f);
            Eq(cool.Acts.Exists(a => a.Code == WatchCode.SofResume), true, "and RESUME once it has fallen under 30 %");

            // A quiet route: PUSH while exposure is low and the way is long.
            var quiet = new Rig();
            quiet.World.HumanCount = 0; quiet.Ai = true; quiet.World.Recon = false;
            quiet.World.Ground[501] = (18f, WatchKind.AirDefence);
            quiet.Show(Ground(501, 6000f, 0f));
            quiet.Tick(300f);
            Eq(quiet.Acts.Exists(p => p.Code == WatchCode.SofPush), true, "a long quiet route is pushed");
            SofWatchPlan push = quiet.Acts.Find(p => p.Code == WatchCode.SofPush);
            TestAssert.That(push.B <= 25 && push.Reason.StartsWith("PUSH", StringComparison.Ordinal), push.Reason);
        }

        private static void CheckIdleReserveAndPacing()
        {
            var rig = new Rig();
            rig.World.HumanCount = 1;
            rig.World.Ground[501] = (18f, WatchKind.AirDefence);
            rig.Show(Ground(501, 3000f, 0f));
            rig.Brain.RecordHuman(rig.Ports.Clock);
            rig.Tick(55f);
            Eq(rig.Acts.Count, 0, "solo: nothing for 60 s after the human's SOF verb");
            rig.Tick(30f);
            TestAssert.That(rig.Acts.Count > 0, "then OVERLORD works again");

            var group = new Rig();
            group.World.HumanCount = 3;
            group.World.Ground[501] = (18f, WatchKind.AirDefence);
            group.Show(Ground(501, 3000f, 0f));
            group.Brain.RecordHuman(group.Ports.Clock);
            group.Tick(290f);
            Eq(group.Acts.Count, 0, "2 or more humans: out for 300 s");

            // One slot stays free for a human: a human's team fills the only slot of a solo faction (cap 2 minus the reserve).
            var reserve = new Rig();
            reserve.World.HumanCount = 1;
            reserve.World.Ground[501] = (18f, WatchKind.AirDefence);
            reserve.Show(Ground(501, 3000f, 0f));
            reserve.Desk.Raise(Human);
            reserve.Tick(40f);
            Eq(reserve.Acts.Exists(p => p.Action == SofWatchAction.Raise), false, "a human's team holds the one slot OVERLORD may use: it raises nothing");
            Eq(reserve.Brain.Last.Why, SofWatchWhy.NoSlot, "and says so");

            // Pacing: one action every 10 s for a faction with humans, 30 s for one without.
            var paced = new Rig();
            paced.World.HumanCount = 1;
            paced.World.Ground[501] = (18f, WatchKind.AirDefence); paced.World.Ground[502] = (16f, WatchKind.AirDefence);
            paced.Show(Ground(501, 3000f, 0f), Ground(502, 3500f, 0f), Anchor(AnchorSub.EwTruck, 700, 3200f, 0f));
            paced.Tick(1500f);
            TestAssert.That(paced.ActionTimes.Count >= 3, "OVERLORD kept working (" + paced.ActionTimes.Count + " actions)");
            for (int i = 1; i < paced.ActionTimes.Count; i++) TestAssert.That(paced.ActionTimes[i] - paced.ActionTimes[i - 1] >= 10f, "at most one action per 10 s: gap " + (paced.ActionTimes[i] - paced.ActionTimes[i - 1]));

            var ai = new Rig();
            ai.World.HumanCount = 0; ai.Ai = true; ai.World.Recon = false;
            ai.World.Ground[501] = (18f, WatchKind.AirDefence); ai.World.Ground[502] = (16f, WatchKind.AirDefence);
            ai.Show(Ground(501, 3000f, 0f), Ground(502, 3500f, 0f), Anchor(AnchorSub.EwTruck, 700, 3200f, 0f));
            ai.Tick(1500f);
            for (int i = 1; i < ai.ActionTimes.Count; i++) TestAssert.That(ai.ActionTimes[i] - ai.ActionTimes[i - 1] >= 30f, "an AI faction: one action per 30 s, gap " + (ai.ActionTimes[i] - ai.ActionTimes[i - 1]));
        }

        private static void CheckNeverLifts()
        {
            var rig = new Rig();
            rig.World.HumanCount = 0; rig.Ai = true;
            rig.World.Ground[501] = (18f, WatchKind.AirDefence); rig.World.Ground[502] = (16f, WatchKind.AirDefence);
            rig.Show(Ground(501, 9000f, 0f), Ground(502, 3500f, 0f), Anchor(AnchorSub.EwTruck, 700, 3200f, 0f));
            rig.Ports.SceneAt = (x, z) => x > 2500f ? new SofScene(0, 1, 5, 0, false) : default;
            rig.Tick(1800f);
            Eq(rig.Acts.Exists(p => p.Action == SofWatchAction.Order && p.Verb == TeamVerb.Lift), false, "OVERLORD never orders the helicopter lift");
            bool waiting = false;
            foreach (SofTeam t in rig.Desk.Teams) waiting |= t.LiftWaiting || t.Carried;
            Eq(waiting, false, "no team of OVERLORD ever waits for a lift or rides one");
            Eq(rig.Ports.Charged, 0, "and it never took a CR");
            Eq(rig.Brain.Failures, 0, "every action it took was legal for the desk");
        }
    }
}
