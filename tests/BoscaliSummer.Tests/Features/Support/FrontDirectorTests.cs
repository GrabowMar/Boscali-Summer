using System.Collections.Generic;
using BoscaliSummer.Modules.Support.Domain.Cyber;
using BoscaliSummer.Modules.Support.Domain.Fronts;
using BoscaliSummer.Modules.Support.Domain.Space;

namespace BoscaliSummer.Tests.Features.Support
{
    /// <summary>OPS FRONTS S1b + S2: the front commands on the wire, the radio words, the counter triangle, the AI doctrine, the mirror's clock and the directive-to-director mapping.</summary>
    internal static class FrontDirectorTests
    {
        private const byte P = 38;

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

        public static void Run()
        {
            Commands();
            Replies();
            Words();
            Codes();
            Counters();
            Ai();
            Mirror();
            Mapping();
            SpacePolicy();
        }

        private static SpaceCommand RoundTrip(in SpaceCommand c)
        {
            var w = new BufW();
            SpaceWire.WriteCommand(w, c);
            return SpaceWire.ReadCommand(new BufR(w.Bytes.ToArray()), P);
        }

        private static void Commands()
        {
            var directive = RoundTrip(new SpaceCommand(P, SpaceCommandKind.FrontDirective, 5, 1 | ((int)FrontDirective.Attack << 2)));
            TestAssert.That(directive.Kind == SpaceCommandKind.FrontDirective && directive.Target == (1 | (4 << 2)) && directive.RequestId == 5, "FrontDirective survives");
            var queue = RoundTrip(new SpaceCommand(P, SpaceCommandKind.FrontQueue, 6, 0 | ((int)ProgrammeId.LaunchSatellite << 2)));
            TestAssert.That(queue.Kind == SpaceCommandKind.FrontQueue && queue.Target == (1 << 2), "FrontQueue survives");
            var priority = RoundTrip(new SpaceCommand(P, SpaceCommandKind.FrontPriority, 7, 0, new[] { 50, 30, 20 }));
            TestAssert.That(priority.Kind == SpaceCommandKind.FrontPriority && priority.Ids.Length == 3 && priority.Ids[1] == 30, "FrontPriority carries three weights");
            var focus = RoundTrip(new SpaceCommand(P, SpaceCommandKind.FrontFocus, 8, 0, new[] { 2, 123456 }));
            TestAssert.That(focus.Kind == SpaceCommandKind.FrontFocus && focus.Ids.Length == 2 && focus.Ids[1] == 123456, "FrontFocus carries a front and a point");
            var donate = RoundTrip(new SpaceCommand(P, SpaceCommandKind.FrontDonate, 9, 0, new[] { 1 | (5 << 2), 25 }));
            TestAssert.That(donate.Kind == SpaceCommandKind.FrontDonate && donate.Ids[1] == 25, "FrontDonate carries a programme and an amount");
            var geo = RoundTrip(new SpaceCommand(P, SpaceCommandKind.RelocateBird, 10, 0, new[] { 1, GeoSpace.Pack(0.62f, 0.31f) }));
            TestAssert.That(geo.Kind == SpaceCommandKind.RelocateBird && geo.Mutating && geo.IsFrontVerb && geo.Ids.Length == 2 && geo.Ids[0] == 1 &&
                GeoSpace.TryUnpack(geo.Ids[1], out float gu, out float gv) && System.Math.Abs(gu - 0.62f) < 0.001f && System.Math.Abs(gv - 0.31f) < 0.001f, "RelocateBird carries a bird and a map point");
            TestAssert.Eq(RoundTrip(new SpaceCommand(P, SpaceCommandKind.RelocateBird, 11, 0, new[] { 1, 2, 3 })).Protocol, (byte)0, "a relocation of three ints is refused");
            var gw = new BufW(); SpaceWire.WriteReply(gw, new SpaceReply(P, SpaceCommandKind.RelocateBird, 10, (byte)FrontOutcome.Relocated, 0, 14, 77));
            SpaceReply geoReply = SpaceWire.ReadReply(new BufR(gw.Bytes.ToArray()), P);
            TestAssert.That(geoReply.Kind == SpaceCommandKind.RelocateBird && geoReply.Outcome == (byte)FrontOutcome.Relocated && geoReply.Detail == 77 && FrontWords.Good(FrontOutcome.Relocated), "the burn verdict rides a reply");
            var sync = RoundTrip(new SpaceCommand(P, SpaceCommandKind.FrontSync, 0));
            TestAssert.That(sync.Kind == SpaceCommandKind.FrontSync && !sync.Mutating, "FrontSync is a plain ask");
            TestAssert.That(directive.Mutating && priority.Mutating && donate.Mutating && directive.IsFrontVerb && !sync.IsFrontVerb, "the five front verbs mutate, the sync does not");

            // Hostile shapes read inert: a short or long weight list, a donation with three ints, an unknown kind.
            TestAssert.Eq(RoundTrip(new SpaceCommand(P, SpaceCommandKind.FrontPriority, 7, 0, new[] { 1, 2 })).Protocol, (byte)0, "two weights are refused");
            TestAssert.Eq(RoundTrip(new SpaceCommand(P, SpaceCommandKind.FrontPriority, 7, 0, new[] { 1, 2, 3, 4 })).Protocol, (byte)0, "four weights are refused");
            TestAssert.Eq(RoundTrip(new SpaceCommand(P, SpaceCommandKind.FrontDonate, 9, 0, new[] { 1, 2, 3 })).Protocol, (byte)0, "a donation of three ints is refused");
            TestAssert.Eq(RoundTrip(new SpaceCommand(P, (SpaceCommandKind)27, 9, 0)).Protocol, (byte)0, "the kind after RelocateBird is unknown");
            TestAssert.That(new SpaceCommand(P, SpaceCommandKind.FrontDirective, 1, 3).Fingerprint() != new SpaceCommand(P, SpaceCommandKind.FrontDirective, 1, 4).Fingerprint(), "a changed payload changes the fingerprint (replay refused)");
        }

