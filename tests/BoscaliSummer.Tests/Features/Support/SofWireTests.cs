using System;
using System.Collections.Generic;
using BoscaliSummer.Modules.Support.Domain.Calls;
using BoscaliSummer.Modules.Support.Domain.Cyber;
using BoscaliSummer.Modules.Support.Domain.Sof;
using BoscaliSummer.Modules.Support.Domain.Space;
using BoscaliSummer.Modules.Support.Runtime;

namespace BoscaliSummer.Tests.Features.Support
{
    /// <summary>M5a protocol 34: SOF commands and replies, the faction-only SOF state, its mirror, subscriptions, notices, posts and words.</summary>
    internal static class SofWireTests
    {
        private const byte P = 34;

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

        private static byte[] Cmd(SpaceCommand c) { var w = new BufW(); SpaceWire.WriteCommand(w, c); return w.Bytes.ToArray(); }
        private static byte[] Rep(SpaceReply r) { var w = new BufW(); SpaceWire.WriteReply(w, r); return w.Bytes.ToArray(); }
        private static byte[] Sta(SofStateData d) { var w = new BufW(); SofWire.WriteState(w, d); return w.Bytes.ToArray(); }

        private static void Eq<T>(T actual, T expected, string message) =>
            TestAssert.That(Equals(actual, expected), message + " (got " + actual + ", want " + expected + ")");

        public static void Run()
        {
            Commands();
            State();
            Mirror();
            Subscriptions();
            Notices();
            Posts();
            Words();
            Codes();
        }

