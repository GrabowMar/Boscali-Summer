using System;
using System.Collections.Generic;
using BoscaliSummer.Modules.Support.Domain.Calls;
using BoscaliSummer.Modules.Support.Domain.Fronts;
using BoscaliSummer.Modules.Support.Domain.Space;
using BoscaliSummer.Modules.Support.Runtime;

namespace BoscaliSummer.Tests.Features.Support
{
    /// <summary>Current M0 perk catalogue, allocation pricing, copy, view and arm/request flow.</summary>
    internal static class CallsTests
    {
        public static void Run()
        {
            CheckSheetPricing();
            CheckPerkCatalogue();
            CheckAllocationRules();
            CheckWordsView();
            CheckFlow();
            CheckBoundaries();
        }

        private static PriceInputs Plain(float scale = 1f) =>
            new PriceInputs(false, null, false, 1f, 1f, scale);

        private static void CheckSheetPricing()
        {
            // The rung table of OPS FRONTS spec 3.6.
            int[] prices = { 4, 6, 10, 16, 30 };
            float[] cooldowns = { 30f, 45f, 60f, 120f, 300f };
            for (int rung = 1; rung <= 5; rung++)
            {
                TestAssert.Eq(CallSheet.BasePrice(rung), prices[rung - 1], "base price of R" + rung);
                TestAssert.Near(CallSheet.Cooldown(rung), cooldowns[rung - 1], "cooldown of R" + rung);
                TestAssert.Eq(CallPricing.Quote(rung, Plain()).Cost, prices[rung - 1], "plain quote of R" + rung);
            }
            TestAssert.Eq(CallSheet.BasePrice(0), 4, "rung is clamped low");
            TestAssert.Eq(CallSheet.BasePrice(9), 30, "rung is clamped high");

            TestAssert.That(CallSheet.TryGet(SupportActionId.Artillery, out CallRow rod), "rod is a perk");
            TestAssert.Eq(rod.Rung, 5, "rod rung");
            TestAssert.Eq(rod.Front, Front.Space, "rod front");
            TestAssert.Eq(rod.Label, "ORBITAL ROD", "rod label");
            TestAssert.Eq(rod.RungWord, "SPACE R5", "rod rung word");
            TestAssert.That(CallSheet.TryGet(SupportActionId.Prsm, out CallRow prsm) && prsm.Rung == 3, "prsm is R3");
            TestAssert.That(!CallSheet.TryGet(SupportActionId.HackPing, out _), "retired hack is not a perk");
            TestAssert.That(!CallSheet.TryGet(SupportActionId.MtiSweep, out _), "MTI SWEEP merged into RECON PASS");
            TestAssert.That(!CallSheet.TryGet(SupportActionId.JtacUnlase, out _), "unlase is not a priced perk");

            CallQuote plain = CallPricing.Quote(3, Plain());
            TestAssert.Eq(plain.Cost, 10, "plain R3");
            TestAssert.Eq(plain.Reason, "", "no reason chip");
            TestAssert.Eq(CallPricing.Quote(5, Plain(2f)).Cost, 60, "PerkPriceScale doubles a price");
            TestAssert.Eq(CallPricing.Quote(1, Plain(0.5f)).Cost, 2, "PerkPriceScale halves a price");

            var degraded = new PriceInputs(true, "UPLINK DOWN", false, 1f, 1f, 1f);
            CallQuote d = CallPricing.Quote(5, degraded);
            TestAssert.Eq(d.Cost, 42, "degraded +40 %");
            TestAssert.Eq(d.Reason, "+40 % UPLINK DOWN", "degraded chip");

            // Largest modifier wins the single chip; costs multiply.
            var mixed = new PriceInputs(true, "UPLINK DOWN", true, 1f, 1f, 1f);
            CallQuote m = CallPricing.Quote(5, mixed);
            TestAssert.Eq(m.Cost, 32, "30 x 1.4 x 0.75 = 31.5");
            TestAssert.Eq(m.Reason, "+40 % UPLINK DOWN", "largest modifier chip");

            var perk = new PriceInputs(false, null, false, 1f, 0.8f, 1f);
            TestAssert.Eq(CallPricing.Quote(5, perk).Cost, 24, "perk discount has no chip");
            TestAssert.Eq(CallPricing.Quote(5, perk).Reason, "", "perk silent");

            var garbage = new PriceInputs(false, null, false, float.NaN, float.PositiveInfinity, 0f);
            TestAssert.That(CallPricing.Quote(1, garbage).Cost >= 1, "garbage inputs still give a sane price");
        }

