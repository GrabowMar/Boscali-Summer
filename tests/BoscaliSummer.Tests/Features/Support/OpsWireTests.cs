using System;
using System.Collections.Generic;
using BoscaliSummer.Modules.Support.Domain.Cyber;
using BoscaliSummer.Modules.Support.Domain.Ops;
using BoscaliSummer.Modules.Support.Domain.Space;

namespace BoscaliSummer.Tests.Features.Support
{
    /// <summary>M6a/M6b protocol 36: OPERATIONS commands and replies, the faction-only state, its mirror, subscriptions, notices and words.</summary>
    internal static class OpsWireTests
    {
        private const byte P = 36;

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
        private static byte[] Sta(OpsStateData d) { var w = new BufW(); OpsWire.WriteState(w, d); return w.Bytes.ToArray(); }

        private static void Eq<T>(T actual, T expected, string message) =>
            TestAssert.That(Equals(actual, expected), message + " (got " + actual + ", want " + expected + ")");

        public static void Run()
        {
            Commands();
            State();
            Mirror();
            Subscriptions();
            Notices();
            Words();
        }

        private static void Commands()
        {
            SpaceCommand fund = SpaceWire.ReadCommand(new BufR(Cmd(new SpaceCommand(P, SpaceCommandKind.OpFund, 3, 1 | (1 << 1)))), P);
            TestAssert.That(fund.Kind == SpaceCommandKind.OpFund && fund.Target == 3 && fund.Mutating && fund.IsOpsVerb && !fund.IsSofVerb && !fund.IsCyberVerb, "fund roundtrip");
            SpaceCommand plan = SpaceWire.ReadCommand(new BufR(Cmd(new SpaceCommand(P, SpaceCommandKind.OpPlan, 4, 0, new[] { 2, 77 }))), P);
            TestAssert.That(plan.Kind == SpaceCommandKind.OpPlan && plan.Ids.Length == 2 && plan.Ids[0] == 2 && plan.Ids[1] == 77 && plan.IsOpsVerb, "plan roundtrip");
            SpaceCommand cancel = SpaceWire.ReadCommand(new BufR(Cmd(new SpaceCommand(P, SpaceCommandKind.OpCancel, 5, 1))), P);
            TestAssert.That(cancel.Kind == SpaceCommandKind.OpCancel && cancel.Target == 1 && cancel.Mutating, "cancel roundtrip");
            SpaceCommand sync = SpaceWire.ReadCommand(new BufR(Cmd(new SpaceCommand(P, SpaceCommandKind.OpSync, 0))), P);
            TestAssert.That(sync.Kind == SpaceCommandKind.OpSync && !sync.Mutating && !sync.IsOpsVerb, "sync roundtrip and is not a mutation");
            TestAssert.That(SpaceWire.ReadCommand(new BufR(Cmd(new SpaceCommand(P, SpaceCommandKind.OpPlan, 5, 0, new[] { 1 }))), P).Protocol == 0, "one id is malformed");
            TestAssert.That(SpaceWire.ReadCommand(new BufR(Cmd(new SpaceCommand(P, SpaceCommandKind.OpPlan, 5, 0, new[] { 1, 2, 3 }))), P).Protocol == 0, "three ids are malformed");
            TestAssert.That(SpaceWire.ReadCommand(new BufR(new byte[] { P, 20, 1 }), P).Protocol == 0, "kind 20 is out of range");
            byte[] bytes = Cmd(new SpaceCommand(P, SpaceCommandKind.OpFund, 3, 1));
            TestAssert.That(SpaceWire.ReadCommand(new BufR(bytes), 34).Protocol == P && SpaceWire.ReadCommand(new BufR(bytes), 34).Kind == SpaceCommandKind.None, "a protocol-34 host sees the byte alone");
            byte[] cut = Cmd(new SpaceCommand(P, SpaceCommandKind.OpPlan, 5, 0, new[] { 2, 321 }));
            for (int n = 0; n < cut.Length; n++) TestAssert.That(SpaceWire.ReadCommand(new BufR(cut, n), P).Protocol == 0 || n < 2, "truncation at " + n + " is inert");
            Eq(new SpaceCommand(P, SpaceCommandKind.OpPlan, 5, 0, new[] { 2, 321 }).Fingerprint() == new SpaceCommand(P, SpaceCommandKind.OpPlan, 5, 0, new[] { 2, 322 }).Fingerprint(), false, "a changed payload changes the fingerprint");

            foreach (OpOutcome o in new[] { OpOutcome.Started, OpOutcome.Funded, OpOutcome.NoTarget, OpOutcome.Offline })
            {
                SpaceReply r = SpaceWire.ReadReply(new BufR(Rep(new SpaceReply(P, SpaceCommandKind.OpFund, 8, (byte)o, 1, 50, 12))), P);
                TestAssert.That(r.Kind == SpaceCommandKind.OpFund && r.Outcome == (byte)o && r.CallId == 1 && r.Charged == 50 && r.Detail == 12, o + " reply roundtrip");
            }
            TestAssert.That(SpaceWire.ReadReply(new BufR(Rep(new SpaceReply(P, SpaceCommandKind.OpPlan, 8, OpsWords.MaxOutcome + 1))), P).Protocol == 0, "an unknown verdict is never guessed at");
            TestAssert.That(SpaceWire.ReadReply(new BufR(Rep(new SpaceReply(P, SpaceCommandKind.OpSync, 8, 1))), P).Protocol == 0, "sync has no reply");
            TestAssert.That(SpaceWire.ReadReply(new BufR(Rep(new SpaceReply(P, SpaceCommandKind.SofRaise, 8, 1))), P).Kind == SpaceCommandKind.SofRaise, "SOF replies are unchanged");
        }

