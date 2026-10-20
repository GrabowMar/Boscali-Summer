using System;
using System.Collections.Generic;
using BoscaliSummer.Modules.Support.Domain.Cyber;
using BoscaliSummer.Modules.Support.Domain.Fronts;
using BoscaliSummer.Modules.Support.Domain.Ops;
using BoscaliSummer.Modules.Support.Domain.Sof;
using BoscaliSummer.Modules.Support.Domain.Space;

namespace BoscaliSummer.Tests.Features.Support
{
    /// <summary>
    /// Protocol 42, the rooms made two-sided: CYBER PURGE (an enemy hold on our network is cut, the holder's trace jumps), SOF SWEEP (a QRF hunts a hostile team near our sites until it
    /// pins, then lets a lone team recover), OVERLORD defending first, the wire carrying both sides, and an AI faction building ASAT.
    /// </summary>
    internal static class DefenceTests
    {
        private const int Us = 7, Them = 9;
        private const byte P = 42;
        private const ulong Human = 111UL, Overlord = SpaceContacts.WatchOfficerId;

        public static void Run()
        {
            PurgeVerb();
            PurgeEvicts();
            PurgeOverlord();
            SweepVerbAndHunt();
            SweepOverlord();
            WireBothSides();
            Commands();
            AiAsat();
        }

        // ---- CYBER ---------------------------------------------------------------------------------

        private sealed class CyberPorts : ICyberPorts
        {
            public float Clock; public int Owner { get; set; } = Us; public int Balance = 100, Charged;
            public float Now => Clock;
            public int Humans => 1;
            public CyberOutcome TrySpend(ulong op, int cr, out int detail)
            {
                detail = 0;
                if (op == Overlord) return CyberOutcome.None;
                if (Balance < cr) { detail = cr; return CyberOutcome.LowCredit; }
                Balance -= cr; Charged += cr;
                return CyberOutcome.None;
            }
            public void Refund(ulong op, int cr) { if (op != Overlord) Balance += cr; }
            public CyberOutcome PostPackage(ulong op, in CyberNode node, in PackageDef def, bool exploit, float effort) => CyberOutcome.None;
        }

        private sealed class CyberWorld : ICyberWatchWorld
        {
            public int HumanCount;
            public int Humans => HumanCount;
            public bool PackageRoom => false;
            public float NearestPilotMeters(float x, float z) => float.PositiveInfinity;
        }

        private static CyberDesk Desk(CyberPorts ports)
        {
            var anchors = new CyberAnchorSet(1, 0);
            anchors.SetPosition(AnchorKind.EwTruck, 0, 0f, 0f);
            return new CyberDesk(ports, anchors);
        }

        private static void PurgeVerb()
        {
            var ports = new CyberPorts { Clock = 100f };
            CyberDesk victim = Desk(ports);
            TestAssert.Eq(victim.Purge(Human).Outcome, CyberOutcome.NothingToPurge, "nothing to purge with no enemy hold");
            victim.SetBreaches(new List<CyberBreach> { new CyberBreach(Them, 3, NodeKind.Radar, 1000f, 0f, 90f) });
            CyberResult r = victim.Purge(Human);
            TestAssert.That(r.Ok && r.Outcome == CyberOutcome.PurgeStarted && r.Charged == CyberRules.PurgeCost && ports.Charged == CyberRules.PurgeCost, "PURGE starts and costs 4 CR");
            TestAssert.Eq(victim.Purge(Human).Outcome, CyberOutcome.PurgeCooling, "one PURGE at a time");
            ports.Clock += CyberRules.PurgeSeconds - 1f;
            TestAssert.That(!victim.TakePurgeDue(ports.Clock), "the PURGE lands after 20 s, not before");
            ports.Clock += 1f;
            TestAssert.That(victim.TakePurgeDue(ports.Clock) && !victim.TakePurgeDue(ports.Clock), "it lands exactly once");
            CyberResult cooling = victim.Purge(Human);
            TestAssert.That(cooling.Outcome == CyberOutcome.PurgeCooling && cooling.Detail > 0 && cooling.Words.Contains("READY IN"), "then it recharges: " + cooling.Words);
            ports.Clock += CyberRules.PurgeCooldownSeconds;
            TestAssert.Eq(victim.Purge(Human).Outcome, CyberOutcome.PurgeStarted, "and runs again once recharged");
            var broke = new CyberPorts { Clock = 1f, Balance = 1 };
            CyberDesk poor = Desk(broke);
            poor.SetBreaches(new List<CyberBreach> { new CyberBreach(Them, 3, NodeKind.Radar, 0f, 0f, 0f) });
            TestAssert.Eq(poor.Purge(Human).Outcome, CyberOutcome.LowCredit, "a PURGE is paid for");
        }

