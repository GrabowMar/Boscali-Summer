using System.Collections.Generic;
using BoscaliSummer.Modules.Support.Domain.Fronts;
using BoscaliSummer.Modules.Support.Domain.Space;

namespace BoscaliSummer.Tests.Features.Support
{
    /// <summary>Slice S1a: the pure front rules (funding, queue, lock, superiority) and their wire.</summary>
    internal static class FrontsTests
    {
        private const byte P = 40;

        private sealed class BufW : ISpaceWriter
        {
            public readonly List<byte> Bytes = new List<byte>();
            public void WriteByte(byte value) => Bytes.Add(value);
        }

        private sealed class BufR : ISpaceReader
        {
            private readonly byte[] data;
            private int at;
            public BufR(byte[] bytes, int length = -1) { data = bytes; Length = length < 0 ? bytes.Length : length; }
            public int Length;
            public int Remaining => Length - at;
            public bool TryReadByte(out byte value)
            {
                if (at >= Length) { value = 0; return false; }
                value = data[at++]; return true;
            }
        }

        private static FrontWorld World() => new FrontWorld { UplinkMax = 3, Uplinks = 3, DataCenterMax = 2, DataCenters = 2, TruckMax = 2, Trucks = 2, CampMax = 2, Camps = 2 };

        public static void Run()
        {
            Funding();
            Queueing();
            Building();
            Locks();
            AutoQueue();
            Superiority();
            Wire();
            MarkSnap();
        }

        private static void Funding()
        {
            var b = new FrontBook();
            float taken = b.ShareTick(1000f, FrontRules.DefaultShare, 0f);
            TestAssert.Near(taken, 30f, "3 % of 1000");
            for (int i = 0; i < 3; i++) TestAssert.Near(b.Side((Front)i).Budget, 10f, "equal split by default");

            TestAssert.Near(b.ShareTick(1000f, 0.5f, 0f), 100f, "share clamps at 10 %");
            TestAssert.Near(b.ShareTick(-5f, 0.03f, 0f), 0f, "negative funds take nothing");
            TestAssert.Near(b.ShareTick(1000f, -1f, 0f), 0f, "negative share takes nothing");
            TestAssert.Near(b.ShareTick(0.5f, 1f, 0f), 0.05f, "never more than the clamped share of funds");

            TestAssert.Near(new FrontBook().ShareTick(600000f, FrontRules.DefaultShare, 0f, FrontRules.DefaultShareCap), FrontRules.DefaultShareCap, "a rich treasury is capped to a trickle");
            TestAssert.Near(new FrontBook().ShareTick(2000f, FrontRules.DefaultShare, 0f, FrontRules.DefaultShareCap), 60f, "a poor treasury stays under the cap");

            var w = new FrontBook();
            TestAssert.That(w.SetPriority(new[] { 2f, 1f, 1f }, 1UL, "A", 0f, out _), "priority set");
            w.ShareTick(1000f, 0.04f, 0f);
            TestAssert.Near(w.Side(Front.Space).Budget, 20f, "SPACE gets half");
            TestAssert.Near(w.Side(Front.Cyber).Budget, 10f, "CYBER a quarter");
            TestAssert.Near(w.Side(Front.Sof).Budget, 10f, "SOF a quarter");
            TestAssert.That(!w.SetPriority(new[] { 0f, 0f, 0f }, 1UL, "A", 100f, out string r0) && r0.Length > 0, "all-zero weights refused");
            TestAssert.That(!w.SetPriority(new[] { 1f, 1f }, 1UL, "A", 100f, out _), "wrong weight count refused");
            TestAssert.That(!w.SetPriority(new[] { 1f, float.NaN, 1f }, 1UL, "A", 100f, out _), "NaN weight refused");

            // overflow: 600 -> 200 per front; the 150 head is paid, 50 stays, build starts
            var o = new FrontBook();
            TestAssert.That(o.Queue(Front.Cyber, ProgrammeId.Readiness, World(), 0f, out _), "queue readiness");
            o.ShareTick(20000f, 0.03f, 5f);
            FrontProgrammeEntry head = o.Side(Front.Cyber).Queue[0];
            TestAssert.Near(head.Funded, 150f, "head fully paid");
            TestAssert.Near(o.Side(Front.Cyber).Budget, 50f, "overflow stays in budget");
            TestAssert.Near(head.BuildEnd, 125f, "build timer started at 5 + 120");
            TestAssert.Near(o.Side(Front.Space).Budget, 200f, "no queue, budget keeps it");

            // partially paid head
            var p = new FrontBook();
            p.Queue(Front.Sof, ProgrammeId.Readiness, World(), 0f, out _);
            p.ShareTick(10000f, 0.03f, 0f);
            TestAssert.Near(p.Side(Front.Sof).Queue[0].Funded, 100f, "paid what the budget had");
            TestAssert.That(!p.Side(Front.Sof).Queue[0].Started, "not started while underfunded");
            TestAssert.Near(p.Side(Front.Sof).Budget, 0f, "budget spent");

            // donations
            var d = new FrontBook();
            d.Queue(Front.Space, ProgrammeId.Readiness, World(), 0f, out _);
            TestAssert.Eq(d.Donate(Front.Space, ProgrammeId.Asat, 10f).Status, DonateStatus.NotQueued, "donation to a programme that is not queued");
            TestAssert.Eq(d.Donate(Front.Space, ProgrammeId.Readiness, 0f).Status, DonateStatus.InvalidAmount, "zero amount");
            TestAssert.Eq(d.Donate(Front.Space, ProgrammeId.Readiness, -3f).Status, DonateStatus.InvalidAmount, "negative amount");
            TestAssert.Eq(d.Donate(Front.Space, ProgrammeId.Readiness, float.NaN).Status, DonateStatus.InvalidAmount, "NaN amount");
            DonateResult ok = d.Donate(Front.Space, ProgrammeId.Readiness, 100f);
            TestAssert.That(ok.Status == DonateStatus.Ok && ok.Accepted == 100f, "donation accepted whole");
            DonateResult capped = d.Donate(Front.Space, ProgrammeId.Readiness, 100f);
            TestAssert.Near(capped.Accepted, 50f, "donation capped at the remaining cost");
            TestAssert.Near(d.Donate(Front.Space, ProgrammeId.Readiness, 100f).Accepted, 0f, "a full bar takes nothing");
        }