        private static void CheckPerkCatalogue()
        {
            // Spec 3.6 as implemented: front, rung, id and label of all 14 perks.
            var expected = new (SupportActionId id, Front front, int rung, string label)[]
            {
                (SupportActionId.Recon, Front.Space, 1, "RECON PASS"),
                (SupportActionId.SatCamera, Front.Space, 2, "SAT CAMERA"),
                (SupportActionId.Prsm, Front.Space, 3, "PRSM"),
                (SupportActionId.Cruise, Front.Space, 4, "CRUISE SALVO"),
                (SupportActionId.Artillery, Front.Space, 5, "ORBITAL ROD"),
                (SupportActionId.ElintSweep, Front.Cyber, 1, "ELINT SWEEP"),
                (SupportActionId.FlareMissile, Front.Cyber, 2, "DECOY BARRAGE"),
                (SupportActionId.RadarBlind, Front.Cyber, 3, "RADAR BLIND"),
                (SupportActionId.SamNetDown, Front.Cyber, 4, "SAM NET DOWN"),
                (SupportActionId.Emp, Front.Cyber, 5, "EMP"),
                (SupportActionId.JtacMark, Front.Sof, 1, "JTAC LASE"),
                (SupportActionId.ReconTeam, Front.Sof, 2, "RECON TEAM"),
                (SupportActionId.Fortify, Front.Sof, 3, "FORTIFY"),
                (SupportActionId.SabotageStrike, Front.Sof, 4, "SABOTAGE STRIKE"),
            };
            TestAssert.Eq(CallSheet.Rows.Count, expected.Length, "fourteen perks");
            var ids = new HashSet<SupportActionId>();
            var labels = new HashSet<string>();
            var rungs = new Dictionary<Front, HashSet<int>>
                { { Front.Space, new HashSet<int>() }, { Front.Cyber, new HashSet<int>() }, { Front.Sof, new HashSet<int>() } };
            for (int i = 0; i < expected.Length; i++)
            {
                CallRow row = CallSheet.Rows[i];
                TestAssert.Eq(row.Id, expected[i].id, "perk id at row " + i);
                TestAssert.Eq(row.Front, expected[i].front, "perk front at row " + i);
                TestAssert.Eq(row.Rung, expected[i].rung, "perk rung at row " + i);
                TestAssert.Eq(row.Label, expected[i].label, "perk label at row " + i);
                TestAssert.That(row.Rung >= 1 && row.Rung <= 5, "rung is 1..5");
                TestAssert.That(ids.Add(row.Id) && labels.Add(row.Label), "perk ids and labels are unique");
                TestAssert.That(rungs[row.Front].Add(row.Rung), "one perk per rung per front");
                TestAssert.Eq(CallPricing.Quote(row.Rung, Plain()).Cost, new[] { 4, 6, 10, 16, 30 }[row.Rung - 1], "perk price by rung: " + row.Label);
                TestAssert.Near(CallSheet.Cooldown(row.Rung), new[] { 30f, 45f, 60f, 120f, 300f }[row.Rung - 1], "perk cooldown by rung: " + row.Label);
            }
            TestAssert.Eq(rungs[Front.Space].Count, 5, "SPACE has five rungs");
            TestAssert.Eq(rungs[Front.Cyber].Count, 5, "CYBER has five rungs");
            TestAssert.Eq(rungs[Front.Sof].Count, 4, "SOF has four rungs");
            // Ids added by S0 sit after the highest earlier id and never reuse a retired one.
            TestAssert.That((byte)SupportActionId.ReconTeam == 40 && (byte)SupportActionId.SabotageStrike == 41 &&
                (byte)SupportActionId.RadarBlind == 42 && (byte)SupportActionId.SamNetDown == 43, "new perk ids are appended");
            TestAssert.That((byte)SupportActionId.MtiSweep == 24, "the retired MTI id keeps its number");
            TestAssert.That(CallSheet.TryGet(SupportActionId.Recon, out CallRow recon) && recon.Label == "RECON PASS",
                "RECON PASS keeps the RADAR SCAN wire id");

            // RECON PASS reveals static and moving contacts: the window it opens admits any speed.
            TestAssert.That(SpaceRevealWindow.TryCreate(BirdKind.Radar, 100f, 100f, 1000f, 100f, SpaceRevealWindow.ObservationSeconds,
                0f, SpaceRevealWindow.AnySpeed, out SpaceRevealWindow pass), "a RECON PASS window opens");
            TestAssert.That(pass.Contains(100f, 100f, 0f, 101f), "RECON PASS admits a static contact");
            TestAssert.That(pass.Contains(100f, 100f, 2.5f, 101f) && pass.Contains(100f, 100f, 25f, 101f) && pass.Contains(100f, 100f, -25f, 101f),
                "RECON PASS admits moving contacts");
            TestAssert.That(!pass.Contains(5000f, 100f, 25f, 101f), "RECON PASS stays inside its radius");
        }

