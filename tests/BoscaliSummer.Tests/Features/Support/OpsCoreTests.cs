using System;
using System.Collections.Generic;
using BoscaliSummer.Modules.Support.Domain.Ops;

namespace BoscaliSummer.Tests.Features.Support
{
    /// <summary>M6a OPERATIONS pure engine: goals by profile, FUND taps and caps, work, countdown, BROKEN, counter-trace, stall, pings, cooldowns (spec 3, core 7a).</summary>
    internal static class OpsCoreTests
    {
        private const ulong Owner = 7, Other = 8;

        public static void Run()
        {
            CheckGoals();
            CheckFundAndCaps();
            CheckWork();
            CheckCountdownAndFire();
            CheckBroken();
            CheckCounterTrace();
            CheckStallAndCancel();
            CheckReviewFixes();
            CheckPingsAndWords();
            CheckLifecycle();
        }

        private sealed class Ports : IOpsPorts
        {
            public float Now { get; set; } = 100f;
            public int Humans { get; set; } = 2;
            public int Owner => 1;
            public int Wallet = 10000;
            public bool CyberUp = true, LauncherUp = true, BuildingUp = true;
            public readonly List<int> Refunded = new List<int>();
            private int pingSeq;
            public int NextPingSeq() => ++pingSeq;
            public readonly HashSet<ulong> Gone = new HashSet<ulong>();
            public bool InFaction(ulong op) => !Gone.Contains(op);
            public OpOutcome TrySpend(ulong op, int cr, out int detail)
            {
                detail = 0;
                if (Wallet < cr) { detail = cr; return OpOutcome.LowCredit; }
                Wallet -= cr; return OpOutcome.None;
            }
            public void Refund(ulong op, int cr) { Wallet += cr; Refunded.Add(cr); }
            public bool AnchorUp(OpKind kind, bool executing) => kind == OpKind.Fob ? BuildingUp : CyberUp && (kind != OpKind.Asat || !executing || LauncherUp);
        }

        private static OpsDesk Make(Ports ports, List<OpEvent> log = null)
        {
            var desk = new OpsDesk(ports);
            if (log != null) desk.Happened += e => log.Add(e);
            return desk;
        }

        private static readonly OpTarget Bird = new OpTarget(1, 0f, 0f);

        private static void CheckGoals()
        {
            TestAssert.Eq(OpsRules.Goal(OpKind.Asat, 1), 900, "solo ASAT goal is 0.6 x 1500");
            TestAssert.Eq(OpsRules.Goal(OpKind.ZeroDay, 1), 540, "solo ZERO-DAY goal is 0.6 x 900");
            TestAssert.Eq(OpsRules.Goal(OpKind.Fob, 1), 600, "solo FOB goal is 0.6 x 1000");
            TestAssert.Eq(OpsRules.Goal(OpKind.Asat, 2), 940, "two humans: 1500 x (0.5 + 2/16) = 937.5 rounds to 940");
            TestAssert.Eq(OpsRules.Goal(OpKind.Asat, 8), 1500, "eight humans: the base goal");
            TestAssert.Eq(OpsRules.Goal(OpKind.Asat, 16), 2250, "sixteen humans: 1.5 x base");
            TestAssert.Eq(OpsRules.Goal(OpKind.Asat, 30), 2250, "thirty humans stay at 1.5 x base");
            TestAssert.Eq(OpsRules.Goal(OpKind.None, 4), 0, "no operation has no goal");
            TestAssert.Eq(OpsRules.DomainOf(OpKind.Asat), OpDomain.Cyber, "ASAT is CYBER");
            TestAssert.Eq(OpsRules.DomainOf(OpKind.ZeroDay), OpDomain.Cyber, "ZERO-DAY is CYBER");
            TestAssert.Eq(OpsRules.DomainOf(OpKind.Fob), OpDomain.Sof, "FOB is SOF");
            TestAssert.Eq(OpsRules.ZeroDayEffectSeconds(false), 180f, "SAM NET FAIL lasts three minutes");
            TestAssert.Eq(OpsRules.ZeroDayEffectSeconds(true), 90f, "an EW truck in 18 km halves it");
        }

