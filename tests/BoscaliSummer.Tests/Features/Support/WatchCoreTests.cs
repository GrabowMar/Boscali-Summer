using System;
using System.Collections.Generic;
using BoscaliSummer.Modules.Support.Domain.Cyber;
using BoscaliSummer.Modules.Support.Domain.Ops;
using BoscaliSummer.Modules.Support.Domain.Sof;

namespace BoscaliSummer.Tests.Features.Support
{
    /// <summary>M6b shared OVERLORD core: reason words, the idle rule, the pacer, the log ring and the AI faction's operation doctrine (spec section 4, core 7a).</summary>
    internal static class WatchCoreTests
    {
        public static void Run()
        {
            CheckWords();
            CheckIdle();
            CheckPacer();
            CheckRing();
            CheckLead();
            CheckSeed();
            CheckDoctrine();
            CheckBrain();
        }

        private static void CheckWords()
        {
            TestAssert.Eq(WatchWords.Reason(WatchCode.CyberHop, (int)NodeKind.SamC2, 0), "HOP SAM C2 — MOST VALUABLE REACHABLE NODE", "hop words");
            TestAssert.Eq(WatchWords.Reason(WatchCode.CyberBurnTrace, (int)NodeKind.Radar, 72), "BURN RADAR — TRACE 72 %", "burn words");
            TestAssert.Eq(WatchWords.Reason(WatchCode.CyberDropTrace, 0, 91), "DROP — TRACE 91 %, LEAVING BEFORE TRACED", "drop words");
            TestAssert.Eq(WatchWords.Reason(WatchCode.SofSabotage, ((int)AnchorSub.EwTruck << 2) | 1, 65), "SABOTAGE EW TRUCK — B-1, ODDS 65 %", "sabotage words pack the anchor and the team");
            TestAssert.Eq(WatchWords.Reason(WatchCode.SofLase, 0, 7), "LASE — A-1 ON A HIGH-VALUE CONTACT 7 KM FROM THE FRONT", "lase words");
            TestAssert.Eq(WatchWords.Reason(WatchCode.OpPlan, (int)OpKind.ZeroDay, 63), "PLAN ZERO-DAY — LEADING 63 % OF OBJECTIVES", "plan words");
            TestAssert.Eq(WatchWords.Reason(WatchCode.OpFund, (int)OpKind.Asat, 64), "FUND ASAT — LEADING, TREASURY ABOUT 640 CR", "fund words");
            for (byte c = 1; c <= (byte)WatchCode.OpFund; c++) TestAssert.That(WatchWords.Reason((WatchCode)c, 1, 1).Length > 0, "code " + c + " has words");
            TestAssert.Eq(WatchWords.Reason(WatchCode.None, 0, 0), "", "no code, no words");
            TestAssert.Eq(WatchWords.Line(new WatchLogRow { Seq = 1, Domain = WatchDomain.Cyber, Code = WatchCode.CyberDropYield }), "OVERLORD · DROP — OPERATOR TAKING OVER", "console line");
            TestAssert.Eq(WatchWords.Domain(WatchDomain.Sof), "SOF", "domain slab");
        }

        private static void CheckIdle()
        {
            var idle = new WatchIdle();
            TestAssert.That(idle.Idle(1, 10f) && idle.QuietOfHumans(1, 10f), "nobody has worked: idle");
            idle.RecordHuman(100f);
            TestAssert.That(idle.Idle(1, 101f) && idle.Idle(2, 101f), "OPS FRONTS S2: the director is always on, whatever a human just did");
            TestAssert.That(!idle.QuietOfHumans(1, 159f) && idle.QuietOfHumans(1, 160f), "the old rule, solo: 60 s");
            TestAssert.That(!idle.QuietOfHumans(2, 399f) && idle.QuietOfHumans(2, 400f), "two or more: 300 s");
            TestAssert.That(idle.QuietOfHumans(0, 101f), "a faction with no humans is always quiet");
            idle.RecordHuman(-5f);
            idle.RecordHuman(float.NaN);
            TestAssert.That(!idle.QuietOfHumans(1, 120f), "an invalid clock changes nothing");
            idle.Reset();
            TestAssert.That(idle.QuietOfHumans(1, 120f), "reset forgets the human");
        }