        private static void Replies()
        {
            var w = new BufW();
            SpaceWire.WriteReply(w, new SpaceReply(P, SpaceCommandKind.FrontDirective, 11, (byte)FrontOutcome.Locked, 0, 0, 42, false, "VIPER-2"));
            SpaceReply back = SpaceWire.ReadReply(new BufR(w.Bytes.ToArray()), P);
            TestAssert.That(back.Kind == SpaceCommandKind.FrontDirective && back.Outcome == (byte)FrontOutcome.Locked && back.Detail == 42 && back.Claimant == "VIPER-2", "a lock names the holder and the seconds");
            TestAssert.Eq(FrontWords.Outcome((FrontOutcome)back.Outcome, Front.Cyber, back.Detail, back.Claimant), "LOCKED 0:42 BY VIPER-2", "and the client words it");

            var bad = new BufW();
            SpaceWire.WriteReply(bad, new SpaceReply(P, SpaceCommandKind.FrontQueue, 12, (byte)(FrontWords.MaxOutcome + 1)));
            TestAssert.Eq(SpaceWire.ReadReply(new BufR(bad.Bytes.ToArray()), P).Protocol, (byte)0, "an unknown verdict byte is never guessed at");
            var ok = new BufW();
            SpaceWire.WriteReply(ok, new SpaceReply(P, SpaceCommandKind.FrontDonate, 13, (byte)FrontOutcome.Donated, 0, 25));
            TestAssert.Eq(SpaceWire.ReadReply(new BufR(ok.Bytes.ToArray()), P).Charged, 25, "a donation reply carries what the bar took");
            // The earlier families still read with their own bounds.
            var ops = new BufW();
            SpaceWire.WriteReply(ops, new SpaceReply(P, SpaceCommandKind.OpFund, 14, 1));
            TestAssert.Eq(SpaceWire.ReadReply(new BufR(ops.Bytes.ToArray()), P).Kind, SpaceCommandKind.OpFund, "an OPERATIONS reply still reads");
        }