        private static void CheckAllocationRules()
        {
            TestAssert.That(AllocationRules.CanAfford(10f, 10f), "exact balance affords");
            TestAssert.That(AllocationRules.CanAfford(9.9995f, 10f), "float slack affords");
            TestAssert.That(!AllocationRules.CanAfford(9f, 10f), "short balance is refused");
            TestAssert.That(!AllocationRules.CanAfford(float.NaN, 1f) && !AllocationRules.CanAfford(5f, float.NaN) &&
                !AllocationRules.CanAfford(5f, -1f) && !AllocationRules.CanAfford(float.PositiveInfinity, 1f), "garbage never affords");
            TestAssert.Near(AllocationRules.AfterSpend(20f, 6f), 14f, "spend");
            TestAssert.Near(AllocationRules.AfterSpend(3f, 6f), 0f, "spend never goes negative");
            // A refund puts back exactly what was taken: spend then refund is the identity.
            float start = 37.5f, cost = 16f;
            TestAssert.Near(AllocationRules.AfterRefund(AllocationRules.AfterSpend(start, cost), cost), start, "refund restores the balance");
            TestAssert.Near(AllocationRules.AfterRefund(10f, -5f), 10f, "a negative refund is ignored");
        }

        private static void CheckWordsView()
        {
            TestAssert.Eq(CallWords.Refusal(CallRefusal.LowCredit, need: 45), "NEGATIVE: LOW ALLOCATION — NEED 45", "low allocation");
            TestAssert.Eq(CallWords.Refusal(CallRefusal.Cooldown, seconds: 12), "NEGATIVE: COOLDOWN — WAIT 12s", "cooldown");
            TestAssert.Eq(CallWords.Refusal(CallRefusal.Locked, unlock: "NEEDS STRIKE QUALIFICATION"), "NEGATIVE: LOCKED — NEEDS STRIKE QUALIFICATION", "locked");
            TestAssert.Eq(CallWords.Refusal(CallRefusal.Locked, unlock: ""), "NEGATIVE: LOCKED — PERK NOT AUTHORISED", "locked empty fix");
            TestAssert.Eq(CallWords.Refusal(CallRefusal.NoAim), "NEGATIVE: NO AIM — DESIGNATE OR RIGHT-CLICK MAP", "no aim");
            TestAssert.Eq(CallWords.Refusal(CallRefusal.None), "", "none");
            TestAssert.Eq(CallWords.Refusal(CallRefusal.Offline), "NEGATIVE: OPS OFFLINE — WAIT FOR THE HOST LINK", "offline");
            TestAssert.Eq(CallWords.Refusal(CallRefusal.Unavailable), "NEGATIVE: UNAVAILABLE — TRY ANOTHER CALL", "unavailable");

            CallSheet.TryGet(SupportActionId.Artillery, out CallRow rod);
            var quote = new CallQuote(30, "");
            CallTile ready = CallsView.Tile(rod, quote, "", 50f, 0f, false, false, false);
            TestAssert.Eq(ready.State, CallState.Ready, "ready");
            TestAssert.Eq(ready.StateWord, "READY", "ready word");
            TestAssert.Eq(ready.CostText, "30 ALLOC", "cost text");
            TestAssert.Eq(ready.RungWord, "SPACE R5", "rung word");
            TestAssert.That(ready.Enabled, "ready enabled");

            TestAssert.Eq(CallsView.Tile(rod, quote, "", 10f, 0f, false, false, false).StateWord, "NEED 30 ALLOC", "low allocation word");
            CallTile locked = CallsView.Tile(rod, quote, "NEEDS STRIKE QUALIFICATION", 90f, 0f, false, false, false);
            TestAssert.Eq(locked.State, CallState.Locked, "an unqualified pilot sees LOCKED");
            TestAssert.Eq(locked.StateWord, "NEEDS STRIKE QUALIFICATION", "locked shows the qualification");
            TestAssert.That(!locked.Enabled, "locked is not pressable");
            TestAssert.Eq(CallsView.Tile(rod, quote, "NEEDS READINESS 3", 90f, 0f, false, false, false).StateWord, "NEEDS READINESS 3", "locked shows the readiness");
            TestAssert.Eq(CallsView.Tile(rod, quote, "", 90f, 4.2f, false, false, false).StateWord, "5s", "cooldown rounds up");
            CallTile armed = CallsView.Tile(rod, quote, "", 90f, 0f, true, false, false);
            TestAssert.Eq(armed.StateWord, "ARMED — PRESS AGAIN", "armed word");
            TestAssert.That(armed.Enabled, "armed stays pressable");
            TestAssert.Eq(CallsView.Tile(rod, quote, "", 90f, 0f, true, true, false).StateWord, "PENDING", "pending beats armed");
            TestAssert.Eq(CallsView.Tile(rod, quote, "", 90f, 0f, false, false, true).StateWord, "OFFLINE", "offline beats all");
            TestAssert.That(!CallsView.Tile(rod, quote, "", 90f, 0f, false, false, true).Enabled, "offline disabled");
        }