        private static void CheckPacer()
        {
            var p = new WatchPacer();
            TestAssert.That(p.CanAct(WatchDomain.Cyber, false, 100f) && p.CanAct(WatchDomain.Sof, false, 100f), "a fresh pacer lets both domains act");
            p.NoteAct(WatchDomain.Cyber, false, 100f);
            TestAssert.That(!p.CanAct(WatchDomain.Cyber, false, 109.9f) && p.CanAct(WatchDomain.Cyber, false, 110f), "10 s per domain");
            TestAssert.That(p.CanAct(WatchDomain.Sof, false, 101f), "the other domain is independent");
            TestAssert.Near(p.WaitSeconds(WatchDomain.Cyber, false, 104f), 6f, "wait is the rest of the gap");
            TestAssert.Near(p.WaitSeconds(WatchDomain.Cyber, false, 200f), 0f, "no wait once the gap is over");

            var ai = new WatchPacer();
            ai.NoteAct(WatchDomain.Cyber, true, 100f);
            TestAssert.That(!ai.CanAct(WatchDomain.Sof, true, 129f) && ai.CanAct(WatchDomain.Sof, true, 130f), "an AI faction shares 30 s across both domains");
            TestAssert.Near(WatchPacer.Gap(true), 30f, "AI gap");
            TestAssert.Near(WatchPacer.Gap(false), 10f, "domain gap");
            TestAssert.That(!ai.CanAct(WatchDomain.Ops, true, 500f), "the operation desk is not a domain of the action limiter");

            // Urgency: while one domain cannot wait, the other may not take an AI faction's shared limiter; two urgent domains do not block each other; a faction with humans is unaffected.
            var u = new WatchPacer();
            u.SetUrgent(WatchDomain.Cyber, true);
            TestAssert.That(!u.CanAct(WatchDomain.Sof, true, 500f) && u.CanAct(WatchDomain.Cyber, true, 500f), "an urgent CYBER keeps SOF off the AI limiter");
            TestAssert.That(u.CanAct(WatchDomain.Sof, false, 500f), "a faction with humans has a limiter per domain: no effect");
            u.SetUrgent(WatchDomain.Sof, true);
            TestAssert.That(u.CanAct(WatchDomain.Sof, true, 500f) && u.CanAct(WatchDomain.Cyber, true, 500f), "both urgent: neither is held back");
            u.SetUrgent(WatchDomain.Cyber, false); u.SetUrgent(WatchDomain.Sof, false);
            TestAssert.That(u.CanAct(WatchDomain.Sof, true, 500f), "urgency released");
            u.SetUrgent(WatchDomain.Ops, true);
            TestAssert.That(u.CanAct(WatchDomain.Sof, true, 500f), "the operation desk has no urgency");

            var f = new WatchPacer();
            TestAssert.That(f.CanFund(100f), "first tap");
            f.NoteFund(100f);
            TestAssert.That(!f.CanFund(159f) && f.CanFund(160f), "one tap or plan per 60 s");
            f.Reset();
            TestAssert.That(f.CanFund(101f), "reset");
        }

        private static void CheckRing()
        {
            var ring = new WatchLogRing();
            for (int i = 1; i <= 5; i++) ring.Add(WatchDomain.Cyber, WatchCode.CyberHop, i, 0);
            var rows = new List<WatchLogRow>();
            TestAssert.Eq(ring.CopyTo(rows), 3, "three rows kept");
            TestAssert.Eq(rows[0].Seq, 3, "oldest kept is 3");
            TestAssert.Eq(rows[2].Seq, 5, "newest is 5");
            ring.Add(WatchDomain.Ops, WatchCode.OpFund, 999, -4);
            rows.Clear(); ring.CopyTo(rows);
            TestAssert.Eq(rows[2].A, (byte)255, "arguments clamp to a byte");
            TestAssert.Eq(rows[2].B, (byte)0, "negative clamps to zero");
            ring.Clear();
            rows.Clear();
            TestAssert.Eq(ring.CopyTo(rows), 0, "cleared");
        }