        private static void PurgeEvicts()
        {
            var ports = new CyberPorts { Owner = Them, Clock = 10f };
            CyberDesk holder = Desk(ports);
            var events = new List<CyberEvent>();
            holder.Happened += events.Add;
            holder.Refresh(new[]
            {
                new NodeObservation(new NodeSeed(NodeKind.Radar, 40, 3000f, 0f, 0f), Us, true, false),
                new NodeObservation(new NodeSeed(NodeKind.Relay, 41, 4000f, 0f, 0f), Us, true, false)
            });
            holder.Ids.TryGet(NodeKind.Radar, 40, out int radar);
            holder.Ids.TryGet(NodeKind.Relay, 41, out int relay);
            TestAssert.That(holder.Hop(Human, relay).Ok, "the enemy hops our relay");
            for (int i = 0; i < 25; i++) { ports.Clock += 1f; holder.Tick(); }
            TestAssert.That(holder.Hop(Human, radar).Ok, "and goes deeper to our radar");
            for (int i = 0; i < 25; i++) { ports.Clock += 1f; holder.Tick(); }
            CyberIntrusion x = holder.Network.Of(Human);
            TestAssert.That(x != null && x.HeldCount == 2, "two of our nodes are held");
            float before = x.Trace;
            TestAssert.Eq(holder.Evict(radar), 1, "our PURGE cuts the radar hold");
            TestAssert.That(x.HeldCount == 1 && !x.Ended && x.Trace >= Math.Min(before + CyberRules.PurgeTraceKick, CyberRules.TraceMax - 0.01f) - 0.01f && x.Trace < CyberRules.TraceMax, "the holder keeps the relay and its trace jumps 25 % (capped just short of traced): held " + x.HeldCount + ", ended " + x.Ended + ", trace " + before + " -> " + x.Trace);
            TestAssert.That(events.Exists(e => e.Kind == CyberEventKind.Released && e.NodeId == radar && e.Reason == IntrusionEnd.Purged), "the holder's console reads PURGED");
            TestAssert.Eq(CyberNetWords.ReasonWord(IntrusionEnd.Purged), "PURGED BY THE ENEMY", "and the words say who did it");
            TestAssert.Eq(holder.Evict(radar), 0, "a node no one holds any more cuts nothing");
            holder.Evict(relay);
            TestAssert.That(x.Ended && x.EndReason == IntrusionEnd.Purged, "cutting the last hold ends the intrusion PURGED");
        }

        private static void PurgeOverlord()
        {
            // DEFEND purges at once, even with humans online.
            var ports = new CyberPorts { Clock = 100f };
            CyberDesk desk = Desk(ports);
            desk.SetBreaches(new List<CyberBreach> { new CyberBreach(Them, 3, NodeKind.SamC2, 0f, 0f, 99f) });
            var brain = new CyberWatchBrain { Bias = new DirectorBias(FrontDirective.Defend, false, 0f, 0f) };
            var pacer = new WatchPacer();
            CyberWatchPlan p = brain.Step(desk, new CyberWorld { HumanCount = 2 }, pacer, false, ports.Clock);
            TestAssert.That(p.Action == CyberWatchAction.Purge && desk.PurgeEndsAt > ports.Clock && p.Reason.StartsWith("PURGE", StringComparison.Ordinal), "DEFEND purges at once: " + p.Reason);

            // Otherwise OVERLORD lets a hold age 30 s first.
            var aiPorts = new CyberPorts { Clock = 100f };
            CyberDesk ai = Desk(aiPorts);
            ai.SetBreaches(new List<CyberBreach> { new CyberBreach(Them, 3, NodeKind.Radar, 0f, 0f, 95f) });
            var aiBrain = new CyberWatchBrain();
            var aiPacer = new WatchPacer();
            var world = new CyberWorld { HumanCount = 0 };
            TestAssert.That(aiBrain.Step(ai, world, aiPacer, true, aiPorts.Clock).Action != CyberWatchAction.Purge, "a 5 s old hold is not purged yet");
            CyberWatchPlan later = aiBrain.Step(ai, world, aiPacer, true, aiPorts.Clock = 130f);
            TestAssert.Eq(later.Action, CyberWatchAction.Purge, "a 35 s old hold is purged by an AI faction");
        }