        private static void Queueing()
        {
            var b = new FrontBook();
            FrontWorld w = World();
            string why;
            TestAssert.That(!b.Queue(Front.Space, ProgrammeId.Asat, w, 0f, out why) && why.Contains("CYBER READINESS 3"), "ASAT needs CYBER readiness 3: " + why);
            b.AddReadiness(Front.Cyber, 1);
            TestAssert.That(!b.Queue(Front.Space, ProgrammeId.Asat, w, 0f, out _), "readiness 2 still refused");
            b.AddReadiness(Front.Cyber, 1);
            TestAssert.That(b.Queue(Front.Space, ProgrammeId.Asat, w, 0f, out _), "readiness 3 allows ASAT");
            TestAssert.Near(b.Side(Front.Space).Queue[0].Cost, 900f, "ASAT cost");

            TestAssert.That(!b.Queue(Front.Space, ProgrammeId.Asat, w, 0f, out why) && why == "ALREADY QUEUED", "duplicates refused");
            TestAssert.That(!b.Queue(Front.Cyber, ProgrammeId.Asat, w, 0f, out why) && why.Contains("NOT A CYBER"), "wrong front refused");

            TestAssert.That(!b.Queue(Front.Sof, ProgrammeId.Fob, w, 0f, out why) && why.Contains("BUILDING"), "FOB needs a held building");
            w.HasHeldBuilding = true;
            TestAssert.That(b.Queue(Front.Sof, ProgrammeId.Fob, w, 0f, out _), "FOB with a held building");

            w.Teams = 4;
            TestAssert.That(!b.Queue(Front.Sof, ProgrammeId.TrainTeam, w, 0f, out why) && why.Contains("CAP"), "team cap");
            w.Teams = 3;
            TestAssert.That(b.Queue(Front.Sof, ProgrammeId.TrainTeam, w, 0f, out _), "fourth team allowed");

            TestAssert.That(!b.Queue(Front.Space, ProgrammeId.LaunchSatellite, w, 0f, out why) && why.Contains("UP"), "launch only if a bird is down");
            w.BirdsDown = 1;
            TestAssert.That(b.Queue(Front.Space, ProgrammeId.LaunchSatellite, w, 0f, out _), "launch with a bird down");
            TestAssert.That(!b.Queue(Front.Space, ProgrammeId.UplinkSite, w, 0f, out _), "uplinks at max refused");
            w.Uplinks = 2;
            TestAssert.That(b.Queue(Front.Space, ProgrammeId.UplinkSite, w, 0f, out _), "uplink below max");

            // max 4: SPACE now holds ASAT, LAUNCH, UPLINK; READINESS is the fourth, then the list is full
            TestAssert.That(b.Queue(Front.Space, ProgrammeId.Readiness, w, 0f, out _), "fourth entry");
            TestAssert.Eq(b.Side(Front.Space).Queue.Count, 4, "four queued");
            var full = new FrontBook();
            for (int i = 0; i < 4; i++) full.Side(Front.Cyber).Queue.Add(new FrontProgrammeEntry { Id = ProgrammeId.Readiness, Cost = 1f });
            TestAssert.That(!full.Queue(Front.Cyber, ProgrammeId.ZeroDay, w, 0f, out why) && why.Contains("FULL"), "a fifth entry is refused");

            // rung order: the only READINESS programme is the next rung, priced by the current level
            var r = new FrontBook();
            r.Queue(Front.Cyber, ProgrammeId.Readiness, World(), 0f, out _);
            TestAssert.Near(r.Side(Front.Cyber).Queue[0].Cost, 150f, "rung 1 to 2 costs 150");
            TestAssert.Near(r.Side(Front.Cyber).Queue[0].Build, 120f, "rung 1 to 2 builds 120 s");
            r.AddReadiness(Front.Space, 2);
            r.Queue(Front.Space, ProgrammeId.Readiness, World(), 0f, out _);
            TestAssert.Near(r.Side(Front.Space).Queue[0].Cost, 450f, "rung 3 to 4 costs 450");
            TestAssert.Near(r.Side(Front.Space).Queue[0].Build, 360f, "rung 3 to 4 builds 360 s");
            r.AddReadiness(Front.Sof, 4);
            TestAssert.Eq(r.Readiness(Front.Sof), 5, "readiness caps at 5");
            TestAssert.That(!r.Queue(Front.Sof, ProgrammeId.Readiness, World(), 0f, out why) && why.Contains("MAX"), "no rung above 5");

            TestAssert.Near(FrontRules.Cost(ProgrammeId.Fob, 2f), 1400f, "cost scale applies");
            TestAssert.Near(FrontRules.Cost(ProgrammeId.Readiness, 4, 1f), 600f, "readiness rung cost");
            var sc = new FrontBook { CostScale = 0.5f };
            sc.Queue(Front.Cyber, ProgrammeId.ZeroDay, World(), 0f, out _);
            TestAssert.Near(sc.Side(Front.Cyber).Queue[0].Cost, 300f, "book applies the scale at queue time");
            TestAssert.Eq(FrontRules.Programmes.Length, 10, "ten programmes in the table");
        }