        private static OpsStateData Full()
        {
            var s = new OpsStateData { Protocol = P, Active = true, CyberOps = true, SofOps = true, Seq = 9, Now = 500f, BirdsDown = 0b101 };
            s.BirdPercent[0] = 40; s.BirdPercent[2] = 77;
            s.Rows.Add(new OpsRow { Domain = OpDomain.Cyber, Kind = OpKind.Asat, State = OpState.Execute, HasTarget = true, Percent = 100, WorkPercent = 31, Goal = 940, TargetId = 1, MyCr = 275, X = 1200.5f, Z = -800f, EndsAt = 548f });
            s.Rows.Add(new OpsRow { Domain = OpDomain.Sof, Kind = OpKind.Fob, State = OpState.Funding, Paused = true, HasTarget = true, Percent = 62, WorkPercent = 12, Goal = 625, TargetId = 3, MyCr = 100, X = -3000f, Z = 4000.2f, EndsAt = 0f });
            s.Pings.Add(new OpsPingRow { Kind = OpKind.Asat, Phase = OpPingPhase.Half, Seq = 4, Until = 560f, Name = "BOSCALI" });
            s.Pings.Add(new OpsPingRow { Kind = OpKind.ZeroDay, Phase = OpPingPhase.Execute, Seq = 5, Until = 580f, Name = "PRIMEVA" });
            s.Pings.Add(new OpsPingRow { Kind = OpKind.Asat, Phase = OpPingPhase.Loss, Seq = 6, Detail = 2, Until = 560f, Name = "BOSCALI" });
            s.Events.Add(new OpsEventRow { Seq = 7, Kind = OpEventKind.Execute, Domain = OpDomain.Cyber, Op = OpKind.Asat });
            s.Events.Add(new OpsEventRow { Seq = 8, Kind = OpEventKind.Broken, Domain = OpDomain.Sof, Op = OpKind.Fob });
            s.Flights.Add(new OpsFlightRow { Id = 3, X = 5000f, Z = -2000f, EndsAt = 560f, Seconds = 60 });
            s.Log.Add(new WatchLogRow { Seq = 4, Domain = WatchDomain.Cyber, Code = WatchCode.CyberHop, A = (byte)NodeKind.SamC2, B = 0 });
            s.Log.Add(new WatchLogRow { Seq = 5, Domain = WatchDomain.Sof, Code = WatchCode.SofSabotage, A = 5, B = 65 });
            s.Log.Add(new WatchLogRow { Seq = 130, Domain = WatchDomain.Ops, Code = WatchCode.OpFund, A = (byte)OpKind.ZeroDay, B = 64 });
            return s;
        }