        private static void CheckFundAndCaps()
        {
            var p = new Ports { Humans = 1 };
            var desk = Make(p);
            TestAssert.Eq(desk.Fund(Owner, OpDomain.Cyber, 25).Outcome, OpOutcome.NoOperation, "no operation, nothing to fund");
            TestAssert.Eq(desk.Plan(Owner, OpKind.None, Bird).Outcome, OpOutcome.NoOperation, "an unknown kind is refused");
            OpResult start = desk.Plan(Owner, OpKind.Asat, Bird);
            TestAssert.Eq(start.Outcome, OpOutcome.Started, "a solo player starts instantly");
            TestAssert.Eq(desk.Slot(OpDomain.Cyber).Goal, 900, "the profile is locked at creation");
            p.Humans = 5; // joining later never changes the locked goal
            TestAssert.Eq(desk.Slot(OpDomain.Cyber).Goal, 900, "the locked goal stays");
            TestAssert.Eq(desk.Fund(Owner, OpDomain.Cyber, 30).Outcome, OpOutcome.BadAmount, "only 25 and 50 are taps");
            TestAssert.Eq(desk.Fund(Owner, OpDomain.Cyber, 25).Outcome, OpOutcome.Funded, "a 25 CR tap");
            TestAssert.Eq(p.Wallet, 10000 - 25, "the CR left the wallet");
            TestAssert.Eq(desk.Slot(OpDomain.Cyber).Mine(Owner), 25f, "the ledger remembers the tap");
            // Five humans: a player may put in at most 30 % of the goal (270 CR of 900).
            for (int i = 0; i < 4; i++) TestAssert.Eq(desk.Fund(Owner, OpDomain.Cyber, 50).Outcome, OpOutcome.Funded, "tap " + i);
            TestAssert.Eq(desk.Slot(OpDomain.Cyber).Mine(Owner), 225f, "225 CR in");
            OpResult last = desk.Fund(Owner, OpDomain.Cyber, 50);
            TestAssert.Eq(last.Outcome, OpOutcome.Funded, "the tap that reaches the cap is trimmed, not refused");
            TestAssert.Eq(last.Charged, 45, "only the 45 CR of room is taken");
            TestAssert.Eq(desk.Fund(Owner, OpDomain.Cyber, 25).Outcome, OpOutcome.PlayerCap, "the cap is a refusal after it");
            TestAssert.Eq(desk.Fund(Other, OpDomain.Cyber, 50).Outcome, OpOutcome.Funded, "another member can fund");
            p.Wallet = 10;
            OpResult poor = desk.Fund(Other, OpDomain.Cyber, 50);
            TestAssert.Eq(poor.Outcome, OpOutcome.LowCredit, "an empty wallet is refused");
            TestAssert.Eq(poor.Detail, 50, "with the need");
            TestAssert.Eq(desk.Slot(OpDomain.Cyber).Mine(Other), 50f, "a refused tap writes nothing");
            // Solo: no per-player cap.
            var solo = new Ports { Humans = 1 };
            var d2 = Make(solo);
            d2.Plan(Owner, OpKind.ZeroDay, new OpTarget(3, 1f, 1f, 2));
            for (int i = 0; i < 11; i++) d2.Fund(Owner, OpDomain.Cyber, 50);
            TestAssert.Eq(d2.Slot(OpDomain.Cyber).State, OpState.Execute, "a solo player may fund the whole bar (540 CR)");
            TestAssert.Eq(solo.Wallet, 10000 - 540, "and pays exactly the goal");
        }