        private static void CheckLead()
        {
            TestAssert.That(AiRules.Leads(0.6f, 0.4f), "60 % against 40 % leads");
            TestAssert.That(!AiRules.Leads(0.5f, 0.5f), "an even split leads nobody");
            TestAssert.That(!AiRules.Leads(0.45f, 0.3f), "under half does not lead even against a weaker rival");
            TestAssert.That(!AiRules.Leads(0.55f, 0.6f), "a rival with more does not");
            TestAssert.That(AiRules.Leads(0.55f, float.NaN), "no rival known: the share decides");
            TestAssert.That(!AiRules.Leads(float.NaN, 0.1f), "no objectives to count: no lead, no funding");
            TestAssert.That(AiRules.CanFund(500.5f) && !AiRules.CanFund(500f) && !AiRules.CanFund(float.NaN), "the treasury must be over 500");
            TestAssert.That(AiRules.LargeTap(551f) && !AiRules.LargeTap(550f), "the 50 CR tap only while the treasury stays above the floor");
            TestAssert.Eq(AiRules.AsatClass(0), 1, "radar first");
            TestAssert.Eq(AiRules.AsatClass(1), 0, "then optical");
            TestAssert.Eq(AiRules.AsatClass(2), 2, "then kinetic");
            TestAssert.Eq(AiRules.AsatClass(3), 1, "and around");
        }

        private static AiOpsFacts Facts(float share = 0.6f, float rival = 0.4f, float treasury = 700f) => new AiOpsFacts
        {
            Share = share, RivalShare = rival, Treasury = treasury, CyberOnline = true, SofOnline = true, DataCenterUp = true,
            Cyber = new AiSlotView(OpKind.None, OpState.Idle), Sof = new AiSlotView(OpKind.None, OpState.Idle)
        };