        // ---- SOF -----------------------------------------------------------------------------------

        private sealed class SofPorts : ISofPorts
        {
            public float Clock = 100f; public int Wallet = 100, Charged;
            public bool Fob { get; set; }
            public float Now => Clock;
            public int Humans => 1;
            public int Owner => Us;
            public SofOutcome TrySpend(ulong op, int cr, out int detail)
            {
                detail = 0;
                if (op == Overlord) return SofOutcome.None;
                if (Wallet < cr) { detail = cr; return SofOutcome.LowCredit; }
                Wallet -= cr; Charged += cr;
                return SofOutcome.None;
            }
            public void Refund(ulong op, int cr) { if (op != Overlord) Wallet += cr; }
            public SofScene Scene(float x, float z) => default;
            public bool CyberNear(float x, float z) => false;
            public bool Roll(int chancePercent) => true;
            public void Reveal(float x, float z, float radius, float seconds) { }
            public bool LaseBegin(int slot, uint key, float x, float z) => true;
            public void LaseEnd(int slot, uint key) { }
            public bool TargetAlive(TargetKind kind, AnchorSub sub, uint key) => true;
            public bool Sabotage(AnchorSub sub, uint key) => true;
            public bool Tap(TargetKind kind, uint key, float seconds) => true;
            public bool BuildingAlive(uint key) => true;
            public bool PostCover(int slot, float x, float z) => true;
            public bool PostLase(int slot, float x, float z) => true;
            public void Pay(ulong pilot, int cr) { }
        }

        private sealed class SofWorld : ISofWatchWorld
        {
            public int HumanCount;
            public int Humans => HumanCount;
            public bool AllowRecon => false;
            public bool TryGround(in SofTarget target, out float value, out WatchKind kind) { value = 0f; kind = WatchKind.Other; return false; }
            public bool CyberNear(float x, float z) => false;
        }

        private static SofDesk SofDeskWithCamp(SofPorts ports)
        {
            var camps = new SofCampSet(1);
            camps.SetPosition(0, 0f, 0f);
            camps.Set(0, 1f, false, 0f);
            return new SofDesk(ports, camps);
        }

        private static void SweepVerbAndHunt()
        {
            var ports = new SofPorts();
            SofDesk victim = SofDeskWithCamp(ports);
            TestAssert.Eq(victim.Sweep(Human).Outcome, SofOutcome.NothingToSweep, "nothing to sweep with no hostile near our sites");
            victim.SetHostiles(new List<SofHostile> { new SofHostile(Them, 1, 500f, 0f, false) });
            SofResult r = victim.Sweep(Human);
            TestAssert.That(r.Ok && r.Outcome == SofOutcome.Swept && ports.Charged == SofRules.SweepCost, "SWEEP rolls and costs 4 CR: " + r.Words);
            TestAssert.Eq(victim.Sweep(Human).Outcome, SofOutcome.SweepCooling, "one QRF at a time");
            ports.Clock += SofRules.SweepSeconds;
            TestAssert.That(victim.TakeSweepDue(ports.Clock) && !victim.TakeSweepDue(ports.Clock), "the QRF arrives once, 30 s later");

            // The hunted side: a lone team on the move is pinned by the hunt, then recovers once it ends.
            var theirPorts = new SofPorts();
            SofDesk foe = SofDeskWithCamp(theirPorts);
            SofResult raised = foe.Raise(Human, 4);
            TestAssert.That(raised.Ok, "a hostile team is raised");
            for (int i = 0; i < 120; i++) { theirPorts.Clock += 1f; foe.Tick(); }
            TestAssert.That(foe.Divert(Human, raised.Slot, 200000f, 0f).Ok, "and sent far away");
            theirPorts.Clock += 1f; foe.Tick();
            SofTeam t = foe.Teams[raised.Slot];
            TestAssert.That(t.State == TeamState.Moving && t.Exposure == 0f, "alone it is not exposed");
            TestAssert.That(foe.Hunt(raised.Slot, theirPorts.Clock + SofRules.HuntSeconds), "our QRF reaches it");
            for (int i = 0; i < 60; i++) { theirPorts.Clock += 1f; foe.Tick(); }
            TestAssert.Eq(t.State, TeamState.Pinned, "the hunt pins a lone team inside its minute");
            for (int i = 0; i < 115; i++) { theirPorts.Clock += 1f; foe.Tick(); }
            TestAssert.That(t.State != TeamState.Lost && t.State != TeamState.Pinned, "with the hunt over a lone team unpins before its 2 minute deadline: " + t.State);
            TestAssert.That(!foe.Hunt(SofRules.MaxTeams, 1e6f) && !foe.Hunt(raised.Slot == 0 ? 1 : 0, 1e6f), "an empty or unknown slot cannot be hunted");
        }