        private static void Building()
        {
            var b = new FrontBook();
            b.Queue(Front.Sof, ProgrammeId.Readiness, World(), 0f, out _);
            b.Donate(Front.Sof, ProgrammeId.Readiness, 150f);
            TestAssert.Eq(b.Tick(10f, World()).Count, 0, "funding starts the timer, nothing completes");
            TestAssert.Near(b.Side(Front.Sof).Queue[0].BuildEnd, 130f, "timer 10 + 120");
            TestAssert.Eq(b.Tick(129.9f, World()).Count, 0, "not done early");
            List<FrontEvent> ev = b.Tick(130f, World());
            TestAssert.Eq(ev.Count, 1, "one completion");
            TestAssert.That(ev[0].Front == Front.Sof && ev[0].Id == ProgrammeId.Readiness && ev[0].Rung == 2, "readiness event names the new level");
            TestAssert.Eq(b.Readiness(Front.Sof), 2, "readiness +1");
            TestAssert.Eq(b.Side(Front.Sof).Queue.Count, 0, "queue popped");

            // an effect programme emits its event and leaves readiness alone; the next head starts from budget in the same tick
            var c = new FrontBook();
            c.Queue(Front.Cyber, ProgrammeId.EwTruck, new FrontWorld { Trucks = 1, TruckMax = 2 }, 0f, out _);
            c.Queue(Front.Cyber, ProgrammeId.ZeroDay, World(), 0f, out _);
            c.Donate(Front.Cyber, ProgrammeId.EwTruck, 120f);
            c.Side(Front.Cyber).Budget = 1000f;
            c.Tick(0f, World());
            ev = c.Tick(120f, World());
            TestAssert.That(ev.Count == 1 && ev[0].Id == ProgrammeId.EwTruck, "truck event");
            TestAssert.Eq(c.Readiness(Front.Cyber), 1, "effects do not raise readiness");
            TestAssert.That(c.Side(Front.Cyber).Queue[0].Started, "next head started from the budget");
            TestAssert.Near(c.Side(Front.Cyber).Budget, 400f, "budget paid 600");

            c.AddReadiness(Front.Cyber, -9);
            TestAssert.Eq(c.Readiness(Front.Cyber), 1, "readiness never below 1");
            TestAssert.That(c.Side(Front.Cyber).Log.Count > 0 && c.Side(Front.Cyber).Log.Count <= FrontRules.LogCapacity, "log written");
            for (int i = 0; i < 20; i++) c.SetFocus(Front.Cyber, i, i, i);
            TestAssert.Eq(c.Side(Front.Cyber).Log.Count, FrontRules.LogCapacity, "log ring holds 8");
        }