        private static void Words()
        {
            string Line(Front f, FrontLogCode code, int a, int b, string by = "") =>
                FrontWords.Line(f, new FrontLogRow { Code = (byte)code, A = (short)a, B = (short)b }, by);

            TestAssert.Eq(Line(Front.Space, FrontLogCode.Done, (int)ProgrammeId.LaunchSatellite, 0), "DARKSTAR · SATELLITE LAUNCHED", "launch line");
            TestAssert.Eq(Line(Front.Space, FrontLogCode.Effect, (int)ProgrammeId.LaunchSatellite, 0), "DARKSTAR · SATELLITE ON STATION · OPTICAL", "launch lands on station");
            TestAssert.Eq(Line(Front.Cyber, FrontLogCode.Directive, (int)FrontDirective.Attack, 0, "VIPER-2"), "CYBER POSTURE ATTACK BY VIPER-2", "directive line names the pilot");
            TestAssert.Eq(Line(Front.Cyber, FrontLogCode.Directive, (int)FrontDirective.Attack, 0), "CYBER POSTURE ATTACK", "and not once the lock is over");
            TestAssert.Eq(Line(Front.Sof, FrontLogCode.Done, (int)ProgrammeId.Readiness, 3), "SHADOW · READINESS UP, NOW 3", "readiness up");
            TestAssert.Eq(Line(Front.Sof, FrontLogCode.Queued, (int)ProgrammeId.Readiness, 2), "SHADOW · READINESS 3 QUEUED", "a READINESS rung queues the next level");
            TestAssert.Eq(Line(Front.Cyber, FrontLogCode.Queued, (int)ProgrammeId.ZeroDay, 1), "HEXWARD · ZERO-DAY QUEUED", "queued");
            TestAssert.Eq(Line(Front.Space, FrontLogCode.Started, (int)ProgrammeId.Asat, 180), "DARKSTAR · ASAT FUNDED, BUILD 3:00", "funded");
            TestAssert.Eq(Line(Front.Cyber, FrontLogCode.Rebuild, (int)ProgrammeId.DataCenter, 0), "HEXWARD · DATA CENTER LOST, REBUILD QUEUED", "anchor lost");
            TestAssert.Eq(Line(Front.Space, FrontLogCode.Counter, 1, 0), "DARKSTAR · COUNTER PRESSURE ACTIVE · ENEMY SOF EXPOSURE +50 %", "counter on");
            TestAssert.Eq(Line(Front.Cyber, FrontLogCode.Counter, 1, 0), "HEXWARD · COUNTER PRESSURE ACTIVE · ENEMY SPACE COOLDOWNS x1.5", "cyber counter");
            TestAssert.Eq(Line(Front.Sof, FrontLogCode.Counter, 1, 0), "SHADOW · COUNTER PRESSURE ACTIVE · ENEMY CYBER TRACE +30 %", "sof counter");
            TestAssert.Eq(Line(Front.Sof, FrontLogCode.Counter, 0, 0), "SHADOW · COUNTER PRESSURE LAPSED", "counter off");
            TestAssert.Eq(Line(Front.Space, FrontLogCode.Priority, 50, 30), "FUNDING SPACE 50 · CYBER 30 · SOF 20", "priority line");
            TestAssert.Eq(Line(Front.Space, (FrontLogCode)99, 0, 0), "", "an unknown code says nothing");
            TestAssert.Eq(Line(Front.Sof, FrontLogCode.Done, 200, 0).Length > 0, true, "a bad programme id is clamped, never throws");

            foreach (FrontLogCode code in new[] { FrontLogCode.Queued, FrontLogCode.Started, FrontLogCode.Done, FrontLogCode.Directive, FrontLogCode.Priority, FrontLogCode.Focus, FrontLogCode.Rebuild, FrontLogCode.Counter, FrontLogCode.Effect })
                for (int f = 0; f < 3; f++) TestAssert.That(Line((Front)f, code, 1, 1).Length > 0, code + " has words on front " + f);

            // Every outcome has a sentence.
            for (byte o = 1; o <= FrontWords.MaxOutcome; o++) TestAssert.That(FrontWords.Outcome((FrontOutcome)o, Front.Space, 5, "A", 4).Length > 0, "outcome " + o + " has words");
            TestAssert.Eq(FrontWords.Outcome(FrontOutcome.LowAllocation, Front.Sof, 25), "NEEDS 25 ALLOCATION", "low allocation names the need");
            TestAssert.Eq(FrontWords.Outcome(FrontOutcome.Donated, Front.Sof, 0, "", 40), "FUNDED 40 ALLOCATION", "donation words");
        }