        private static void SweepOverlord()
        {
            var ports = new SofPorts();
            SofDesk desk = SofDeskWithCamp(ports);
            desk.SetHostiles(new List<SofHostile> { new SofHostile(Them, 0, 100f, 0f, false, ports.Clock - 5f), new SofHostile(Them, 2, 900f, 0f, false, ports.Clock) });
            var humans = new SofWatchBrain();
            TestAssert.That(humans.Step(desk, new SofWorld { HumanCount = 2 }, new WatchPacer(), false, ports.Clock + 1f).Action != SofWatchAction.Sweep, "on RECON a raider gets its first 30 s");
            var hold = new SofWatchBrain { Bias = new DirectorBias(FrontDirective.Hold, false, 0f, 0f) };
            SofWatchPlan p = hold.Step(desk, new SofWorld { HumanCount = 2 }, new WatchPacer(), false, ports.Clock + 1f);
            TestAssert.That(p.Action == SofWatchAction.Sweep && desk.SweepEndsAt > ports.Clock && p.Reason.Contains("2 HOSTILE TEAMS"), "HOLD rolls the QRF at once: " + p.Reason);
            var ai = new SofWatchBrain();
            var aiPorts = new SofPorts();
            SofDesk aiDesk = SofDeskWithCamp(aiPorts);
            aiDesk.SetHostiles(new List<SofHostile> { new SofHostile(Them, 0, 100f, 0f, false, aiPorts.Clock - 40f) });
            TestAssert.Eq(ai.Step(aiDesk, new SofWorld { HumanCount = 0 }, new WatchPacer(), true, aiPorts.Clock + 1f).Action, SofWatchAction.Sweep, "an AI faction sweeps on its own");
        }

        // ---- Wire ----------------------------------------------------------------------------------

        private sealed class BufW : ISpaceWriter
        {
            public readonly List<byte> Bytes = new List<byte>();
            public void WriteByte(byte value) => Bytes.Add(value);
        }

        private sealed class BufR : ISpaceReader
        {
            private readonly byte[] data;
            private int at;
            public BufR(byte[] bytes) { data = bytes; }
            public int Remaining => data.Length - at;
            public bool TryReadByte(out byte value)
            {
                if (at >= data.Length) { value = 0; return false; }
                value = data[at++]; return true;
            }
        }