        private static void CheckWork()
        {
            var p = new Ports { Humans = 1 };
            var desk = Make(p);
            TestAssert.Eq(desk.Work(OpDomain.Cyber, 8), 0f, "work with no operation adds nothing");
            desk.Plan(Owner, OpKind.ZeroDay, new OpTarget(3, 0f, 0f, 2)); // goal 540, work cap 270
            for (int i = 0; i < 40; i++) desk.Work(OpDomain.Cyber, OpsRules.HopWork);
            TestAssert.Eq(desk.Slot(OpDomain.Cyber).Work, 270f, "work is capped at half the goal");
            TestAssert.Eq(desk.Slot(OpDomain.Cyber).WorkPercent, 50, "shown as 50 %");
            p.CyberUp = false;
            TestAssert.Eq(desk.Work(OpDomain.Cyber, 8), 0f, "no work while the anchor is down");
            p.CyberUp = true;
            var f = new Ports { Humans = 1 };
            var fd = Make(f);
            fd.Plan(Owner, OpKind.Fob, new OpTarget(1, 5f, 5f));
            TestAssert.Near(fd.Work(OpDomain.Sof, OpsRules.SofWork), 40f, "a SOF mission success is +40");
            TestAssert.Eq(fd.Slot(OpDomain.Sof).Percent, 6, "40 of 600 reads 6 %");
        }

        private static void FillToExecute(OpsDesk desk, OpDomain d)
        {
            for (int i = 0; i < 100 && desk.Slot(d).State != OpState.Execute; i++) desk.Fund(Owner, d, 50);
        }

        private static void CheckCountdownAndFire()
        {
            var p = new Ports { Humans = 1 };
            var log = new List<OpEvent>();
            OpsDesk desk = Make(p, log);
            desk.Plan(Owner, OpKind.Asat, Bird);
            FillToExecute(desk, OpDomain.Cyber);
            OpSlot s = desk.Slot(OpDomain.Cyber);
            TestAssert.Eq(s.State, OpState.Execute, "a full bar starts the countdown");
            TestAssert.Near(s.CountdownEnds, 160f, "60 s countdown");
            TestAssert.Eq(desk.Fund(Owner, OpDomain.Cyber, 25).Outcome, OpOutcome.Executing, "no funding during the countdown");
            TestAssert.Eq(desk.Plan(Owner, OpKind.ZeroDay, Bird).Outcome, OpOutcome.Executing, "no planning during the countdown");
            TestAssert.Eq(desk.Cancel(Owner, OpDomain.Cyber).Outcome, OpOutcome.Executing, "no cancelling during the countdown");
            TestAssert.Eq(desk.Pings.Count, 2, "an enemy ping at 50 % and one at the countdown");
            TestAssert.Eq(log.Exists(e => e.Kind == OpEventKind.Half), true, "the 50 % event");
            TestAssert.Eq(log.Exists(e => e.Kind == OpEventKind.Execute), true, "the countdown event");
            p.Now = 159f; desk.Tick();
            TestAssert.Eq(s.State, OpState.Execute, "still counting at T-1");
            p.Now = 160.5f; desk.Tick();
            TestAssert.Eq(s.State, OpState.Done, "the countdown ends in DONE");
            TestAssert.Eq(log.Exists(e => e.Kind == OpEventKind.Fired && e.Op == OpKind.Asat && e.Target.Id == 1), true, "FIRED carries the kind and the target");
            TestAssert.Eq(desk.Plan(Owner, OpKind.Asat, Bird).Outcome, OpOutcome.Cooldown, "the type cooldown is eight minutes");
            TestAssert.Eq(desk.Plan(Owner, OpKind.ZeroDay, new OpTarget(2, 0f, 0f, 2)).Outcome, OpOutcome.Started, "another CYBER operation may start after DONE");
        }