        private static void Commands()
        {
            byte[] raise = Cmd(new SpaceCommand(P, SpaceCommandKind.SofRaise, 3));
            SpaceCommand back = SpaceWire.ReadCommand(new BufR(raise), P);
            TestAssert.That(back.Kind == SpaceCommandKind.SofRaise && back.RequestId == 3 && back.Mutating && back.IsSofVerb && !back.IsCyberVerb, "raise roundtrip");
            Eq(raise.Length, 3, "raise golden size");
            back = SpaceWire.ReadCommand(new BufR(Cmd(new SpaceCommand(P, SpaceCommandKind.SofOrder, 4, 2 | ((int)TeamVerb.Exfil << 2)))), P);
            TestAssert.That(back.Kind == SpaceCommandKind.SofOrder && back.Target == (2 | ((int)TeamVerb.Exfil << 2)), "order roundtrip");
            back = SpaceWire.ReadCommand(new BufR(Cmd(new SpaceCommand(P, SpaceCommandKind.SofMission, 5, 0, new[] { 1 | ((int)MissionKind.Sabotage << 2), 321 }))), P);
            TestAssert.That(back.Kind == SpaceCommandKind.SofMission && back.Ids.Length == 2 && back.Ids[0] == (1 | (3 << 2)) && back.Ids[1] == 321, "mission roundtrip");
            back = SpaceWire.ReadCommand(new BufR(Cmd(new SpaceCommand(P, SpaceCommandKind.SofDivert, 6, 0, new[] { 3, 7000000 }))), P);
            TestAssert.That(back.Kind == SpaceCommandKind.SofDivert && back.Ids[1] == 7000000, "divert roundtrip");
            TestAssert.That(SpaceWire.ReadCommand(new BufR(Cmd(new SpaceCommand(P, SpaceCommandKind.SofSync, 0))), P).Kind == SpaceCommandKind.SofSync, "sync roundtrip");
            TestAssert.That(!SpaceWire.ReadCommand(new BufR(Cmd(new SpaceCommand(P, SpaceCommandKind.SofSync, 0))), P).Mutating, "sync is not a mutation");
            // A SOF command carries exactly two ints; anything else is inert.
            TestAssert.That(SpaceWire.ReadCommand(new BufR(Cmd(new SpaceCommand(P, SpaceCommandKind.SofMission, 5, 0, new[] { 1 }))), P).Protocol == 0, "one id is malformed");
            TestAssert.That(SpaceWire.ReadCommand(new BufR(Cmd(new SpaceCommand(P, SpaceCommandKind.SofDivert, 5, 0, new[] { 1, 2, 3 }))), P).Protocol == 0, "three ids are malformed");
            TestAssert.That(SpaceWire.ReadCommand(new BufR(new byte[] { P, 20, 1 }), P).Protocol == 0, "kind 20 is out of range");
            TestAssert.That(SpaceWire.ReadCommand(new BufR(raise), 33).Protocol == P && SpaceWire.ReadCommand(new BufR(raise), 33).Kind == SpaceCommandKind.None, "a foreign protocol decodes to the byte alone");
            byte[] cut = Cmd(new SpaceCommand(P, SpaceCommandKind.SofMission, 5, 0, new[] { 4, 321 }));
            for (int n = 0; n < cut.Length; n++) TestAssert.That(SpaceWire.ReadCommand(new BufR(cut, n), P).Protocol == 0 || n < 2, "truncation at " + n + " is inert");
            Eq(new SpaceCommand(P, SpaceCommandKind.SofMission, 5, 0, new[] { 4, 321 }).Fingerprint() == new SpaceCommand(P, SpaceCommandKind.SofMission, 5, 0, new[] { 4, 322 }).Fingerprint(), false, "a changed payload changes the fingerprint");

            foreach (SofOutcome o in new[] { SofOutcome.Raised, SofOutcome.NoTarget, SofOutcome.Raising })
            {
                SpaceReply r = SpaceWire.ReadReply(new BufR(Rep(new SpaceReply(P, SpaceCommandKind.SofMission, 8, (byte)o, 2, 45, 12))), P);
                TestAssert.That(r.Kind == SpaceCommandKind.SofMission && r.SofVerdict == o && r.CallId == 2 && r.Charged == 45 && r.Detail == 12, o + " reply roundtrip");
            }
            TestAssert.That(SpaceWire.ReadReply(new BufR(Rep(new SpaceReply(P, SpaceCommandKind.SofDivert, 8, SofOutcomeWords.MaxOutcome + 1))), P).Protocol == 0, "an unknown verdict is never guessed at");
            TestAssert.That(SpaceWire.ReadReply(new BufR(Rep(new SpaceReply(P, SpaceCommandKind.SofSync, 8, 1))), P).Protocol == 0, "sync has no reply");
            TestAssert.That(SpaceWire.ReadReply(new BufR(Rep(new SpaceReply(P, SpaceCommandKind.CyberHop, 8, (byte)CyberOutcome.BoardFull))), P).CyberVerdict == CyberOutcome.BoardFull, "CYBER replies are unchanged");
        }