        private static void CheckFlow()
        {
            var arm = new ArmState();
            TestAssert.Eq(arm.Press(SupportActionId.Prsm, 0f), ArmStep.Armed, "first press arms");
            TestAssert.Eq(arm.Armed, SupportActionId.Prsm, "armed id");
            TestAssert.Eq(arm.Press(SupportActionId.Prsm, 7.9f), ArmStep.Fire, "second press within 8 s fires");
            TestAssert.That(arm.Armed == null, "fire clears");
            arm.Press(SupportActionId.Prsm, 10f);
            TestAssert.Eq(arm.Press(SupportActionId.Prsm, 18.01f), ArmStep.Armed, "press after 8 s re-arms");
            TestAssert.Eq(arm.Press(SupportActionId.Cruise, 19f), ArmStep.Armed, "other call switches the arm");
            TestAssert.Eq(arm.Armed, SupportActionId.Cruise, "switched");
            TestAssert.That(!arm.Tick(26f), "still armed at 7 s");
            TestAssert.That(arm.Tick(27.1f), "auto-clear after 8 s");
            TestAssert.That(arm.Armed == null, "cleared");
            TestAssert.That(!arm.Tick(30f), "tick idle is quiet");
            arm.Press(SupportActionId.Emp, 31f);
            arm.Clear();
            TestAssert.That(arm.Armed == null, "clear");

            TestAssert.Eq(Aim.Pick(true, true), AimSource.Pod, "pod beats map");
            TestAssert.Eq(Aim.Pick(false, true), AimSource.Map, "map");
            TestAssert.Eq(Aim.Pick(false, false), AimSource.None, "none");
            TestAssert.Eq(Aim.Label(AimSource.Pod), "AIM: POD", "pod label");
            TestAssert.Eq(Aim.Label(AimSource.Map), "AIM: MAP", "map label");

            var req = new CallRequestTracker();
            TestAssert.That(req.Begin(5, 70f, 0f), "begin");
            TestAssert.That(!req.Begin(6, 25f, 0.5f), "one pending at a time");
            TestAssert.That(!req.Tick(2.9f, out _), "not timed out yet");
            TestAssert.That(req.Resolve(5), "host answer resolves");
            TestAssert.That(!req.Pending, "no longer pending");
            TestAssert.That(req.Begin(7, 25f, 10f), "next request");
            TestAssert.That(req.Tick(13.01f, out float refund), "times out after 3 s");
            TestAssert.Near(refund, 25f, "local refund of the cost");
            TestAssert.That(!req.Resolve(7), "late answer after timeout does not resolve again");
            TestAssert.That(!req.Resolve(99), "unknown id");
        }