        private static void CheckBroken()
        {
            var p = new Ports { Humans = 2 };
            var log = new List<OpEvent>();
            OpsDesk desk = Make(p, log);
            desk.Plan(Owner, OpKind.Asat, Bird); // goal 940
            for (int i = 0; i < 40 && desk.Slot(OpDomain.Cyber).State != OpState.Execute; i++)
            {
                desk.Fund(Owner, OpDomain.Cyber, 50);
                desk.Fund(Other, OpDomain.Cyber, 50);
                desk.Fund(9, OpDomain.Cyber, 50);
                desk.Fund(10, OpDomain.Cyber, 50);
            }
            OpSlot s = desk.Slot(OpDomain.Cyber);
            TestAssert.Eq(s.State, OpState.Execute, "full bar");
            float fundedBefore = s.Funded;
            int walletBefore = p.Wallet;
            p.Now += 20f; p.CyberUp = false; desk.Tick();
            TestAssert.Eq(s.State, OpState.Broken, "the anchor died in the countdown: BROKEN");
            TestAssert.Eq(log.Exists(e => e.Kind == OpEventKind.Broken), true, "the broken event");
            TestAssert.Near(s.Value, 470f, "the bar restarts at 50 % of 940");
            TestAssert.Near(s.Funded, fundedBefore / 2f, "half the CR stays in");
            TestAssert.That(p.Wallet - walletBefore >= (int)(fundedBefore / 2f) - 4, "half the CR went back to the members");
            TestAssert.Eq(desk.Pings.Count, 1, "the countdown ping is withdrawn");
            TestAssert.Eq(desk.Fund(Owner, OpDomain.Cyber, 25).Outcome, OpOutcome.AnchorDown, "no funding while the anchor is down");
            p.CyberUp = true;
            TestAssert.Eq(desk.Fund(Owner, OpDomain.Cyber, 25).Outcome, OpOutcome.Funded, "FUND resumes a broken operation");
            TestAssert.Eq(s.State, OpState.Funding, "back to FUNDING");
        }

        private static void CheckCounterTrace()
        {
            var p = new Ports { Humans = 1 };
            OpsDesk desk = Make(p);
            TestAssert.Eq(desk.CounterTrace(OpDomain.Cyber), false, "nothing to trace");
            desk.Plan(Owner, OpKind.Asat, Bird); // goal 900
            for (int i = 0; i < 6; i++) desk.Fund(Owner, OpDomain.Cyber, 50); // 300
            OpSlot s = desk.Slot(OpDomain.Cyber);
            TestAssert.Eq(desk.CounterTrace(OpDomain.Cyber), true, "an enemy hop on the data center");
            TestAssert.Near(s.Value, 210f, "-10 % of the goal (90)");
            TestAssert.Near(s.Mine(Owner), 210f, "the ledger shrinks with the bar");
            FillToExecute(desk, OpDomain.Cyber);
            TestAssert.Eq(s.State, OpState.Execute, "full again");
            TestAssert.Eq(desk.CounterTrace(OpDomain.Cyber), true, "a counter-trace in the countdown");
            TestAssert.Eq(s.State, OpState.Funding, "a bar below full aborts the countdown");
            TestAssert.Eq(desk.Pings.Exists(x => x.Phase == OpPingPhase.Execute), false, "and withdraws the countdown ping");
        }

        private static void CheckStallAndCancel()
        {
            var p = new Ports { Humans = 1 };
            var log = new List<OpEvent>();
            OpsDesk desk = Make(p, log);
            desk.Plan(Owner, OpKind.Fob, new OpTarget(1, 0f, 0f));
            desk.Fund(Owner, OpDomain.Sof, 50);
            p.Now += 599f; desk.Tick();
            TestAssert.Eq(desk.Slot(OpDomain.Sof).State, OpState.Funding, "no stall before ten minutes");
            p.Now += 2f; desk.Tick();
            TestAssert.Eq(desk.Slot(OpDomain.Sof).State, OpState.NeedsFunding, "ten minutes without input: NEEDS FUNDING");
            TestAssert.Eq(log.Exists(e => e.Kind == OpEventKind.Stalled), true, "the stall event");
            TestAssert.Eq(desk.Fund(Owner, OpDomain.Sof, 25).Outcome, OpOutcome.Funded, "a tap resumes it");
            TestAssert.Eq(desk.Slot(OpDomain.Sof).State, OpState.Funding, "back to FUNDING");
            TestAssert.Eq(log.Exists(e => e.Kind == OpEventKind.Resumed), true, "the resume event");
            int wallet = p.Wallet;
            TestAssert.Eq(desk.Cancel(Other, OpDomain.Sof).Outcome, OpOutcome.NotOwner, "only the owner cancels");
            TestAssert.Eq(desk.Cancel(Owner, OpDomain.Sof).Outcome, OpOutcome.Cancelled, "the owner cancels");
            TestAssert.Eq(p.Wallet, wallet + 75, "everything is refunded");
            TestAssert.Eq(desk.Slot(OpDomain.Sof).State, OpState.Idle, "the slot is free");
            TestAssert.Eq(desk.Plan(Owner, OpKind.Fob, new OpTarget(1, 0f, 0f)).Outcome, OpOutcome.Started, "a cancelled FOB has no cooldown");
        }