        private static SofStateData Full()
        {
            var s = new SofStateData { Protocol = P, Active = true, Seq = 7, Now = 300f, TeamCap = 3, TapIntrusions = 2, TapUntil = 540f };
            s.Camps.Add(new SofCampRow { Health = AnchorHealth.Damaged, X = 1200.5f, Z = -800f, Rebuild = 0 });
            s.Camps.Add(new SofCampRow { Health = AnchorHealth.Down, X = -3000f, Z = 4000.2f, Rebuild = 40 });
            s.Teams.Add(new SofTeamRow { Slot = 0, State = TeamState.Moving, Insert = Insertion.Ground, Push = true, HasDest = true, X = 100f, Z = 200f, DestX = 5000f, DestZ = 6000f, TargetX = 5000f, TargetZ = 6000f, Exposure = 34, Ammo = 70, Odds = 62, Mission = MissionKind.Sabotage, Exploit = true, TargetId = 12, EndsAt = 0f });
            s.Teams.Add(new SofTeamRow { Slot = 1, State = TeamState.OnSite, Insert = Insertion.Helicopter, Lasing = true, Carried = false, X = 9000f, Z = 9000f, TargetX = 9010f, TargetZ = 9020f, Exposure = 80, Ammo = 40, Odds = 55, Mission = MissionKind.Lase, TargetId = 4, EndsAt = 345f });
            s.Teams.Add(new SofTeamRow { Slot = 3, State = TeamState.Pinned, Wounded = true, LiftWaiting = true, Hold = true, X = -50f, Z = -50f, Exposure = 100, Ammo = 0, Odds = 10, Mission = MissionKind.Recon, EndsAt = 420f });
            for (int i = 1; i <= 6; i++) s.Targets.Add(new SofTargetRow { Id = i, Kind = (TargetKind)(i % 4), Sub = (AnchorSub)(i % 4), X = i * 1000f, Z = -i * 500f, Exploit = i % 2 == 0, Resisted = i == 3 });
            s.Held.Add(new SofHeldRow { Id = 1, X = 800f, Z = 900f, Until = 880f });
            s.Enemies.Add(new SofEnemyRow { X = 7000f, Z = 7000f });
            s.Events.Add(new SofEventRow { Seq = 5, Kind = SofEventKind.Pinned, Slot = 3, Mission = MissionKind.Recon });
            s.Events.Add(new SofEventRow { Seq = 6, Kind = SofEventKind.Success, Slot = 0, Mission = MissionKind.Sabotage });
            return s;
        }

        private static void State()
        {
            SofStateData s = Full();
            byte[] bytes = Sta(s);
            TestAssert.That(bytes.Length < 600, "a full SOF message stays small (" + bytes.Length + " B)");
            Eq(SofWire.StateSize(s), bytes.Length, "size counter agrees");
            SofStateData r = SofWire.ReadState(new BufR(bytes), P);
            TestAssert.That(r.Protocol == P && r.Active && r.Seq == 7 && r.TeamCap == 3 && r.TapIntrusions == 2, "header");
            TestAssert.That(Math.Abs(r.TapUntil - 540f) < 0.2f, "tap expiry");
            Eq(r.Camps.Count, 2, "camps"); Eq(r.Camps[0].Health, AnchorHealth.Damaged, "camp health"); Eq(r.Camps[1].Rebuild, (byte)40, "camp rebuild");
            TestAssert.That(Math.Abs(r.Camps[0].X - 1200.5f) < 0.11f && Math.Abs(r.Camps[1].Z - 4000.2f) < 0.11f, "camp points");
            Eq(r.Teams.Count, 3, "teams");
            SofTeamRow t0 = r.Teams[0], t1 = r.Teams[1], t2 = r.Teams[2];
            TestAssert.That(t0.State == TeamState.Moving && t0.Push && t0.HasDest && t0.Exploit && t0.Mission == MissionKind.Sabotage && t0.TargetId == 12 && t0.Exposure == 34 && t0.Ammo == 70 && t0.Odds == 62, "team 0");
            TestAssert.That(t1.Slot == 1 && t1.Lasing && t1.Insert == Insertion.Helicopter && t1.Mission == MissionKind.Lase && Math.Abs(t1.EndsAt - 345f) < 0.2f, "team 1");
            TestAssert.That(t2.Slot == 3 && t2.State == TeamState.Pinned && t2.Wounded && t2.LiftWaiting && t2.Hold && t2.Exposure == 100, "team 3");
            Eq(r.Targets.Count, 6, "targets"); TestAssert.That(r.Targets[2].Resisted && r.Targets[1].Exploit && r.Targets[0].Kind == TargetKind.Anchor, "target flags");
            Eq(r.Held.Count, 1, "held"); Eq(r.Enemies.Count, 1, "enemy team");
            Eq(r.Events.Count, 2, "events"); TestAssert.That(r.Events[1].Kind == SofEventKind.Success && r.Events[1].Mission == MissionKind.Sabotage && r.Events[0].Slot == 3, "event rows");
            TestAssert.That(r.SameAs(SofWire.ReadState(new BufR(bytes), P)), "two decodes agree");
            // Inactive: header only.
            byte[] off = Sta(new SofStateData { Protocol = P, Active = false, Seq = 2, Now = 10f });
            TestAssert.That(off.Length <= 8 && !SofWire.ReadState(new BufR(off), P).Active, "an inactive state is a header");
            // Foreign protocol and bad bytes.
            SofStateData foreign = SofWire.ReadState(new BufR(bytes), 33);
            TestAssert.That(foreign.Protocol == P && !foreign.Active && foreign.Teams.Count == 0, "foreign protocol is the byte alone");
            for (int n = 0; n < bytes.Length; n++)
            {
                SofStateData cutState = SofWire.ReadState(new BufR(bytes, n), P);
                TestAssert.That(cutState.Protocol == 0 || n < 2 || !cutState.Active, "truncation at " + n + " is inert");
            }
            byte[] bad = (byte[])bytes.Clone();
            bad[1] = 2; // an unknown flag
            TestAssert.That(SofWire.ReadState(new BufR(bad), P).Protocol == 0, "an unknown flag refuses the message");
            bad = (byte[])bytes.Clone();
            for (int i = 0; i < bad.Length; i++) bad[i] = 0xFF;
            bad[0] = P;
            TestAssert.That(SofWire.ReadState(new BufR(bad), P).Protocol == 0, "garbage is inert");
            // Oversized lists are clipped on write, never trusted on read.
            SofStateData big = Full();
            for (int i = 0; i < 50; i++) { big.Targets.Add(new SofTargetRow { Id = 100 + i, Kind = TargetKind.Ground, X = 1f, Z = 1f }); big.Teams.Add(big.Teams[0]); big.Enemies.Add(new SofEnemyRow()); }
            SofStateData clipped = SofWire.ReadState(new BufR(Sta(big)), P);
            TestAssert.That(clipped.Targets.Count == SofWire.MaxTargets && clipped.Teams.Count == SofWire.MaxTeams && clipped.Enemies.Count == SofWire.MaxEnemies, "lists are clipped to their caps");
            TestAssert.That(Sta(big).Length < 1200, "the worst message stays under 1200 B (" + Sta(big).Length + ")");
        }