        private static void Locks()
        {
            var b = new FrontBook();
            TestAssert.Eq(b.Side(Front.Space).Directive, FrontDirective.Recon, "SPACE default");
            TestAssert.Eq(b.Side(Front.Cyber).Directive, FrontDirective.Balanced, "CYBER default");
            TestAssert.Eq(b.Side(Front.Sof).Directive, FrontDirective.Recon, "SOF default");
            TestAssert.That(FrontRules.IsValid(Front.Space, FrontDirective.Strike) && !FrontRules.IsValid(Front.Space, FrontDirective.Attack), "SPACE set");
            TestAssert.That(FrontRules.IsValid(Front.Cyber, FrontDirective.Attack) && !FrontRules.IsValid(Front.Cyber, FrontDirective.Recon), "CYBER set");
            TestAssert.That(FrontRules.IsValid(Front.Sof, FrontDirective.Hold) && !FrontRules.IsValid(Front.Sof, FrontDirective.Strike), "SOF set");

            TestAssert.That(b.SetDirective(Front.Cyber, FrontDirective.Attack, 1UL, "DARKSTAR", 100f, out _), "first set");
            TestAssert.That(b.SetDirective(Front.Cyber, FrontDirective.Defend, 1UL, "DARKSTAR", 110f, out _), "same player inside the lock");
            TestAssert.That(!b.SetDirective(Front.Cyber, FrontDirective.Balanced, 2UL, "VIPER", 128f, out string why), "other player refused");
            TestAssert.Eq(why, "LOCKED 0:42 BY DARKSTAR", "lock reason");
            TestAssert.Eq(b.Side(Front.Cyber).Directive, FrontDirective.Defend, "refusal leaves the directive");
            TestAssert.That(b.SetDirective(Front.Space, FrontDirective.Strike, 2UL, "VIPER", 128f, out _), "locks are per front");
            TestAssert.That(!b.SetDirective(Front.Space, FrontDirective.Hold, 2UL, "VIPER", 129f, out why) && why.Contains("POSTURE"), "invalid posture for the front");
            TestAssert.That(b.SetDirective(Front.Cyber, FrontDirective.Balanced, 2UL, "VIPER", 170f, out _), "after expiry (110 + 60)");

            TestAssert.That(b.SetPriority(new[] { 1f, 1f, 2f }, 1UL, "DARKSTAR", 200f, out _), "priority");
            TestAssert.That(b.SetPriority(new[] { 1f, 2f, 1f }, 1UL, "DARKSTAR", 210f, out _), "same player again");
            TestAssert.That(!b.SetPriority(new[] { 1f, 1f, 1f }, 2UL, "VIPER", 220f, out why) && why == "LOCKED 0:50 BY DARKSTAR", "priority locked: " + why);
            TestAssert.That(b.SetPriority(new[] { 1f, 1f, 1f }, 2UL, "VIPER", 270f, out _), "priority after expiry");
            TestAssert.Near(b.Side(Front.Sof).PriorityWeight, 1f / 3f, "normalised");
        }