        private static void WireBothSides()
        {
            var c = new CyberStateData { Protocol = P, Faction = 0x1234u, Epoch = 3, Active = true, Seq = 1, Now = 200f, IntrusionCap = 2, HeldTotal = 1, PurgeEndsAt = 215f, PurgeReadyAt = 305f };
            c.Nodes.Add(new CyberNodeRow { Id = 4, Kind = NodeKind.Radar, X = 100f, Z = 200f, Held = true, Purging = true });
            c.Breaches.Add(new CyberBreachRow { Kind = NodeKind.SamC2, X = -5000f, Z = 7000f });
            var w = new BufW(); CyberWire.WriteState(w, c);
            CyberStateData back = CyberWire.ReadState(new BufR(w.Bytes.ToArray()), P);
            TestAssert.That(back.Protocol == P && back.Nodes.Count == 1 && back.Nodes[0].Purging && back.Nodes[0].Held, "the holder learns its node is being purged");
            TestAssert.That(back.Breaches.Count == 1 && back.Breaches[0].Kind == NodeKind.SamC2 && Math.Abs(back.Breaches[0].X + 5000f) < 0.2f, "the victim learns what is held and where");
            TestAssert.That(Math.Abs(back.PurgeEndsAt - 215f) < 1f && Math.Abs(back.PurgeReadyAt - 305f) < 1f && back.SameAs(c), "the PURGE clocks survive the wire");
            byte[] cut = w.Bytes.GetRange(0, w.Bytes.Count - 3).ToArray();
            TestAssert.Eq(CyberWire.ReadState(new BufR(cut), P).Protocol, (byte)0, "a truncated breach row is refused");

            var s = new SofStateData { Protocol = P, Faction = 0x1234u, Epoch = 3, Active = true, Seq = 1, Now = 200f, TeamCap = 2, SweepEndsAt = 230f, SweepReadyAt = 350f };
            s.Teams.Add(new SofTeamRow { Slot = 1, State = TeamState.Moving, Hunted = true, Exposure = 40, Ammo = 100, Odds = 50, Controller = "" });
            s.Enemies.Add(new SofEnemyRow { X = 10f, Z = 20f, Near = true, Hunted = true });
            s.Enemies.Add(new SofEnemyRow { X = 30f, Z = 40f });
            var sw = new BufW(); SofWire.WriteState(sw, s);
            SofStateData sb = SofWire.ReadState(new BufR(sw.Bytes.ToArray()), P);
            TestAssert.That(sb.Protocol == P && sb.Teams.Count == 1 && sb.Teams[0].Hunted, "our team knows a QRF hunts it");
            TestAssert.That(sb.Enemies.Count == 2 && sb.Enemies[0].Near && sb.Enemies[0].Hunted && !sb.Enemies[1].Near && !sb.Enemies[1].Hunted, "near-site and bird-seen enemies stay apart");
            TestAssert.That(Math.Abs(sb.SweepEndsAt - 230f) < 1f && Math.Abs(sb.SweepReadyAt - 350f) < 1f && sb.SameAs(s), "the SWEEP clocks survive the wire");
        }

        private static void Commands()
        {
            foreach (SpaceCommandKind kind in new[] { SpaceCommandKind.CyberPurge, SpaceCommandKind.SofSweep })
            {
                var w = new BufW(); SpaceWire.WriteCommand(w, new SpaceCommand(P, kind, 12, 0));
                SpaceCommand back = SpaceWire.ReadCommand(new BufR(w.Bytes.ToArray()), P);
                TestAssert.That(back.Kind == kind && back.RequestId == 12 && back.Mutating && (kind == SpaceCommandKind.CyberPurge ? back.IsCyberVerb : back.IsSofVerb), kind + " is a mutating verb that rides the wire");
            }
            var unknown = new BufW(); SpaceWire.WriteCommand(unknown, new SpaceCommand(P, (SpaceCommandKind)29, 9, 0));
            TestAssert.Eq(SpaceWire.ReadCommand(new BufR(unknown.Bytes.ToArray()), P).Protocol, (byte)0, "the kind after SofSweep is unknown");
            var ok = new BufW(); SpaceWire.WriteReply(ok, new SpaceReply(P, SpaceCommandKind.CyberPurge, 12, (byte)CyberOutcome.PurgeCooling, 0, 0, 40));
            TestAssert.That(SpaceWire.ReadReply(new BufR(ok.Bytes.ToArray()), P).CyberVerdict == CyberOutcome.PurgeCooling, "a PURGE verdict rides a reply");
            var bad = new BufW(); SpaceWire.WriteReply(bad, new SpaceReply(P, SpaceCommandKind.SofSweep, 12, (byte)(SofOutcomeWords.MaxOutcome + 1)));
            TestAssert.Eq(SpaceWire.ReadReply(new BufR(bad.Bytes.ToArray()), P).Protocol, (byte)0, "an unknown SWEEP verdict is never guessed at");
            TestAssert.That(SofOutcomeWords.Of(SofOutcome.SweepCooling, 30).Contains("READY IN") && CyberWords.Of(CyberOutcome.NothingToPurge).StartsWith("NEGATIVE", StringComparison.Ordinal), "every new verdict has words");
        }

        private static void AiAsat()
        {
            var w = new FrontWorld();
            TestAssert.Eq(FrontAi.Next(Front.Space, FrontRules.MaxReadiness, w, false, true), (ProgrammeId?)ProgrammeId.Asat, "an AI faction ready for it builds an ASAT");
            TestAssert.That(FrontAi.Next(Front.Space, FrontRules.MaxReadiness, w, false) != ProgrammeId.Asat, "and never when the host says it is not ready");
        }
    }
}