        private static void Mirror()
        {
            var m = new SofMirror();
            SofStateData s = Full();
            TestAssert.That(m.Apply(s, P, 400f) && m.Known && m.Seq == 7, "applied");
            TestAssert.That(Math.Abs(m.State.TapUntil - 640f) < 0.2f && Math.Abs(m.State.Teams[1].EndsAt - 445f) < 0.2f && Math.Abs(m.State.Held[0].Until - 980f) < 0.2f, "expiries move onto the client clock");
            TestAssert.That(!m.Apply(s, P, 400f), "an old sequence is refused");
            SofStateData newer = Full(); newer.Seq = 8;
            TestAssert.That(m.Apply(newer, P, 410f), "a newer one is applied");
            TestAssert.That(!m.Apply(Full(), 33, 400f) && !m.Apply(null, P, 400f), "foreign and null are refused");
            TestAssert.That(m.TryLase(out string callsign, out float x, out float z) && callsign == "B-1" && Math.Abs(x - 9010f) < 0.2f && Math.Abs(z - 9020f) < 0.2f, "AIM: TEAM reads the lasing team");
            m.ResetLink();
            TestAssert.That(!m.Known && !m.TryLase(out _, out _, out _), "a link reset forgets everything");
        }

        private static void Subscriptions()
        {
            var subs = new SofSubscriptions();
            SofStateData a = Full();
            SofStateData first = subs.Next(11, 1, a, 100f, 10f);
            TestAssert.That(first != null && first.Seq == 1, "a new member gets a fresh full state");
            TestAssert.That(subs.Next(11, 1, Full(), 101f, 10.5f) == null, "nothing changed and inside the gap: nothing sent");
            TestAssert.That(subs.Next(11, 1, Full(), 105f, 13f) == null, "nothing changed: nothing sent");
            SofStateData moved = Full(); moved.Teams[0] = new SofTeamRow { Slot = 0, State = TeamState.Moving, X = 777f, Z = 1f, Exposure = 34 };
            TestAssert.That(subs.Next(11, 1, moved, 106f, 11f) == null, "a change inside the 2 s gap waits");
            SofStateData later = subs.Next(11, 1, moved, 107f, 12.5f);
            TestAssert.That(later != null && later.Seq == 2, "then it goes");
            subs.Resync(11);
            TestAssert.That(subs.Next(11, 1, moved, 108f, 12.6f) != null, "a resync forces one");
            TestAssert.That(subs.Next(11, 2, moved, 109f, 12.7f) != null, "another faction forces one");
            subs.Prune(new HashSet<ulong>());
            Eq(subs.Count, 0, "members who left are forgotten");
        }