        private static void AutoQueue()
        {
            var b = new FrontBook();
            FrontWorld w = World();
            w.BirdsDown = 1; w.Uplinks = 1; w.Camps = 0; w.DataCenters = 1; w.Trucks = 0;
            b.Queue(Front.Space, ProgrammeId.Readiness, w, 0f, out _);
            TestAssert.Eq(b.AutoQueueRebuilds(w, 0f), 5, "five anchors down, five rebuilds");
            var space = b.Side(Front.Space).Queue;
            TestAssert.That(space.Count == 3 && space[0].Id == ProgrammeId.LaunchSatellite && space[1].Id == ProgrammeId.UplinkSite && space[2].Id == ProgrammeId.Readiness,
                "rebuilds go to the front in order, ahead of the waiting readiness");
            TestAssert.Eq(b.AutoQueueRebuilds(w, 0f), 0, "nothing queued twice");

            var started = new FrontBook();
            started.Queue(Front.Space, ProgrammeId.Readiness, w, 0f, out _);
            started.Donate(Front.Space, ProgrammeId.Readiness, 150f);
            started.Tick(0f, w);
            started.AutoQueueRebuilds(w, 1f);
            TestAssert.That(started.Side(Front.Space).Queue[0].Id == ProgrammeId.Readiness && started.Side(Front.Space).Queue[1].Id == ProgrammeId.LaunchSatellite,
                "a build already running keeps its place");

            var cap = new FrontBook();
            for (int i = 0; i < 3; i++) cap.Side(Front.Space).Queue.Add(new FrontProgrammeEntry { Id = ProgrammeId.Readiness, Cost = 1f });
            cap.AutoQueueRebuilds(w, 0f);
            TestAssert.Eq(cap.Side(Front.Space).Queue.Count, 4, "never exceeds four");
        }

        private static FrontSides S(int birds = 0, int up = 0, int total = 0, bool jam = false, int nodes = 0, int anchors = 0, float trace = 0f, int teams = 0, int bld = 0, int camps = 0) =>
            new FrontSides { BirdsUp = birds, UplinksLive = up, UplinksTotal = total, JamsOpponent = jam, HeldEnemyNodes = nodes, AnchorsLive = anchors, Trace = trace, TeamsAfield = teams, HeldBuildings = bld, CampsLive = camps };