        private static void CheckDoctrine()
        {
            AiOpsPlan p = AiRules.Decide(Facts(share: 0.4f, rival: 0.6f));
            TestAssert.Eq(p.Action, AiOpsAction.None, "losing: nothing");
            TestAssert.Eq(p.Why, AiOpsWhy.NotLeading, "reason");
            p = AiRules.Decide(Facts(treasury: 500f));
            TestAssert.Eq(p.Why, AiOpsWhy.Poor, "treasury at 500 is not over 500");

            // Plans use only what the faction's own desks show: a revealed SAM C2 makes ZERO-DAY, none makes ASAT, a held building makes a FOB.
            AiOpsFacts f = Facts();
            f.SamNodeId = 12;
            p = AiRules.Decide(f);
            TestAssert.Eq(p.Action, AiOpsAction.Plan, "plan"); TestAssert.Eq(p.Kind, OpKind.ZeroDay, "a revealed SAM C2: ZERO-DAY"); TestAssert.Eq(p.Target, 12, "on that node"); TestAssert.Eq(p.Domain, OpDomain.Cyber, "CYBER");
            TestAssert.That(p.Reason.StartsWith("PLAN ZERO-DAY — LEADING 60 %", StringComparison.Ordinal), p.Reason);
            f = Facts();
            p = AiRules.Decide(f);
            TestAssert.Eq(p.Kind, OpKind.Asat, "no SAM C2 revealed: ASAT"); TestAssert.Eq(p.Target, 1, "radar bird first");
            f.AsatsPlanned = 1;
            TestAssert.Eq(AiRules.Decide(f).Target, 0, "the next ASAT aims at the optical bird");
            f = Facts(); f.DataCenterUp = false; f.SamNodeId = 12;
            p = AiRules.Decide(f);
            TestAssert.Eq(p.Action, AiOpsAction.None, "no data center: no CYBER operation");
            f = Facts(); f.DataCenterUp = false; f.HeldBuildingId = 3;
            p = AiRules.Decide(f);
            TestAssert.Eq(p.Kind, OpKind.Fob, "a held building: FOB"); TestAssert.Eq(p.Target, 3, "on it"); TestAssert.Eq(p.Domain, OpDomain.Sof, "SOF");
            f = Facts(); f.CyberOnline = false; f.SofOnline = false;
            TestAssert.Eq(AiRules.Decide(f).Action, AiOpsAction.None, "no domains: nothing");

            // An open bar is funded; a counting-down or finished one is left alone.
            f = Facts(treasury: 800f);
            f.Cyber = new AiSlotView(OpKind.ZeroDay, OpState.Funding);
            p = AiRules.Decide(f);
            TestAssert.Eq(p.Action, AiOpsAction.Fund, "fund the open bar"); TestAssert.Eq(p.Large, true, "a 50 CR tap from 800"); TestAssert.Eq(p.Kind, OpKind.ZeroDay, "kind");
            TestAssert.That(p.Reason.StartsWith("FUND ZERO-DAY", StringComparison.Ordinal), p.Reason);
            f.Treasury = 540f;
            TestAssert.Eq(AiRules.Decide(f).Large, false, "the small tap near the floor");
            f.Cyber = new AiSlotView(OpKind.ZeroDay, OpState.Execute);
            TestAssert.Eq(AiRules.Decide(f).Action, AiOpsAction.None, "a countdown is not funded");
            f.Cyber = new AiSlotView(OpKind.Asat, OpState.Broken);
            TestAssert.Eq(AiRules.Decide(f).Action, AiOpsAction.Fund, "a broken bar is funded again");
        }

        private static void CheckSeed()
        {
            TestAssert.Near(AiRules.SeedFor(0f, 60f), 40f, "a minute of seed is 40 CR");
            TestAssert.Near(AiRules.SeedFor(0f, 1f), 40f / 60f, "a second of it");
            TestAssert.Near(AiRules.SeedFor(1499f, 60f), 1f, "the seed stops at the 1500 CR cap");
            TestAssert.Near(AiRules.SeedFor(1500f, 60f), 0f, "nothing past the cap");
            TestAssert.Near(AiRules.SeedFor(0f, 600f), 40f, "a long gap is paid as one minute at most (a hitch cannot mint a fortune)");
            TestAssert.Near(AiRules.SeedFor(float.NaN, 5f), 0f, "NaN treasury seeds nothing");
            TestAssert.Near(AiRules.SeedFor(0f, -5f), 0f, "negative time seeds nothing");
        }

        private sealed class AiHost : IAiOpsHost
        {
            public int HumanCount;
            public AiOpsFacts Facts;
            public bool Online = true;
            public OpOutcome Verdict = OpOutcome.Started;
            public readonly List<string> Calls = new List<string>();
            public int Humans => HumanCount;
            public bool TryFacts(float now, out AiOpsFacts facts) { facts = Facts; return Online; }
            public OpResult Plan(OpKind kind, int target) { Calls.Add("plan " + kind + " " + target); return new OpResult(Verdict, kind); }
            public OpResult Fund(OpDomain domain, bool large) { Calls.Add("fund " + domain + (large ? " 50" : " 25")); return new OpResult(Verdict == OpOutcome.Started ? OpOutcome.Funded : Verdict); }
        }