        private static void Codes()
        {
            var b = new FrontBook();
            var w = new FrontWorld { BirdsDown = 0, Uplinks = 2, UplinkMax = 2, Teams = 4 };
            TestAssert.Eq(b.RefusalCode(Front.Space, ProgrammeId.LaunchSatellite, w), FrontOutcome.BirdsUp, "all satellites up");
            TestAssert.Eq(b.RefusalCode(Front.Space, ProgrammeId.UplinkSite, w), FrontOutcome.UplinksMax, "uplinks at max");
            TestAssert.Eq(b.RefusalCode(Front.Sof, ProgrammeId.TrainTeam, w), FrontOutcome.TeamCap, "team cap");
            TestAssert.Eq(b.RefusalCode(Front.Cyber, ProgrammeId.Asat, w), FrontOutcome.NotAProgramme, "wrong front");
            TestAssert.Eq(b.RefusalCode(Front.Space, ProgrammeId.Asat, w), FrontOutcome.NeedsCyber3, "needs CYBER 3");
            TestAssert.Eq(b.RefusalCode(Front.Space, ProgrammeId.Readiness, w), FrontOutcome.None, "readiness can queue");
            TestAssert.Eq(b.Refusal(Front.Space, ProgrammeId.LaunchSatellite, w), "ALL SATELLITES ARE UP", "the old words are unchanged");
            TestAssert.Eq(b.Refusal(Front.Cyber, ProgrammeId.Asat, w), "NOT A CYBER PROGRAMME", "and name the front");
            TestAssert.Eq(b.Refusal(Front.Space, ProgrammeId.Readiness, w), "", "a clean queue says nothing");
            b.NoteLog(Front.Sof, FrontLogCode.Counter, 1, 0, 12f);
            TestAssert.Eq(b.Side(Front.Sof).Log[0].Code, (byte)FrontLogCode.Counter, "a host line lands on the log");
            for (int i = 0; i < 12; i++) b.NoteLog(Front.Sof, FrontLogCode.Effect, 1, 0, 13f);
            TestAssert.Eq(b.Side(Front.Sof).Log.Count, FrontRules.LogCapacity, "the log stays bounded");
        }