        private static void Superiority()
        {
            // SPACE: 40*2/3 + 30*(1 - 0.5) + 30 = 26.67 + 15 + 30 = 71.67
            TestAssert.Eq(FrontSuperiority.Compute(Front.Space, S(3, 3, 3, true), S(1, 1, 2)), 72, "SPACE hand value");
            TestAssert.Eq(FrontSuperiority.Compute(Front.Space, S(0, 0, 3), S(3, 3, 3, true)), -100, "SPACE floor");
            TestAssert.Eq(FrontSuperiority.Compute(Front.Space, S(3, 3, 3, true), S(0, 0, 3)), 100, "SPACE ceiling");
            TestAssert.Eq(FrontSuperiority.Compute(Front.Space, S(2), S(2)), 0, "SPACE even");
            TestAssert.Eq(FrontSuperiority.Compute(Front.Space, S(9), S(0)), 40, "SPACE bird term clamps at 40");
            // CYBER: 50*2/4 + 30*2/4 + 20*(0.5-0.1) = 25 + 15 + 8
            TestAssert.Eq(FrontSuperiority.Compute(Front.Cyber, S(nodes: 2, anchors: 3, trace: 0.1f), S(anchors: 1, trace: 0.5f)), 48, "CYBER hand value");
            TestAssert.Eq(FrontSuperiority.Compute(Front.Cyber, S(nodes: 8, anchors: 8, trace: 1f), S()), 50 + 30 - 20, "CYBER terms clamp, trace against us subtracts");
            TestAssert.Eq(FrontSuperiority.Compute(Front.Cyber, S(trace: 1f), S(nodes: 9, anchors: 9)), -100, "CYBER floor");
            // SOF: 40*2/4 + 40*2/4 + 20*1/2 = 20 + 20 + 10
            TestAssert.Eq(FrontSuperiority.Compute(Front.Sof, S(teams: 3, bld: 2, camps: 1), S(teams: 1)), 50, "SOF hand value");
            TestAssert.Eq(FrontSuperiority.Compute(Front.Sof, S(teams: 9, bld: 9, camps: 9), S()), 100, "SOF ceiling");
            TestAssert.Eq(FrontSuperiority.Compute(Front.Sof, S(), S(teams: 2, bld: 1, camps: 2)), -20 - 10 - 20, "SOF negative");

            TestAssert.Near(FrontSuperiority.Quality(100), 1.25f, "quality +25 %");
            TestAssert.Near(FrontSuperiority.Quality(0), 1f, "quality neutral");
            TestAssert.Near(FrontSuperiority.Quality(-100), 0.75f, "quality -25 %");
            TestAssert.Near(FrontSuperiority.Quality(40), 1.1f, "quality at 40");
            TestAssert.Eq(FrontSuperiority.Word(100), "DOMINANT", "word 100");
            TestAssert.Eq(FrontSuperiority.Word(60), "DOMINANT", "word 60");
            TestAssert.Eq(FrontSuperiority.Word(59), "AHEAD", "word 59");
            TestAssert.Eq(FrontSuperiority.Word(20), "AHEAD", "word 20");
            TestAssert.Eq(FrontSuperiority.Word(19), "CONTESTED", "word 19");
            TestAssert.Eq(FrontSuperiority.Word(-19), "CONTESTED", "word -19");
            TestAssert.Eq(FrontSuperiority.Word(-20), "BEHIND", "word -20");
            TestAssert.Eq(FrontSuperiority.Word(-59), "BEHIND", "word -59");
            TestAssert.Eq(FrontSuperiority.Word(-60), "LOST", "word -60");
            TestAssert.That(FrontSuperiority.CounterActive(40) && !FrontSuperiority.CounterActive(39), "counter at +40");
        }

        private static byte[] Bytes(FrontStateData d) { var w = new BufW(); FrontWire.WriteState(w, d); return w.Bytes.ToArray(); }