        private static void CheckBoundaries()
        {
            byte[] retired = { 10, 11, 12, 13, 14, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27 };
            foreach (byte id in retired)
                TestAssert.That(!CallSheet.TryGet((SupportActionId)id, out _), "retired OPS id " + id + " has no perk row");
            var wire = new HashSet<byte>();
            foreach (SupportActionId id in Enum.GetValues(typeof(SupportActionId)))
                TestAssert.That(wire.Add((byte)id), "action wire ids are unique, including reserved ids");

            var rounding = new PriceInputs(false, null, false, 1f, 1f, 0.5f);
            TestAssert.Eq(CallPricing.Quote(2, rounding).Cost, 3, "half allocations round away from zero");
            var eventPrice = new PriceInputs(true, "UPLINK DOWN", true, 0.5f, 0.8f, 2f);
            CallQuote eventQuote = CallPricing.Quote(4, eventPrice);
            TestAssert.Eq(eventQuote.Cost, 13, "all modifiers multiply before rounding (16 x 1.4 x 0.75 x 0.5 x 0.8 x 2 = 13.44)");
            TestAssert.Eq(eventQuote.Reason, "-50 % EVENT", "the largest visible modifier wins one chip");
            TestAssert.Eq(CallPricing.Quote(1, new PriceInputs(false, null, false, float.NaN, float.PositiveInfinity, -1f)).Cost, 4,
                "invalid multipliers use the base price");

            CallSheet.TryGet(SupportActionId.Prsm, out CallRow prsm);
            var quote = new CallQuote(10, null);
            CallTile offline = CallsView.Tile(prsm, quote, "LOCKED", 0f, 10f, true, true, true);
            TestAssert.Eq(offline.State, CallState.Offline, "offline outranks pending, armed, locked, cooldown and allocation");
            TestAssert.That(!offline.Enabled && offline.Reason == "", "offline cannot fire; null reason shows no chip");
            TestAssert.Eq(CallsView.Tile(prsm, quote, "LOCKED", 0f, 10f, true, true, false).State,
                CallState.Pending, "pending outranks armed and local gating");
            TestAssert.Eq(CallsView.Tile(prsm, quote, "LOCKED", 0f, 10f, true, false, false).State,
                CallState.Armed, "armed remains pressable until server revalidation");
            TestAssert.Eq(CallsView.Tile(prsm, quote, "LOCKED", 0f, 10f, false, false, false).State,
                CallState.Locked, "locked outranks cooldown");
            TestAssert.Eq(CallsView.Tile(prsm, quote, "", 0f, 10f, false, false, false).State,
                CallState.Cooldown, "cooldown outranks insufficient allocation");
            TestAssert.That(CallsView.Tile(prsm, quote, "", 10f, 0.05f, false, false, false).Enabled,
                "exact price and settled cooldown make the perk ready");

            foreach (CallRefusal refusal in Enum.GetValues(typeof(CallRefusal)))
                if (refusal != CallRefusal.None)
                    TestAssert.That(CallWords.Refusal(refusal, 25, 1).StartsWith("NEGATIVE: "),
                        "every current refusal has player-facing words: " + refusal);
            TestAssert.Eq(Aim.Label(AimSource.None), "AIM: NONE", "no aim is explicit");

            var arm = new ArmState();
            arm.Press(SupportActionId.Prsm, 100f);
            TestAssert.That(!arm.Tick(108f), "arming remains valid at exactly eight seconds");
            TestAssert.Eq(arm.Press(SupportActionId.Prsm, 108f), ArmStep.Fire, "the boundary second press fires once");
            TestAssert.That(arm.Armed == null && !arm.Tick(109f), "firing clears the arm without another expiry");
            var request = new CallRequestTracker();
            TestAssert.That(request.Begin(42, 70f, 100f), "request begins");
            TestAssert.That(!request.Resolve(41) && request.Pending && request.PendingId == 42,
                "a stale reply cannot clear the current pending request");
            TestAssert.That(request.Tick(103f, out float refund) && refund == 70f,
                "the silence timeout returns its display cost at exactly three seconds");
            TestAssert.That(!request.Tick(104f, out refund) && refund == 0f,
                "timeout refund signal occurs only once");
            request.Begin(43, 25f, 110f);
            request.Clear();
            TestAssert.That(!request.Pending && !request.Resolve(43) && !request.Tick(120f, out _),
                "scene reset cannot resolve or refund an old request");
        }
    }
}