        private static void Counters()
        {
            CounterPressure none = FrontCounters.Against(false, false, false);
            TestAssert.That(!none.Any && none.SofExposure == 1f && none.SpaceCooldown == 1f && none.CyberTrace == 1f, "no lead, no pressure");
            CounterPressure space = FrontCounters.Against(true, false, false);
            TestAssert.That(space.Any && space.SofExposure == 1.5f && space.SpaceCooldown == 1f && space.CyberTrace == 1f, "SPACE leads: enemy SOF exposure x1.5 only");
            CounterPressure cyber = FrontCounters.Against(false, true, false);
            TestAssert.That(cyber.SpaceCooldown == 1.5f && cyber.SofExposure == 1f, "CYBER leads: enemy SPACE cooldowns x1.5 only");
            CounterPressure sof = FrontCounters.Against(false, false, true);
            TestAssert.Near(sof.CyberTrace, 1.3f, "SOF leads: enemy CYBER trace x1.3");
            CounterPressure all = FrontCounters.Against(true, true, true);
            TestAssert.That(all.SofExposure == 1.5f && all.SpaceCooldown == 1.5f && all.CyberTrace > 1.29f, "all three at once stack independently");
            TestAssert.That(CounterPressure.None.SofExposure == 1f && !CounterPressure.None.Any, "the empty pressure is neutral");

            // The lead that switches it on is the existing +40 rule.
            TestAssert.That(FrontSuperiority.CounterActive(FrontSuperiority.Compute(Front.Space, new FrontSides { BirdsUp = 3, UplinksLive = 2, UplinksTotal = 2, JamsOpponent = true }, new FrontSides { BirdsUp = 1, UplinksLive = 1, UplinksTotal = 2 })), "a dominant SPACE side presses");
            TestAssert.That(!FrontSuperiority.CounterActive(FrontSuperiority.Compute(Front.Space, new FrontSides { BirdsUp = 2, UplinksLive = 2, UplinksTotal = 2 }, new FrontSides { BirdsUp = 2, UplinksLive = 2, UplinksTotal = 2 })), "an even one does not");

            // The strongest enemy is the one with the most.
            var weak = new FrontSides { BirdsUp = 1, UplinksLive = 1 };
            var strong = new FrontSides { BirdsUp = 3, UplinksLive = 2 };
            TestAssert.That(FrontSuperiority.Strength(Front.Space, strong) > FrontSuperiority.Strength(Front.Space, weak), "more birds, stronger");
            TestAssert.That(FrontSuperiority.Strength(Front.Cyber, new FrontSides { HeldEnemyNodes = 2 }) > FrontSuperiority.Strength(Front.Cyber, new FrontSides { AnchorsLive = 3 }), "held nodes outweigh anchors");
            TestAssert.That(FrontSuperiority.Strength(Front.Sof, new FrontSides { TeamsAfield = 1 }) > FrontSuperiority.Strength(Front.Sof, new FrontSides { CampsLive = 2, HeldBuildings = 1 }), "a team afield outweighs a camp and a building");
        }

        private static void Ai()
        {
            var w = new FrontWorld { Teams = 0 };
            TestAssert.Eq(FrontAi.Next(Front.Space, 1, w, false), ProgrammeId.Readiness, "SPACE climbs the ladder");
            TestAssert.That(FrontAi.Next(Front.Space, 5, w, false) == null, "and stops at five");
            TestAssert.Eq(FrontAi.Next(Front.Sof, 1, w, false), ProgrammeId.TrainTeam, "SOF trains a team first");
            w.Teams = 2;
            TestAssert.Eq(FrontAi.Next(Front.Sof, 1, w, false), ProgrammeId.Readiness, "then readiness");
            w.HasHeldBuilding = true;
            TestAssert.Eq(FrontAi.Next(Front.Sof, 2, w, false), ProgrammeId.Readiness, "a FOB waits for readiness 3");
            TestAssert.Eq(FrontAi.Next(Front.Sof, 3, w, false), ProgrammeId.Fob, "and then takes it");
            TestAssert.Eq(FrontAi.Next(Front.Cyber, 3, w, true), ProgrammeId.ZeroDay, "CYBER blacks out a revealed SAM net from readiness 3");
            TestAssert.Eq(FrontAi.Next(Front.Cyber, 3, w, false), ProgrammeId.Readiness, "with no SAM net known it climbs");

            // A faction left alone for a while raises its readiness by itself (the S2 acceptance, in the pure rules).
            var book = new FrontBook();
            var world = new FrontWorld { UplinkMax = 2, Uplinks = 2 };
            float now = 0f;
            for (int minute = 0; minute < 40; minute++)
            {
                book.ShareTick(6000f, FrontRules.DefaultShare, now);
                for (int i = 0; i < 3; i++)
                {
                    var front = (Front)i;
                    if (book.Side(front).Queue.Count > 0) continue;
                    ProgrammeId? next = FrontAi.Next(front, book.Readiness(front), world, false);
                    if (next.HasValue) book.Queue(front, next.Value, world, now, out _);
                }
                now += 60f;
                book.Tick(now, world);
            }
            TestAssert.That(book.Readiness(Front.Space) >= 3, "left alone, an AI faction's SPACE front reaches readiness 3 on its own (got " + book.Readiness(Front.Space) + ")");
        }