        private static void CheckReviewFixes()
        {
            // BROKEN then CANCEL: refunds plus what is left in the ledger equal what was paid in (the ledger must not be halved twice).
            var p = new Ports { Humans = 2 };
            OpsDesk desk = Make(p);
            desk.Plan(Owner, OpKind.Asat, Bird);
            int paid = 0, start = p.Wallet;
            for (int i = 0; i < 40 && desk.Slot(OpDomain.Cyber).State != OpState.Execute; i++)
                foreach (ulong who in new[] { Owner, Other, 9UL, 10UL }) if (desk.Fund(who, OpDomain.Cyber, 50).Ok) { }
            OpSlot s = desk.Slot(OpDomain.Cyber);
            TestAssert.Eq(s.State, OpState.Execute, "full bar");
            paid = start - p.Wallet;
            p.Now += 10f; p.CyberUp = false; desk.Tick();
            TestAssert.Eq(s.State, OpState.Broken, "BROKEN");
            float left = 0f; foreach (var kv in s.Ledger) left += kv.Value;
            TestAssert.Near(left, paid - (p.Wallet - (start - paid)), "the ledger keeps exactly what was not refunded");
            TestAssert.Eq(desk.Cancel(Owner, OpDomain.Cyber).Outcome, OpOutcome.Cancelled, "cancel after BROKEN");
            TestAssert.That(start - p.Wallet <= 4, "BROKEN then CANCEL returns everything paid in (within the floor of each member's half)");

            // The owner left the faction: any member may cancel and retarget.
            p = new Ports { Humans = 2 };
            desk = Make(p);
            desk.Plan(Owner, OpKind.Fob, new OpTarget(1, 0f, 0f));
            desk.Fund(Other, OpDomain.Sof, 50);
            TestAssert.Eq(desk.Cancel(Other, OpDomain.Sof).Outcome, OpOutcome.NotOwner, "a member cannot cancel while the owner is present");
            p.Gone.Add(Owner);
            TestAssert.Eq(desk.Plan(Other, OpKind.Fob, new OpTarget(2, 0f, 0f)).Outcome, OpOutcome.Retargeted, "a member retargets when the owner is gone");
            p.Gone.Add(Other);
            TestAssert.Eq(desk.Cancel(9, OpDomain.Sof).Outcome, OpOutcome.Cancelled, "a member cancels when the owner is gone");
            TestAssert.Eq(p.Wallet, 10000, "the ledger came back in full");

            // A FOB whose held building stays lost for more than 120 s cancels itself with a full refund.
            p = new Ports { Humans = 1 };
            var log = new List<OpEvent>();
            desk = Make(p, log);
            desk.Plan(Owner, OpKind.Fob, new OpTarget(1, 0f, 0f));
            desk.Fund(Owner, OpDomain.Sof, 50); desk.Fund(Owner, OpDomain.Sof, 25);
            p.BuildingUp = false; desk.Tick();
            p.Now += 100f; desk.Tick();
            TestAssert.Eq(desk.Slot(OpDomain.Sof).State, OpState.Funding, "still waiting inside the grace");
            p.BuildingUp = true; desk.Tick();
            p.BuildingUp = false; p.Now += 50f; desk.Tick();
            p.Now += 100f; desk.Tick();
            TestAssert.Eq(desk.Slot(OpDomain.Sof).State, OpState.Funding, "a restored anchor restarts the grace");
            p.Now += 30f; desk.Tick();
            TestAssert.Eq(desk.Slot(OpDomain.Sof).State, OpState.Idle, "the lost FOB is given up after 120 s");
            TestAssert.Eq(p.Wallet, 10000, "full refund");
            TestAssert.Eq(log.Exists(e => e.Kind == OpEventKind.Cancelled), true, "the cancel event");

            // Withdraw (OPERATIONS switched off) refunds every ledger; Fail refunds a Done operation and lifts the cooldown; EndEffect stops a Done FOB showing.
            p = new Ports { Humans = 1 };
            desk = Make(p);
            desk.Plan(Owner, OpKind.ZeroDay, new OpTarget(3, 0f, 0f, 2)); desk.Fund(Owner, OpDomain.Cyber, 50);
            desk.Plan(Owner, OpKind.Fob, new OpTarget(1, 0f, 0f)); desk.Fund(Owner, OpDomain.Sof, 25);
            desk.Withdraw();
            TestAssert.Eq(p.Wallet, 10000, "disabling refunds every ledger");
            TestAssert.Eq(desk.Slot(OpDomain.Cyber).Kind, OpKind.None, "slots are empty after the withdraw");
            desk.Plan(Owner, OpKind.ZeroDay, new OpTarget(3, 0f, 0f, 2));
            for (int i = 0; i < 12; i++) desk.Fund(Owner, OpDomain.Cyber, 50);
            TestAssert.Eq(desk.Slot(OpDomain.Cyber).State, OpState.Execute, "ZERO-DAY full");
            p.Now += 61f; desk.Tick();
            TestAssert.Eq(desk.Slot(OpDomain.Cyber).State, OpState.Done, "ZERO-DAY fired");
            desk.Fail(OpDomain.Cyber);
            TestAssert.Eq(p.Wallet, 10000, "a failed effect refunds the members");
            TestAssert.Eq(desk.Plan(Owner, OpKind.ZeroDay, new OpTarget(3, 0f, 0f, 2)).Outcome, OpOutcome.Started, "and lifts the cooldown");
            desk.Cancel(Owner, OpDomain.Cyber);
            desk.Plan(Owner, OpKind.Fob, new OpTarget(1, 0f, 0f));
            for (int i = 0; i < 12; i++) desk.Fund(Owner, OpDomain.Sof, 50);
            p.Now += 61f; desk.Tick();
            desk.SetEffectEnd(OpDomain.Sof, p.Now + 1000f);
            desk.EndEffect(OpDomain.Sof);
            p.Now += 50f; desk.Tick();
            TestAssert.Eq(desk.Slot(OpDomain.Sof).Kind, OpKind.None, "a FOB whose effect ended early leaves the page");
            // Pings carry the victim.
            desk.Plan(Owner, OpKind.ZeroDay, new OpTarget(3, 0f, 0f, 2));
            for (int i = 0; i < 6; i++) desk.Fund(Owner, OpDomain.Cyber, 50);
            TestAssert.Eq(desk.Pings.Count > 0 && desk.Pings[0].Victim == 2, true, "a ping names its victim faction");
        }