        private static void State()
        {
            OpsStateData s = Full();
            byte[] bytes = Sta(s);
            TestAssert.That(bytes.Length < 200, "a full OPERATIONS message stays small (" + bytes.Length + " B)");
            Eq(OpsWire.StateSize(s), bytes.Length, "size counter agrees");
            OpsStateData r = OpsWire.ReadState(new BufR(bytes), P);
            TestAssert.That(r.Protocol == P && r.Active && r.CyberOps && r.SofOps && r.Seq == 9 && r.BirdsDown == 5 && r.BirdPercent[0] == 40 && r.BirdPercent[2] == 77, "header and birds");
            Eq(r.Rows.Count, 2, "rows");
            OpsRow a = r.Rows[0], b = r.Rows[1];
            TestAssert.That(a.Domain == OpDomain.Cyber && a.Kind == OpKind.Asat && a.State == OpState.Execute && a.HasTarget && !a.Paused && a.Percent == 100 && a.WorkPercent == 31 &&
                a.Goal == 940 && a.TargetId == 1 && a.MyCr == 275 && Math.Abs(a.EndsAt - 548f) < 0.2f && Math.Abs(a.X - 1200.5f) < 0.11f, "row 0");
            TestAssert.That(b.Domain == OpDomain.Sof && b.Kind == OpKind.Fob && b.State == OpState.Funding && b.Paused && b.Percent == 62 && b.EndsAt == 0f, "row 1");
            Eq(r.Pings.Count, 3, "pings"); Eq(r.Pings[0].Name, "BOSCALI", "ping name"); Eq(r.Pings[1].Phase, OpPingPhase.Execute, "ping phase");
            TestAssert.That(r.Pings[2].Phase == OpPingPhase.Loss && r.Pings[2].Detail == 2, "a loss ping names the bird");
            Eq(r.Events.Count, 2, "events"); Eq(r.Events[1].Kind, OpEventKind.Broken, "event kind"); Eq(r.Events[1].Domain, OpDomain.Sof, "event domain");
            Eq(r.Log.Count, 3, "OVERLORD log rows");
            TestAssert.That(r.Log[0].Seq == 4 && r.Log[0].Domain == WatchDomain.Cyber && r.Log[0].Code == WatchCode.CyberHop && r.Log[0].A == 1 && r.Log[0].B == 0, "log row 0");
            TestAssert.That(r.Log[1].Domain == WatchDomain.Sof && r.Log[1].Code == WatchCode.SofSabotage && r.Log[1].A == 5 && r.Log[1].B == 65, "log row 1");
            TestAssert.That(r.Log[2].Seq == 130 && r.Log[2].Domain == WatchDomain.Ops && r.Log[2].Code == WatchCode.OpFund, "a sequence over 127 survives the varint");
            Eq(WatchWords.Line(r.Log[1]), "OVERLORD · SABOTAGE EW TRUCK — B-1, ODDS 65 %", "the console words come from the mirrored row alone");
            Eq(r.Flights.Count, 1, "flights"); TestAssert.That(r.Flights[0].Id == 3 && r.Flights[0].Seconds == 60 && Math.Abs(r.Flights[0].EndsAt - 560f) < 0.2f, "flight");
            TestAssert.That(r.TryRow(OpDomain.Sof, out OpsRow sof) && sof.Kind == OpKind.Fob && !new OpsStateData().TryRow(OpDomain.Cyber, out _), "TryRow finds a domain");

            var idle = new OpsStateData { Protocol = P, Active = false, Seq = 2, Now = 10f };
            OpsStateData ri = OpsWire.ReadState(new BufR(Sta(idle)), P);
            TestAssert.That(ri.Protocol == P && !ri.Active && ri.Rows.Count == 0, "an inactive state is a header only");
            TestAssert.That(OpsWire.ReadState(new BufR(bytes), 34).Protocol == P && OpsWire.ReadState(new BufR(bytes), 34).Rows.Count == 0, "a foreign protocol is the byte alone");
            for (int n = 0; n < bytes.Length; n++)
                TestAssert.That(OpsWire.ReadState(new BufR(bytes, n), P).Protocol == 0 || n < 1, "truncation at " + n + " is inert");
            // Out-of-range bytes: a bird mask above 7, a kind 3 ping 0, a row count of 3.
            byte[] bad = (byte[])bytes.Clone(); // header: P, flags, seq (1 byte), now (4) => the bird mask is byte 7
            bad[7] = 0x08;
            Eq(OpsWire.ReadState(new BufR(bad), P).Protocol, (byte)0, "a bird mask above 7 is refused");
            // A log row with a domain or a code that does not exist is refused whole, never guessed at.
            byte[] logBytes = Sta(s);
            int logAt = logBytes.Length - 13; // rows of 4, 4 and 5 bytes (the third sequence needs a two-byte varint): the first row starts here
            byte[] badDomain = (byte[])logBytes.Clone(); badDomain[logAt + 1] = (byte)(3 | ((int)WatchCode.CyberHop << 2));
            Eq(OpsWire.ReadState(new BufR(badDomain), P).Protocol, (byte)0, "log domain 3 is refused");
            byte[] badCode = (byte[])logBytes.Clone(); badCode[logAt + 1] = (byte)(0 | (63 << 2));
            Eq(OpsWire.ReadState(new BufR(badCode), P).Protocol, (byte)0, "an unknown reason code is refused");
            byte[] zeroSeq = (byte[])logBytes.Clone(); zeroSeq[logAt] = 0;
            Eq(OpsWire.ReadState(new BufR(zeroSeq), P).Protocol, (byte)0, "log sequence 0 is refused");
            var worst = Full();
            while (worst.Pings.Count < 4) worst.Pings.Add(new OpsPingRow { Kind = OpKind.Fob, Phase = OpPingPhase.Half, Seq = 10 + worst.Pings.Count, Until = 600f, Name = "ABCDEFGHIJKL" });
            while (worst.Events.Count < 3) worst.Events.Add(new OpsEventRow { Seq = 20, Kind = OpEventKind.Half, Domain = OpDomain.Cyber, Op = OpKind.ZeroDay });
            TestAssert.That(OpsWire.StateSize(worst) < 1200, "the worst case fits one message");
            Console.WriteLine("WIRE ops worst-case state: " + OpsWire.StateSize(worst) + " bytes (budget 1200)");
        }