        private static void CheckBrain()
        {
            var host = new AiHost { Facts = Facts(treasury: 800f) };
            var pacer = new WatchPacer();
            var brain = new AiOpsBrain();
            host.Facts.SamNodeId = 5;
            AiOpsPlan p = brain.Step(host, pacer, 100f);
            TestAssert.Eq(p.Action, AiOpsAction.Plan, "a leading AI faction with a treasury plans");
            TestAssert.Eq(host.Calls.Count, 1, "one call");
            TestAssert.Eq(host.Calls[0], "plan ZeroDay 5", "ZERO-DAY on the revealed SAM C2");
            TestAssert.Eq(brain.Plans, 1, "counted");
            TestAssert.Eq(brain.Step(host, pacer, 110f).Action, AiOpsAction.None, "nothing inside the minute");
            host.Facts.Cyber = new AiSlotView(OpKind.ZeroDay, OpState.Funding);
            TestAssert.Eq(brain.Step(host, pacer, 161f).Action, AiOpsAction.Fund, "a minute later it funds the open bar");
            TestAssert.Eq(host.Calls[1], "fund Cyber 50", "a 50 CR tap from 800");
            TestAssert.Eq(brain.Funds, 1, "counted");
            for (float t = 162f; t < 220f; t += 2f) brain.Step(host, pacer, t);
            TestAssert.Eq(host.Calls.Count, 2, "never more than one call per minute");
            TestAssert.Eq(brain.Step(host, pacer, 222f).Action, AiOpsAction.Fund, "the next minute, the next tap");

            // One new plan per half hour: when the operation is over and the slot is free again, the next plan waits.
            host.Facts.Cyber = new AiSlotView(OpKind.None, OpState.Idle);
            int callsBefore = host.Calls.Count;
            TestAssert.Eq(brain.Step(host, pacer, 400f).Action, AiOpsAction.None, "a free slot 5 minutes after the last plan does not plan again");
            TestAssert.Eq(host.Calls.Count, callsBefore, "and made no call");
            TestAssert.Eq(brain.Step(host, pacer, 1901f).Action, AiOpsAction.Plan, "30 minutes after the last plan it may plan the next");

            // A human in the faction: the driver is not its business.
            var staffed = new AiHost { HumanCount = 1, Facts = Facts(treasury: 800f) };
            TestAssert.Eq(new AiOpsBrain().Step(staffed, new WatchPacer(), 100f).Action, AiOpsAction.None, "a faction with a human is never driven");
            TestAssert.Eq(staffed.Calls.Count, 0, "and nothing was called");

            // A refusal costs the same minute a success does, and is counted.
            var refused = new AiHost { Facts = Facts(treasury: 800f), Verdict = OpOutcome.Cooldown };
            var rb = new AiOpsBrain(); var rp = new WatchPacer();
            TestAssert.Eq(rb.Step(refused, rp, 100f).Action, AiOpsAction.None, "a refused plan reports no action");
            TestAssert.Eq(rb.Failures, 1, "failure counted");
            TestAssert.Eq(rb.Step(refused, rp, 130f).Action, AiOpsAction.None, "and it waits the minute");
            TestAssert.Eq(refused.Calls.Count, 1, "no second call inside it");

            // The satellite class rotates with each ASAT planned.
            var asat = new AiHost { Facts = Facts(treasury: 900f) };
            var ab = new AiOpsBrain(); var ap = new WatchPacer();
            ab.Step(asat, ap, 100f);
            asat.Facts.Cyber = new AiSlotView(OpKind.None, OpState.Idle);
            ab.Step(asat, ap, 1901f);
            TestAssert.Eq(asat.Calls[0], "plan Asat 1", "radar first"); TestAssert.Eq(asat.Calls[1], "plan Asat 0", "then optical");
            TestAssert.Eq(ab.AsatsPlanned, 2, "counted");

            // No OPERATIONS for the faction: nothing.
            var none = new AiHost { Facts = Facts(treasury: 900f), Online = false };
            TestAssert.Eq(new AiOpsBrain().Step(none, new WatchPacer(), 100f).Action, AiOpsAction.None, "no facts, no plan");
        }
    }
}