        private static void CheckPingsAndWords()
        {
            TestAssert.Eq(OpsWords.Ping(OpKind.Asat, OpPingPhase.Half, "Red"), "RED IS DECRYPTING YOUR SATELLITE TRACK", "the spec ping words");
            TestAssert.Eq(OpsWords.Ping(OpKind.Asat, OpPingPhase.Launch, ""), "ASAT LAUNCH DETECTED", "the launch warning");
            TestAssert.Eq(OpsWords.Ping(OpKind.Asat, OpPingPhase.Loss, "Red", 1), "SAT LOST: RADAR", "the loss names the bird");
            TestAssert.Eq(OpsWords.State(OpState.Execute, 41.2f), "EXECUTE T-42", "the countdown word");
            TestAssert.Eq(OpsWords.State(OpState.NeedsFunding, 0f), "NEEDS FUNDING", "the stall word");
            for (byte b = 0; b <= OpsWords.MaxOutcome; b++)
            {
                string words = OpsWords.Of((OpOutcome)b, 5);
                TestAssert.That(b == 0 || words.Length > 8, "every outcome has words: " + b);
                TestAssert.That(b < (byte)OpOutcome.NoTarget || words.StartsWith("NEGATIVE"), "a refusal speaks NEGATIVE: " + b);
            }
            var p = new Ports { Humans = 1 };
            OpsDesk desk = Make(p);
            desk.Plan(Owner, OpKind.ZeroDay, new OpTarget(3, 0f, 0f, 2));
            for (int i = 0; i < 6; i++) desk.Fund(Owner, OpDomain.Cyber, 50);
            TestAssert.Eq(desk.Pings.Count, 1, "the 50 % ping");
            TestAssert.Eq(desk.Pings[0].Phase, OpPingPhase.Half, "is the half ping");
            desk.AddPing(OpKind.Asat, OpPingPhase.Loss, 45f);
            TestAssert.Eq(desk.Pings.Count, 2, "the runtime may add a loss ping");
            p.Now += 100f; desk.Tick();
            TestAssert.Eq(desk.Pings.Count, 0, "pings expire");
        }