        private static void Wire()
        {
            var b = new FrontBook();
            FrontWorld w = World();
            w.BirdsDown = 1;
            b.Queue(Front.Space, ProgrammeId.LaunchSatellite, w, 0f, out _);
            b.Queue(Front.Space, ProgrammeId.Readiness, w, 0f, out _);
            b.Queue(Front.Sof, ProgrammeId.Readiness, w, 0f, out _);
            b.SetPriority(new[] { 2f, 1f, 1f }, 7UL, "DARKSTAR", 100f, out _);
            b.SetDirective(Front.Cyber, FrontDirective.Attack, 7UL, "DARKSTAR", 100f, out _);
            b.SetFocus(Front.Sof, -1234.5f, 4321f, 101f);
            b.SetSuperiority(Front.Cyber, -37);
            b.ShareTick(10000f, 0.03f, 105f);

            FrontStateData s = b.Snapshot(P, 5, 110f);
            byte[] bytes = Bytes(s);
            FrontStateData back = FrontWire.ReadState(new BufR(bytes), P);
            TestAssert.Eq(back.Protocol, P, "protocol survives");
            TestAssert.Eq(back.Seq, 5, "seq");
            TestAssert.That(back.SameAs(s), "round trip is identical");
            TestAssert.Eq(back.Fronts[(int)Front.Cyber].Superiority, (sbyte)-37, "negative superiority");
            TestAssert.Eq(back.Fronts[(int)Front.Cyber].Directive, FrontDirective.Attack, "directive");
            TestAssert.Eq(back.Fronts[(int)Front.Cyber].DirectiveBy, "DARKSTAR", "setter name");
            TestAssert.Near(back.Fronts[(int)Front.Cyber].DirectiveLockUntil, 160f, "lock end moved by the expiry");
            TestAssert.Near(back.PriorityLockUntil, 160f, "priority lock");
            TestAssert.Eq(back.PriorityBy, "DARKSTAR", "priority setter");
            TestAssert.Eq(back.Fronts[(int)Front.Space].PriorityPct, (byte)50, "priority %");
            TestAssert.That(back.Fronts[(int)Front.Sof].HasFocus && System.Math.Abs(back.Fronts[(int)Front.Sof].FocusX + 1234.5f) < 0.11f, "focus");
            TestAssert.Eq(back.Fronts[(int)Front.Space].Queue.Count, 2, "queue rows");
            TestAssert.Eq(back.Fronts[(int)Front.Space].Queue[0].Id, ProgrammeId.LaunchSatellite, "queue head id");
            TestAssert.That(back.Fronts[(int)Front.Space].Queue[0].Percent > 0, "queue percent");
            TestAssert.That(back.Fronts[(int)Front.Space].Log.Count > 0, "log rows");
            TestAssert.Near(back.Fronts[(int)Front.Space].Log[0].Time, 0f, "log age restored");

            FrontStateData clone = s.Clone();
            TestAssert.That(clone.SameAs(s), "clone is the same");
            clone.Fronts[0].Readiness = 4;
            TestAssert.That(!clone.SameAs(s) && s.Fronts[0].Readiness == 1, "clone is deep");

            // an expired lock is not sent
            FrontStateData late = b.Snapshot(P, 6, 500f);
            FrontStateData lateBack = FrontWire.ReadState(new BufR(Bytes(late)), P);
            TestAssert.That(lateBack.PriorityLockUntil == 0f && lateBack.Fronts[1].DirectiveBy == "", "expired locks drop off");

            // hostile or truncated input never throws and is never accepted
            for (int n = 0; n < bytes.Length; n++) TestAssert.Eq(FrontWire.ReadState(new BufR(bytes, n), P).Seq, 0, "truncated at " + n);
            TestAssert.Eq(FrontWire.ReadState(new BufR(bytes), (byte)(P + 1)).Protocol, P, "foreign protocol is reported alone");
            byte[] bad = (byte[])bytes.Clone();
            bad[6] = 0; // first front's readiness
            TestAssert.Eq(FrontWire.ReadState(new BufR(bad), P).Seq, 0, "readiness 0 refused");

            // worst case: everything full, names at 16 chars
            var f = new FrontStateData { Protocol = P, Seq = int.MaxValue, Now = 99999f, PriorityLockUntil = 100050f, PriorityBy = "ABCDEFGHIJKLMNOP" };
            for (int i = 0; i < 3; i++)
            {
                FrontRow r = f.Fronts[i];
                r.HasFocus = true; r.FocusX = 99999f; r.FocusZ = -99999f; r.Budget = ushort.MaxValue; r.DirectiveLockUntil = 100050f; r.DirectiveBy = "ABCDEFGHIJKLMNOP";
                r.Directive = FrontRules.DefaultDirective((Front)i);
                for (int q = 0; q < 4; q++) r.Queue.Add(new FrontQueueRow { Id = i == 0 ? ProgrammeId.Readiness : i == 1 ? ProgrammeId.ZeroDay : ProgrammeId.Fob, Percent = 100, BuildLeft = ushort.MaxValue });
                for (int l = 0; l < 8; l++) r.Log.Add(new FrontLogRow { Code = 3, A = short.MaxValue, B = short.MinValue, Time = 90000f });
            }
            int size = FrontWire.StateSize(f);
            TestAssert.That(size <= FrontWire.ByteBudget, "worst-case state " + size + " bytes within " + FrontWire.ByteBudget);
            TestAssert.That(FrontWire.StateSize(s) < size, "a typical state is smaller");
        }

        private static void MarkSnap()
        {
            var marks = new List<SpaceMark> { new SpaceMark(1, 1000f, 0f, false, 100f, BirdKind.Optical), new SpaceMark(2, 1200f, 0f, false, 100f, BirdKind.Radar) };
            TestAssert.That(SpaceFireControl.TryNearestMark(marks, 1150f, 0f, 10f, out SpaceMark m) && m.Id == 2, "nearest mark within 300 m wins");
            TestAssert.That(!SpaceFireControl.TryNearestMark(marks, 1600f, 0f, 10f, out _), "nothing past 300 m");
            TestAssert.That(!SpaceFireControl.TryNearestMark(marks, 1000f, 0f, 100f, out _), "an expired mark never snaps");
            TestAssert.That(!SpaceFireControl.TryNearestMark(null, 0f, 0f, 0f, out _), "no marks, no snap");
        }
    }
}