        private static void Mirror()
        {
            var d = new FrontStateData { Protocol = P, Seq = 1, Now = 100f, PriorityLockUntil = 130f, PriorityBy = "A" };
            d.Fronts[1].DirectiveLockUntil = 150f; d.Fronts[1].DirectiveBy = "VIPER-2"; d.Fronts[1].Readiness = 3; d.Fronts[1].Superiority = 50;
            d.Fronts[0].Log.Add(new FrontLogRow { Code = (byte)FrontLogCode.Done, A = 1, Time = 90f });
            var mirror = new FrontMirror();
            TestAssert.Eq(mirror.Readiness(Front.Cyber), 5, "an unknown mirror opens everything (the host still enforces)");
            TestAssert.Near(mirror.Quality(Front.Cyber), 1f, "and is neutral");
            TestAssert.That(mirror.Apply(d, P, 400f), "a snapshot applies");
            TestAssert.Eq(mirror.Readiness(Front.Cyber), 3, "readiness is read from the mirror");
            TestAssert.Near(mirror.Quality(Front.Cyber), 1.125f, "quality from superiority +50");
            TestAssert.Near(mirror.State.Fronts[1].DirectiveLockUntil, 450f, "the lock moves onto the client's clock");
            TestAssert.Near(mirror.State.PriorityLockUntil, 430f, "so does the priority lock");
            TestAssert.Near(mirror.State.Fronts[0].Log[0].Time, 390f, "and a log row's time");
            TestAssert.That(!mirror.Apply(d, P, 401f), "a replayed sequence is refused");
            d.Seq = 2;
            d.Fronts[0].Queue.AddRange(new FrontQueueRow[5]);
            TestAssert.That(!mirror.Apply(d, P, 402f), "a queue over the codec's bound is refused");
        }