        private static void Mirror()
        {
            var mirror = new OpsMirror();
            TestAssert.That(mirror.BirdUp(BirdKind.Optical), "an unknown mirror says every bird is up");
            OpsStateData s = Full();
            TestAssert.That(mirror.Apply(s, P, 520f), "a first state applies");
            Eq(mirror.State.Now, 520f, "re-based onto the client clock");
            TestAssert.That(Math.Abs(mirror.State.Rows[0].EndsAt - 568f) < 0.01f && Math.Abs(mirror.State.Pings[0].Until - 580f) < 0.01f && Math.Abs(mirror.State.Flights[0].EndsAt - 580f) < 0.01f, "deadlines shift by the clock offset");
            TestAssert.That(!mirror.BirdUp(BirdKind.Optical) && mirror.BirdUp(BirdKind.Radar) && !mirror.BirdUp(BirdKind.Kinetic), "dead birds read from the mask");
            TestAssert.That(mirror.State.Log.Count == 3 && mirror.State.Log[2].Seq == 130, "the log rides the mirror");
            TestAssert.That(!mirror.Apply(s, P, 521f), "the same sequence is dropped");
            s.Seq = 8;
            TestAssert.That(!mirror.Apply(s, P, 521f), "an older sequence is dropped");
            s.Seq = 10; s.Protocol = 34;
            TestAssert.That(!mirror.Apply(s, P, 521f), "a foreign protocol is dropped");
            s.Protocol = P;
            while (s.Rows.Count < 3) s.Rows.Add(default);
            TestAssert.That(!mirror.Apply(s, P, 521f), "an oversized list is dropped");
            mirror.ResetLink();
            TestAssert.That(!mirror.Known && mirror.Floor == 0, "a new link starts over");
        }