        private static void CheckLifecycle()
        {
            var p = new Ports { Humans = 1 };
            var log = new List<OpEvent>();
            OpsDesk desk = Make(p, log);
            TestAssert.Eq(desk.Plan(Owner, OpKind.Fob, new OpTarget(1, 0f, 0f)).Outcome, OpOutcome.Started, "FOB starts");
            TestAssert.Eq(desk.Plan(Owner, OpKind.Fob, new OpTarget(2, 3f, 3f)).Outcome, OpOutcome.Retargeted, "the owner retargets");
            TestAssert.Eq(desk.Slot(OpDomain.Sof).Target.Id, 2, "the new target");
            TestAssert.Eq(desk.Plan(Other, OpKind.Fob, new OpTarget(1, 0f, 0f)).Outcome, OpOutcome.NotOwner, "another member cannot");
            // The FOB anchor (the held building) lost while funding pauses the bar.
            p.BuildingUp = false; desk.Tick();
            TestAssert.Eq(desk.Slot(OpDomain.Sof).Paused, true, "a lost building pauses the bar");
            TestAssert.Eq(desk.Fund(Owner, OpDomain.Sof, 25).Outcome, OpOutcome.AnchorDown, "no funding without the building");
            p.BuildingUp = true; desk.Tick();
            FillToExecute(desk, OpDomain.Sof);
            p.Now += 61f; desk.Tick();
            TestAssert.Eq(desk.Slot(OpDomain.Sof).State, OpState.Done, "the FOB fires");
            desk.SetEffectEnd(OpDomain.Sof, p.Now + 600f);
            p.Now += 100f; desk.Tick();
            TestAssert.Eq(desk.Slot(OpDomain.Sof).State, OpState.Done, "DONE stays while the effect runs");
            p.Now += 600f; desk.Tick();
            TestAssert.Eq(desk.Slot(OpDomain.Sof).State, OpState.Idle, "and closes when it ends");
            TestAssert.Eq(log.Exists(e => e.Kind == OpEventKind.Done), true, "the done event");
            TestAssert.Eq(desk.Plan(Owner, OpKind.Fob, new OpTarget(2, 3f, 3f)).Outcome, OpOutcome.Started, "a FOB is renewable at once");
            desk.Reset();
            TestAssert.Eq(desk.Slot(OpDomain.Sof).State, OpState.Idle, "reset clears the desk");
            // Two domains run side by side; a CYBER operation never blocks the SOF one.
            TestAssert.Eq(desk.Plan(Owner, OpKind.ZeroDay, new OpTarget(1, 0f, 0f, 2)).Outcome, OpOutcome.Started, "CYBER and");
            TestAssert.Eq(desk.Plan(Owner, OpKind.Fob, new OpTarget(1, 0f, 0f)).Outcome, OpOutcome.Started, "SOF run together");
            TestAssert.Eq(desk.Plan(Owner, OpKind.Asat, Bird).Outcome, OpOutcome.Busy, "but one operation per domain");
        }
    }

    internal static class OpsListExtensions
    {
        public static bool Exists(this IReadOnlyList<OpPing> list, Predicate<OpPing> match)
        {
            for (int i = 0; i < list.Count; i++) if (match(list[i])) return true;
            return false;
        }
    }
}