        private static void Mapping()
        {
            DirectorBias N(Front f) => DirectorBias.Neutral(f);
            DirectorBias D(FrontDirective d, bool pin = false, float x = 0f, float z = 0f) => new DirectorBias(d, pin, x, z);

            // SPACE: the neutral posture is the rule as it was.
            TestAssert.Near(N(Front.Space).NoContactScale, 1f, "SPACE neutral scan wait"); TestAssert.Eq(N(Front.Space).MaxPosts, 2, "SPACE neutral posts"); TestAssert.Near(N(Front.Space).RodWindowScale, 1f, "SPACE neutral rod window");
            TestAssert.Near(D(FrontDirective.Strike).NoContactScale, 1.5f, "STRIKE scans later"); TestAssert.Eq(D(FrontDirective.Strike).MaxPosts, 3, "STRIKE keeps three posts"); TestAssert.Near(D(FrontDirective.Strike).RodWindowScale, 2f, "STRIKE keeps the rod in view twice as long");
            TestAssert.Near(D(FrontDirective.Strike).HighValue(WatchKind.AirDefence), 1.25f, "STRIKE prefers air defence"); TestAssert.Near(D(FrontDirective.Strike).HighValue(WatchKind.Other), 1f, "and nothing else");
            TestAssert.Near(D(FrontDirective.Recon).HighValue(WatchKind.Armour), 1f, "RECON has no preference");
            TestAssert.Near(D(FrontDirective.Defend).NoContactScale, 2f, "DEFEND scans least"); TestAssert.Eq(D(FrontDirective.Defend).MaxPosts, 1, "DEFEND posts once");

            // CYBER.
            TestAssert.Near(N(Front.Cyber).RestScale, 1f, "CYBER neutral rest"); TestAssert.Eq(N(Front.Cyber).MaxHeld, 2, "CYBER neutral hold");
            TestAssert.Eq(N(Front.Cyber).NodePriority(NodeKind.SamC2), 5, "neutral SAM C2 first"); TestAssert.Eq(N(Front.Cyber).NodePriority(NodeKind.DataCenter), 1, "neutral DATA CENTER last");
            TestAssert.Eq(D(FrontDirective.Attack).NodePriority(NodeKind.Radar), CyberWatchBrain.Priority(NodeKind.Radar), "ATTACK is the pure CYBER order");
            TestAssert.That(D(FrontDirective.Defend).NodePriority(NodeKind.Uplink) > D(FrontDirective.Defend).NodePriority(NodeKind.SamC2), "DEFEND turns the order round");
            TestAssert.Near(D(FrontDirective.Defend).RestScale, 2f, "DEFEND rests"); TestAssert.Near(D(FrontDirective.Attack).RestScale, 0.5f, "ATTACK presses");

            // SOF.
            TestAssert.Near(N(Front.Sof).MinSabotageOdds, 55f, "SOF neutral bar is the old 55"); TestAssert.Near(N(Front.Sof).SabotageCooldownScale, 1f, "SOF neutral gap"); TestAssert.Near(N(Front.Sof).NearFrontScale, 1f, "SOF neutral reach"); TestAssert.That(N(Front.Sof).AllowRecon, "SOF neutral recon");
            TestAssert.Near(D(FrontDirective.Sabotage).MinSabotageOdds, 45f, "SABOTAGE takes 45 %"); TestAssert.Near(D(FrontDirective.Sabotage).SabotageCooldownScale, 0.5f, "SABOTAGE twice as often"); TestAssert.That(!D(FrontDirective.Sabotage).AllowRecon, "SABOTAGE never stands off");
            TestAssert.Near(D(FrontDirective.Hold).MinSabotageOdds, 65f, "HOLD wants 65 %"); TestAssert.Near(D(FrontDirective.Hold).NearFrontScale, 0.5f, "HOLD stays close"); TestAssert.Near(D(FrontDirective.Hold).SabotageCooldownScale, 1.5f, "HOLD sabotages less");

            // The pin: 15 km.
            DirectorBias pin = D(FrontDirective.Recon, true, 1000f, 1000f);
            TestAssert.That(pin.Near(1000f, 15999f) && !pin.Near(1000f, 16001f), "within 15 km of the pin");
            TestAssert.Near(pin.Focus(1000f, 1000f), 1.5f, "x1.5 near the pin"); TestAssert.Near(pin.Focus(90000f, 0f), 1f, "x1 far from it");
            TestAssert.Near(N(Front.Space).Focus(0f, 0f), 1f, "no pin, no boost"); TestAssert.Eq(pin.OddsBonus(1000f, 1000f), 10, "a pinned sabotage ranks 10 higher");
            TestAssert.That(!D(FrontDirective.Recon, true, float.NaN, 0f).HasFocus, "a pin that is not a number is no pin");

            // Every valid posture of every front gives a usable bias (no zero or negative scale).
            for (int f = 0; f < 3; f++)
                for (int d = 0; d <= 6; d++)
                {
                    if (!FrontRules.IsValid((Front)f, (FrontDirective)d)) continue;
                    DirectorBias bias = D((FrontDirective)d);
                    TestAssert.That(bias.NoContactScale > 0f && bias.RodWindowScale > 0f && bias.RestScale > 0f && bias.MaxPosts > 0 && bias.MaxHeld > 0 && bias.NearFrontScale > 0f && bias.SabotageCooldownScale > 0f, "posture " + d + " on front " + f);
                }
        }

        private static WatchInputs Inputs(float now, int posts = 0, float rodIn = 0f) =>
            new WatchInputs { Now = now, Humans = 1, Linked = true, BoardCount = 0, BoardCapacity = 20, OverlordPosts = posts, LiveMarks = 0, RodReadyIn = rodIn, RadarReadyIn = 0f, OpticalReadyIn = 99f };

        private static WatchTarget Target(int id, float x, float value, WatchKind kind = WatchKind.AirDefence) =>
            new WatchTarget(id, x, 0f, value, kind, false, false, 900f, -1f);