        private static void Notices()
        {
            var t = new SofNoticeTracker();
            SofStateData s = Full();
            TestAssert.That(t.Observe(true, s, 100f, false) == SofNoticeKind.None, "the first sight is silent");
            s.Events.Add(new SofEventRow { Seq = 9, Kind = SofEventKind.Pinned, Slot = 1, Mission = MissionKind.None });
            Eq(t.Observe(true, s, 101f, false), SofNoticeKind.Pinned, "a new pin is announced");
            s.Events.Add(new SofEventRow { Seq = 10, Kind = SofEventKind.Lost, Slot = 1, Mission = MissionKind.None });
            Eq(t.Observe(true, s, 102f, false), SofNoticeKind.None, "one notice per 3 s");
            s.Events.Add(new SofEventRow { Seq = 11, Kind = SofEventKind.Success, Slot = 0, Mission = MissionKind.Recon });
            Eq(t.Observe(true, s, 105f, true), SofNoticeKind.None, "QUIET drops them");
            Eq(t.Observe(false, s, 106f, false), SofNoticeKind.None, "no mirror, no notice");
        }

        private static void Posts()
        {
            TestAssert.That(TaskedKinds.TryGet(SupportActionId.SofCover, out TaskedKind cover) && cover.Domain == TaskedDomain.Sof && cover.Label == "COVER TEAM", "cover kind");
            TestAssert.That(TaskedKinds.TryGet(SupportActionId.SofLase, out TaskedKind lase) && lase.Domain == TaskedDomain.Sof && lase.Label == "LASE TARGET", "lase kind");
            TestAssert.That(TaskedKinds.IsHostPost(SupportActionId.SofCover) && TaskedKinds.IsHostPost(SupportActionId.CyberBlackout) && !TaskedKinds.IsHostPost(SupportActionId.Artillery), "host posts");
            Eq(TaskedFees.Quote(SupportActionId.SofCover, 1, 5, false), 0, "a SOF post is free to claim");
            Eq(TaskedFees.Quote(SupportActionId.CyberJamRadar, 25, 5, false), 10, "CYBER packages still cost");
            Eq(SofPosts.Title(SupportActionId.SofCover, 1), "COVER TEAM A-1", "board title");
            Eq(TaskedKinds.Slab(TaskedDomain.Sof), "SOF", "slab");
            Eq(Aim.Pick(false, false, true), AimSource.Team, "a lasing team is the last aim source");
            Eq(Aim.Pick(true, false, true), AimSource.Pod, "the pod wins");
            Eq(Aim.Pick(false, true, true), AimSource.Map, "a map pick wins over the team");
            Eq(Aim.Label(AimSource.Team), "AIM: TEAM", "label");
        }