        private static void Subscriptions()
        {
            var subs = new OpsSubscriptions();
            OpsStateData s = Full();
            OpsStateData first = subs.Next(7, 1, s, 500f, 10f);
            TestAssert.That(first != null && first.Seq == 1, "a new member gets a full state");
            TestAssert.That(subs.Next(7, 1, Full(), 501f, 10.5f) == null, "nothing changed: nothing sent");
            OpsStateData changed = Full(); changed.Rows[0] = new OpsRow { Domain = OpDomain.Cyber, Kind = OpKind.Asat, State = OpState.Done, Percent = 100, Goal = 940 };
            TestAssert.That(subs.Next(7, 1, changed, 502f, 11f) == null, "a change inside the two second gap waits");
            TestAssert.That(subs.Next(7, 1, changed, 503f, 12.5f) != null, "then goes");
            subs.Resync(7);
            TestAssert.That(subs.Next(7, 1, changed, 504f, 12.6f) != null, "a sync forces one");
            TestAssert.That(subs.Next(7, 2, changed, 505f, 13f) != null, "a faction change forces one");
            TestAssert.That(subs.Next(0, 1, changed, 505f, 14f) == null, "player 0 is refused");
            subs.Prune(new HashSet<ulong>());
            Eq(subs.Count, 0, "pruned members are forgotten");
        }

        private static void Notices()
        {
            var t = new OpsNoticeTracker();
            OpsStateData s = Full();
            TestAssert.That(t.Observe(true, s, 500f, false).Kind == OpsNoticeKind.None, "the first sight of a mirror is silent");
            s.Events.Add(new OpsEventRow { Seq = 9, Kind = OpEventKind.Execute, Domain = OpDomain.Cyber, Op = OpKind.Asat });
            OpsNotice n = t.Observe(true, s, 501f, false);
            TestAssert.That(n.Kind == OpsNoticeKind.Execute && n.Text.Contains("EXECUTE T-60"), "an own EXECUTE: " + n.Text);
            s.Pings.Add(new OpsPingRow { Kind = OpKind.Asat, Phase = OpPingPhase.Half, Seq = 6, Until = 600f, Name = "RED" });
            TestAssert.That(t.Observe(true, s, 502f, false).Kind == OpsNoticeKind.None, "one notice every 3 s");
            OpsNotice p = t.Observe(true, s, 505f, false);
            TestAssert.That(p.Kind == OpsNoticeKind.None, "the ping was consumed by the gap (notices are edges, not a queue)");
            s.Pings.Add(new OpsPingRow { Kind = OpKind.Asat, Phase = OpPingPhase.Half, Seq = 7, Until = 600f, Name = "RED" });
            OpsNotice q = t.Observe(true, s, 509f, false);
            Eq(q.Text, "RED IS DECRYPTING YOUR SATELLITE TRACK", "the enemy ping words");
            s.Pings.Add(new OpsPingRow { Kind = OpKind.Fob, Phase = OpPingPhase.Execute, Seq = 8, Until = 600f, Name = "RED" });
            TestAssert.That(t.Observe(true, s, 520f, true).Kind == OpsNoticeKind.None, "QUIET silences notices");
            Eq(t.Observe(false, s, 521f, false).Kind, OpsNoticeKind.None, "no mirror, no notice");
            TestAssert.That(t.Observe(true, s, 600f, false).Kind == OpsNoticeKind.None, "and the first sight after a reset is silent again");
        }