        private static void SpacePolicy()
        {
            var targets = new List<WatchTarget> { Target(1, 1000f, 20f) };
            var sites = new List<WatchSite>();

            // The post cap: 2 by default, 3 under STRIKE, 1 under DEFEND.
            var neutral = new WatchOfficerPolicy();
            TestAssert.Eq(neutral.Think(Inputs(100f, posts: 2), targets, sites).Why, WatchWhy.OverlordCap, "two posts up: the neutral director waits");
            var strike = new WatchOfficerPolicy { Bias = new DirectorBias(FrontDirective.Strike, false, 0f, 0f) };
            TestAssert.Eq(strike.Think(Inputs(100f, posts: 2), targets, sites).Action, WatchAction.Post, "STRIKE posts a third");
            var defend = new WatchOfficerPolicy { Bias = new DirectorBias(FrontDirective.Defend, false, 0f, 0f) };
            TestAssert.Eq(defend.Think(Inputs(100f, posts: 1), targets, sites).Why, WatchWhy.OverlordCap, "DEFEND stops at one");

            // The rod window: 90 s by default, 180 s under STRIKE.
            TestAssert.Eq(new WatchOfficerPolicy().Think(Inputs(100f, rodIn: 120f), targets, sites).Why, WatchWhy.RodNotReady, "a rod 120 s away is not in the neutral window");
            TestAssert.Eq(new WatchOfficerPolicy { Bias = new DirectorBias(FrontDirective.Strike, false, 0f, 0f) }.Think(Inputs(100f, rodIn: 120f), targets, sites).Action, WatchAction.Post, "but it is in STRIKE's");

            // The silence before a scan: 90 s neutral, 180 s DEFEND.
            var one = new List<WatchSite> { new WatchSite(1, 0f, 0f) };
            var none = new List<WatchTarget>();
            WatchOfficerPolicy scanner = new WatchOfficerPolicy();
            TestAssert.Eq(scanner.Think(Inputs(100f), none, one).Why, WatchWhy.Waiting, "first look: waiting");
            TestAssert.Eq(scanner.Think(Inputs(189f), none, one).Why, WatchWhy.Waiting, "still inside 90 s");
            TestAssert.Eq(scanner.Think(Inputs(191f), none, one).Action, WatchAction.Scan, "the neutral director scans after 90 s");
            var careful = new WatchOfficerPolicy { Bias = new DirectorBias(FrontDirective.Defend, false, 0f, 0f) };
            careful.Think(Inputs(100f), none, one);
            TestAssert.Eq(careful.Think(Inputs(191f), none, one).Why, WatchWhy.Waiting, "DEFEND is still waiting at 91 s");
            TestAssert.Eq(careful.Think(Inputs(281f), none, one).Action, WatchAction.Scan, "and scans after 180 s");

            // The pin ranks a target: the dearer one first, the pinned cheaper one when the pin is near it.
            var two = new List<WatchTarget> { Target(1, 0f, 20f), Target(2, 30000f, 24f) };
            TestAssert.Eq(new WatchOfficerPolicy().Think(Inputs(100f), two, sites).Id(0), 2, "no pin: the more valuable target leads");
            TestAssert.Eq(new WatchOfficerPolicy { Bias = new DirectorBias(FrontDirective.Recon, true, 0f, 0f) }.Think(Inputs(100f), two, sites).Id(0), 1, "a pin on the cheaper target: it leads");

            // A human at the desk no longer sends the director away; the old rule is still measured.
            var p = new WatchOfficerPolicy();
            p.RecordHuman(100f);
            TestAssert.That(p.Idle(1, 101f) && p.Idle(4, 101f), "always on");
            TestAssert.That(!p.QuietOfHumans(1, 159f) && p.QuietOfHumans(1, 160f) && !p.QuietOfHumans(2, 399f) && p.QuietOfHumans(2, 400f), "the old 60 s / 300 s numbers are kept");
        }
    }
}