        private static void Codes()
        {
            foreach (var p in new[] { new[] { 0f, 0f }, new[] { 1234.5f, -9876.5f }, new[] { 600000f, 600000f }, new[] { -600000f, -600000f } })
            {
                int packed = SofCodes.PackPoint(p[0], p[1]);
                TestAssert.That(packed >= 0 && SofCodes.TryUnpackPoint(packed, out float x, out float z) && Math.Abs(x - p[0]) <= 20.01f && Math.Abs(z - p[1]) <= 20.01f, "point " + p[0] + "," + p[1] + " roundtrips within half a cell");
            }
            TestAssert.That(SofCodes.PackPoint(9e9f, -9e9f) >= 0, "a far point clamps instead of overflowing");
            TestAssert.That(!SofCodes.TryUnpackPoint(-1, out _, out _), "a negative code is refused");
            for (int slot = 0; slot < 4; slot++)
                foreach (TeamVerb v in new[] { TeamVerb.Push, TeamVerb.Hold, TeamVerb.Exfil, TeamVerb.Lift, TeamVerb.Cancel })
                {
                    TestAssert.That(SofCodes.TryUnpackOrder(SofCodes.PackOrder(slot, v), out int s, out TeamVerb back) && s == slot && back == v, "order " + slot + v);
                }
            TestAssert.That(!SofCodes.TryUnpackOrder(0, out _, out _) && !SofCodes.TryUnpackOrder(6 << 2, out _, out _), "verb 0 and 6 are refused");
            foreach (MissionKind k in new[] { MissionKind.Recon, MissionKind.Lase, MissionKind.Sabotage, MissionKind.Seize, MissionKind.Tap })
                TestAssert.That(SofCodes.TryUnpackMission(SofCodes.PackMission(3, k), out int s, out MissionKind back) && s == 3 && back == k, "mission " + k);
            TestAssert.That(!SofCodes.TryUnpackMission(0, out _, out _) && !SofCodes.TryUnpackMission(6 << 2, out _, out _), "kind 0 and 6 are refused");
        }

        private static void Words()
        {
            for (byte o = 1; o <= SofOutcomeWords.MaxOutcome; o++)
            {
                string w = SofOutcomeWords.Of((SofOutcome)o, 5);
                TestAssert.That(w.Length > 0, "words for " + (SofOutcome)o);
                bool refusal = o >= (byte)SofOutcome.NoTarget;
                TestAssert.That(refusal == w.StartsWith("NEGATIVE: ") , "only refusals start NEGATIVE (" + (SofOutcome)o + ")");
                if (refusal) TestAssert.That(w.Contains(" — "), "a refusal says what fixes it (" + (SofOutcome)o + ")");
            }
            TestAssert.That(new SofResult(SofOutcome.Raised).Ok && !new SofResult(SofOutcome.NoTarget).Ok, "ok flags");
            Eq(SofWords.Clock(125f), "2:05", "clock"); Eq(SofWords.Clock(float.NaN), "0:00", "bad clock");
            var t = new SofTeamRow { Slot = 0, State = TeamState.Moving, X = 0f, Z = 0f, DestX = 3500f, DestZ = 0f, Exposure = 34 };
            Eq(SofPageWords.TeamLine(t, 0f), "A-1 · MOVING · ETA 6:00 · EXP 34 %", "moving line");
            t.Push = true;
            Eq(SofPageWords.TeamLine(t, 0f), "A-1 · MOVING · PUSH · ETA 4:00 · EXP 34 %", "pushed line");
            Eq(SofPageWords.TeamLine(new SofTeamRow { Slot = 2, State = TeamState.Raising, EndsAt = 90f }, 0f), "C-1 · RAISING · 1:30", "raising line");
            Eq(SofPageWords.Sub(null), "NO CAMP STANDING", "no state");
            Eq(SofPageWords.Sub(Full()), "1 CAMP · 3/3 TEAMS · 1 HELD", "sub line");
            foreach (SofEventKind k in Enum.GetValues(typeof(SofEventKind)))
                TestAssert.That(SofPageWords.EventLine(new SofEventRow { Kind = k, Slot = 1, Mission = MissionKind.Recon }).Length > 0, "event words " + k);
        }
    }
}