        private static void Words()
        {
            var r = new OpsRow { Domain = OpDomain.Cyber, Kind = OpKind.Asat, State = OpState.Funding, Percent = 40, Goal = 940, TargetId = 1 };
            Eq(OpsPageWords.Line(r, 0f), "ASAT · FUNDING · 40 % OF 940 CR", "funding line");
            r.Paused = true;
            TestAssert.That(OpsPageWords.Line(r, 0f).EndsWith("PAUSED: DATA CENTER DOWN"), "paused line");
            r.State = OpState.Execute; r.EndsAt = 140f;
            Eq(OpsPageWords.Line(r, 100f), "ASAT · EXECUTE T-40 · PROTECT THE DATA CENTER", "countdown line");
            r.State = OpState.Done; r.EndsAt = 190f;
            Eq(OpsPageWords.Line(r, 100f), "ASAT · DONE · IN FLIGHT 1:30", "in-flight line");
            Eq(OpsPageWords.Line(default, 0f), "NO OPERATION RUNNING", "idle line");
            var d = new OpsRow { Domain = OpDomain.Cyber, Kind = OpKind.ZeroDay, State = OpState.Funding, Percent = 40, WorkPercent = 12, Goal = 540, MyCr = 75 };
            Eq(OpsPageWords.Detail(d, 0f), "40 % OF 540 CR · YOURS 75 CR · WORK 12 %", "funding detail");
            d.Paused = true;
            TestAssert.That(OpsPageWords.Detail(d, 0f).EndsWith("PAUSED: DATA CENTER DOWN"), "paused detail");
            d.State = OpState.Broken; d.Paused = false;
            TestAssert.That(OpsPageWords.Detail(d, 0f).EndsWith("BROKEN, FUND TO RESUME"), "broken detail");
            d.Kind = OpKind.Asat; d.State = OpState.Execute;
            Eq(OpsPageWords.Detail(d, 0f), "PROTECT THE DATA CENTER AND THE LAUNCHER", "countdown detail");
            d.State = OpState.Done; d.EndsAt = 75f;
            Eq(OpsPageWords.Detail(d, 15f), "ASCENT 1:00", "ascent detail");
            d.Kind = OpKind.Fob; d.EndsAt = 1215f;
            Eq(OpsPageWords.Detail(d, 15f), "FOB UP 20:00", "FOB detail");
            Eq(OpsPageWords.Detail(default, 0f), "", "idle detail is empty");
            TestAssert.That(OpsWords.Hint(OpKind.Fob).Contains("20 MIN") && OpsWords.Hint(OpKind.None) == "" && OpsWords.NeedTarget(OpKind.ZeroDay).Contains("SAM"), "hints");
            var s = new OpsStateData { Protocol = P, Active = true, BirdsDown = 0b011 };
            s.BirdPercent[0] = 30; s.BirdPercent[1] = 80;
            Eq(OpsPageWords.Birds(s), "SAT OPTICAL LOST 30 % · RADAR LOST 80 %", "bird line");
            Eq(OpsPageWords.Birds(new OpsStateData { Active = true }), "", "no dead bird, no line");
            Eq(OpsWords.TargetWord(OpKind.Asat, 2), "KINETIC", "bird target word");
            Eq(OpsWords.TargetWord(OpKind.Fob, 3), "HELD H3", "held target word");
        }
    }
}
