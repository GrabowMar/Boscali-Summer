using System;
using System.Collections.Generic;
using BoscaliSummer.Modules.Support.Domain.Calls;
using BoscaliSummer.Modules.Support.Runtime;

namespace BoscaliSummer.Tests.Features.Support
{
    /// <summary>Current M0 CALL sheet, pricing, floors, copy, view and arm/request flow.</summary>
    internal static class CallsTests
    {
        public static void Run()
        {
            CheckSheetPricing();
            CheckFloorsWordsView();
            CheckFlow();
            CheckBoundaries();
        }

        private static PriceInputs Plain(float share = float.NaN, int n = 0) =>
            new PriceInputs(share, n, false, null, false, 1f, 1f, 1f);

        private static void CheckSheetPricing()
        {
            Eq(CallSheet.BasePrice(CallTier.Light), 25, "light base");
            Eq(CallSheet.BasePrice(CallTier.Heavy), 70, "heavy base");
            Eq(CallSheet.BasePrice(CallTier.Strategic), 400, "strategic base");

            TestAssert.That(CallSheet.TryGet(SupportActionId.Artillery, out CallRow rod), "rod is a call");
            Eq(rod.Tier, CallTier.Strategic, "rod tier");
            Eq(rod.Label, "ORBITAL ROD", "rod label");
            TestAssert.That(CallSheet.TryGet(SupportActionId.Prsm, out CallRow prsm) && prsm.Tier == CallTier.Light, "prsm light");
            TestAssert.That(!CallSheet.TryGet(SupportActionId.HackPing, out _), "retired hack is not a call");
            TestAssert.That(!CallSheet.TryGet(SupportActionId.JtacUnlase, out _), "unlase is not a priced call");

            // UNDERDOG: -30 % at share 0, 0 at 0.5, +20 % at 1; unknown share = 0.
            Near(CallPricing.Underdog(0f, 10), -0.30f, "underdog at 0");
            Near(CallPricing.Underdog(0.5f, 10), 0f, "underdog at half");
            Near(CallPricing.Underdog(1f, 10), 0.20f, "underdog at 1");
            Near(CallPricing.Underdog(0.25f, 10), -0.15f, "underdog at quarter");
            Near(CallPricing.Underdog(float.NaN, 10), 0f, "unknown share");
            Near(CallPricing.Underdog(0.3f, 0), 0f, "no objectives");
            Near(CallPricing.Underdog(0f, 3), -0.15f, "small map clamp low");
            Near(CallPricing.Underdog(1f, 2), 0.10f, "small map clamp high");

            CallQuote plain = CallPricing.Quote(CallTier.Heavy, Plain());
            Eq(plain.Cost, 70, "plain heavy");
            Eq(plain.Reason, "", "no reason chip");

            CallQuote behind = CallPricing.Quote(CallTier.Strategic, Plain(0f, 10));
            Eq(behind.Cost, 280, "strategic -30 %");
            Eq(behind.Reason, "-30 % UNDERDOG", "underdog chip");

            CallQuote ahead = CallPricing.Quote(CallTier.Strategic, Plain(1f, 10));
            Eq(ahead.Cost, 480, "strategic +20 %");
            Eq(ahead.Reason, "+20 % LEADING", "leading chip");

            var degraded = new PriceInputs(0.5f, 10, true, "UPLINK DOWN", false, 1f, 1f, 1f);
            CallQuote d = CallPricing.Quote(CallTier.Light, degraded);
            Eq(d.Cost, 35, "degraded +40 %");
            Eq(d.Reason, "+40 % UPLINK DOWN", "degraded chip");

            // Largest modifier wins the single chip; costs multiply.
            var mixed = new PriceInputs(0f, 10, true, "UPLINK DOWN", true, 1f, 1f, 1f);
            CallQuote m = CallPricing.Quote(CallTier.Heavy, mixed);
            Eq(m.Cost, 51, "70 x 0.7 x 1.4 x 0.75 = 51.45");
            Eq(m.Reason, "+40 % UPLINK DOWN", "largest modifier chip");

            var perk = new PriceInputs(float.NaN, 0, false, null, false, 1f, 0.9f, 1f);
            Eq(CallPricing.Quote(CallTier.Heavy, perk).Cost, 63, "perk discount has no chip");
            Eq(CallPricing.Quote(CallTier.Heavy, perk).Reason, "", "perk silent");

            var garbage = new PriceInputs(float.NaN, -4, false, null, false, float.NaN, float.PositiveInfinity, 0f);
            TestAssert.That(CallPricing.Quote(CallTier.Light, garbage).Cost >= 1, "garbage inputs still give a sane price");
        }

        private static void CheckFloorsWordsView()
        {
            Near(CallFloors.Share(3, 0, 10), 0.3f, "share held");
            Near(CallFloors.Share(3, 2, 10), 0.4f, "contested counts half");
            TestAssert.That(float.IsNaN(CallFloors.Share(0, 0, 0)), "no objectives = unknown share");
            Near(CallFloors.Share(12, 0, 10), 1f, "share clamps");

            TestAssert.That(CallFloors.Unlocked(CallTier.Light, 0, 10, 0f, 1f), "light always");
            TestAssert.That(!CallFloors.Unlocked(CallTier.Heavy, 0, 10, 30f, 1f), "heavy needs a base");
            TestAssert.That(CallFloors.Unlocked(CallTier.Heavy, 1, 10, 0f, 1f), "heavy with one base");
            TestAssert.That(!CallFloors.Unlocked(CallTier.Strategic, 1, 10, 0f, 1f), "strategic needs 2 of 10");
            TestAssert.That(CallFloors.Unlocked(CallTier.Strategic, 2, 10, 0f, 1f), "strategic with 2");
            TestAssert.That(!CallFloors.Unlocked(CallTier.Strategic, 1, 3, 0f, 1f), "strategic needs 2 of 3");
            TestAssert.That(CallFloors.Unlocked(CallTier.Strategic, 2, 3, 0f, 1f), "strategic 2 of 3");
            // N < 3: time unlock at 8 min x T.
            TestAssert.That(!CallFloors.Unlocked(CallTier.Heavy, 0, 2, 7.9f, 1f), "small map before 8 min");
            TestAssert.That(CallFloors.Unlocked(CallTier.Strategic, 0, 2, 8f, 1f), "small map after 8 min");
            TestAssert.That(CallFloors.Unlocked(CallTier.Heavy, 0, 0, 3.2f, 0.4f), "time unlock scales with T (8 x 0.4 = 3.2)");

            Eq(CallFloors.NextUnlock(0, 10, 0f, 1f), "HOLD A BASE → HEAVY", "next unlock heavy");
            Eq(CallFloors.NextUnlock(1, 10, 0f, 1f), "HOLD 1 MORE → STRATEGIC", "next unlock strategic");
            Eq(CallFloors.NextUnlock(2, 10, 0f, 1f), "", "all unlocked");
            Eq(CallFloors.NextUnlock(0, 2, 5.5f, 1f), "3 MIN → HEAVY", "small map countdown");

            Eq(CallWords.Refusal(CallRefusal.LowCredit, need: 45), "NEGATIVE: LOW CREDIT — NEED 45 CR", "low credit");
            Eq(CallWords.Refusal(CallRefusal.Cooldown, seconds: 12), "NEGATIVE: COOLDOWN 12s", "cooldown");
            Eq(CallWords.Refusal(CallRefusal.Locked, unlock: "HOLD A BASE → HEAVY"), "NEGATIVE: LOCKED — HOLD A BASE → HEAVY", "locked");
            Eq(CallWords.Refusal(CallRefusal.Locked, unlock: ""), "NEGATIVE: LOCKED — HOLD MORE GROUND", "locked empty fix");
            Eq(CallWords.Refusal(CallRefusal.NoAim), "NEGATIVE: NO AIM — DESIGNATE OR RIGHT-CLICK MAP", "no aim");
            Eq(CallWords.Refusal(CallRefusal.Timeout), "NEGATIVE: NO ANSWER — CREDIT RETURNED", "timeout");
            Eq(CallWords.Refusal(CallRefusal.Frozen, seconds: 540), "NEGATIVE: CREDIT FROZEN 9 MIN — NEW FACTION", "frozen");
            Eq(CallWords.Refusal(CallRefusal.None), "", "none");
            Eq(CallWords.Refusal(CallRefusal.Offline), "NEGATIVE: OPS OFFLINE — WAIT FOR THE HOST LINK", "offline");
            Eq(CallWords.Refusal(CallRefusal.Unavailable), "NEGATIVE: UNAVAILABLE — TRY ANOTHER CALL", "unavailable");
            Eq(CallWords.TierWord(CallTier.Strategic), "STRATEGIC", "tier word");

            CallSheet.TryGet(SupportActionId.Artillery, out CallRow rod);
            var quote = new CallQuote(400, "");
            CallTile ready = CallsView.Tile(rod, quote, true, "", 500f, 0f, false, false, false);
            Eq(ready.State, CallState.Ready, "ready");
            Eq(ready.StateWord, "READY", "ready word");
            Eq(ready.CostText, "400 CR", "cost text");
            TestAssert.That(ready.Enabled, "ready enabled");

            Eq(CallsView.Tile(rod, quote, true, "", 100f, 0f, false, false, false).StateWord, "NEED 400 CR", "low credit word");
            Eq(CallsView.Tile(rod, quote, false, "HOLD 1 MORE → STRATEGIC", 900f, 0f, false, false, false).StateWord, "HOLD 1 MORE → STRATEGIC", "locked shows the fix");
            Eq(CallsView.Tile(rod, quote, true, "", 900f, 4.2f, false, false, false).StateWord, "5s", "cooldown rounds up");
            CallTile armed = CallsView.Tile(rod, quote, true, "", 900f, 0f, true, false, false);
            Eq(armed.StateWord, "ARMED — PRESS AGAIN", "armed word");
            TestAssert.That(armed.Enabled, "armed stays pressable");
            Eq(CallsView.Tile(rod, quote, true, "", 900f, 0f, true, true, false).StateWord, "PENDING", "pending beats armed");
            Eq(CallsView.Tile(rod, quote, true, "", 900f, 0f, false, false, true).StateWord, "OFFLINE", "offline beats all");
            TestAssert.That(!CallsView.Tile(rod, quote, true, "", 900f, 0f, false, false, true).Enabled, "offline disabled");
        }

        private static void CheckFlow()
        {
            var arm = new ArmState();
            Eq(arm.Press(SupportActionId.Prsm, 0f), ArmStep.Armed, "first press arms");
            Eq(arm.Armed, SupportActionId.Prsm, "armed id");
            Eq(arm.Press(SupportActionId.Prsm, 7.9f), ArmStep.Fire, "second press within 8 s fires");
            TestAssert.That(arm.Armed == null, "fire clears");
            arm.Press(SupportActionId.Prsm, 10f);
            Eq(arm.Press(SupportActionId.Prsm, 18.01f), ArmStep.Armed, "press after 8 s re-arms");
            Eq(arm.Press(SupportActionId.Cruise, 19f), ArmStep.Armed, "other call switches the arm");
            Eq(arm.Armed, SupportActionId.Cruise, "switched");
            TestAssert.That(!arm.Tick(26f), "still armed at 7 s");
            TestAssert.That(arm.Tick(27.1f), "auto-clear after 8 s");
            TestAssert.That(arm.Armed == null, "cleared");
            TestAssert.That(!arm.Tick(30f), "tick idle is quiet");
            arm.Press(SupportActionId.Emp, 31f);
            arm.Clear();
            TestAssert.That(arm.Armed == null, "clear");

            Eq(Aim.Pick(true, true), AimSource.Pod, "pod beats map");
            Eq(Aim.Pick(false, true), AimSource.Map, "map");
            Eq(Aim.Pick(false, false), AimSource.None, "none");
            Eq(Aim.Label(AimSource.Pod), "AIM: POD", "pod label");
            Eq(Aim.Label(AimSource.Map), "AIM: MAP", "map label");

            var req = new CallRequestTracker();
            TestAssert.That(req.Begin(5, 70f, 0f), "begin");
            TestAssert.That(!req.Begin(6, 25f, 0.5f), "one pending at a time");
            TestAssert.That(!req.Tick(2.9f, out _), "not timed out yet");
            TestAssert.That(req.Resolve(5), "host answer resolves");
            TestAssert.That(!req.Pending, "no longer pending");
            TestAssert.That(req.Begin(7, 25f, 10f), "next request");
            TestAssert.That(req.Tick(13.01f, out float refund), "times out after 3 s");
            Near(refund, 25f, "local refund of the cost");
            TestAssert.That(!req.Resolve(7), "late answer after timeout does not resolve again");
            TestAssert.That(!req.Resolve(99), "unknown id");
        }

        private static void CheckBoundaries()
        {
            SupportActionId[] ids = { SupportActionId.Recon, SupportActionId.Prsm, SupportActionId.JtacMark,
                SupportActionId.MtiSweep, SupportActionId.ElintSweep, SupportActionId.Cruise,
                SupportActionId.FlareMissile, SupportActionId.Fortify, SupportActionId.Artillery, SupportActionId.Emp };
            Eq(CallSheet.Rows.Count, ids.Length, "M0 lists the ten surviving priced CALLs");
            var seen = new HashSet<SupportActionId>();
            for (int i = 0; i < ids.Length; i++)
            {
                CallRow row = CallSheet.Rows[i];
                Eq(row.Id, ids[i], "CALL order at row " + i);
                TestAssert.That(seen.Add(row.Id), "one row per CALL");
                TestAssert.That(CallSheet.TryGet(row.Id, out CallRow found) && found.Label == row.Label &&
                    found.Tier == row.Tier && found.Family == row.Family, "CALL lookup preserves its sheet metadata");
            }
            byte[] retired = { 10, 11, 12, 13, 14, 16, 17, 18, 19, 20, 21, 22, 23, 25, 26, 27 };
            foreach (byte id in retired)
                TestAssert.That(!CallSheet.TryGet((SupportActionId)id, out _), "retired OPS id " + id + " has no CALL row");
            var wire = new HashSet<byte>();
            foreach (SupportActionId id in Enum.GetValues(typeof(SupportActionId)))
                TestAssert.That(wire.Add((byte)id), "action wire ids are unique, including reserved ids");

            Eq(CallPricing.Quote(CallTier.Light, Plain(0.5f, 10)).Cost, 25, "neutral objectives retain base price");
            var rounding = new PriceInputs(float.NaN, 0, false, null, false, 1f, 1f, 0.5f);
            Eq(CallPricing.Quote(CallTier.Light, rounding).Cost, 13, "half credits round away from zero");
            var eventPrice = new PriceInputs(0f, 10, true, "UPLINK DOWN", true, 0.5f, 0.8f, 2f);
            CallQuote eventQuote = CallPricing.Quote(CallTier.Heavy, eventPrice);
            Eq(eventQuote.Cost, 41, "all discounts and knobs multiply before rounding");
            Eq(eventQuote.Reason, "-50 % EVENT", "the largest visible modifier wins one chip");
            Eq(CallPricing.Quote(CallTier.Light, new PriceInputs(float.NaN, 0, false, null, false,
                float.NaN, float.PositiveInfinity, -1f)).Cost, 25, "invalid multipliers use the base price");
            Near(CallFloors.Share(-1, -5, 5), 0f, "negative census counts cannot discount beyond zero share");
            TestAssert.That(!CallFloors.Unlocked(CallTier.Heavy, 0, 2, 7.999f, 1f) &&
                CallFloors.Unlocked(CallTier.Heavy, 0, 2, 8f, 1f), "tiny-map floors open exactly at eight minutes");
            TestAssert.That(!CallFloors.Unlocked(CallTier.Strategic, 0, 0, 11.99f, 9f) &&
                CallFloors.Unlocked(CallTier.Strategic, 0, 0, 12f, 9f), "time scale is capped at 1.5");

            CallSheet.TryGet(SupportActionId.Prsm, out CallRow prsm);
            var quote = new CallQuote(25, null);
            CallTile offline = CallsView.Tile(prsm, quote, false, "LOCKED", 0f, 10f, true, true, true);
            Eq(offline.State, CallState.Offline, "offline outranks pending, armed, locked, cooldown and credit");
            TestAssert.That(!offline.Enabled && offline.Reason == "", "offline cannot fire; null reason shows no chip");
            Eq(CallsView.Tile(prsm, quote, false, "LOCKED", 0f, 10f, true, true, false).State,
                CallState.Pending, "pending outranks armed and local gating");
            Eq(CallsView.Tile(prsm, quote, false, "LOCKED", 0f, 10f, true, false, false).State,
                CallState.Armed, "armed remains pressable until server revalidation");
            Eq(CallsView.Tile(prsm, quote, false, "", 0f, 10f, false, false, false).StateWord,
                "LOCKED", "a missing unlock hint still says locked");
            Eq(CallsView.Tile(prsm, quote, true, "", 0f, 10f, false, false, false).State,
                CallState.Cooldown, "cooldown outranks insufficient credit");
            TestAssert.That(CallsView.Tile(prsm, quote, true, "", 25f, 0.05f, false, false, false).Enabled,
                "exact price and settled cooldown make the CALL ready");

            foreach (CallRefusal refusal in Enum.GetValues(typeof(CallRefusal)))
                if (refusal != CallRefusal.None)
                    TestAssert.That(CallWords.Refusal(refusal, 25, 1).StartsWith("NEGATIVE: "),
                        "every current refusal has player-facing words: " + refusal);
            Eq(CallWords.Refusal(CallRefusal.Frozen, seconds: 1),
                "NEGATIVE: CREDIT FROZEN 1 MIN — NEW FACTION", "frozen credit rounds partial minutes up");
            Eq(Aim.Label(AimSource.None), "AIM: NONE", "no aim is explicit");

            var arm = new ArmState();
            arm.Press(SupportActionId.Prsm, 100f);
            TestAssert.That(!arm.Tick(108f), "arming remains valid at exactly eight seconds");
            Eq(arm.Press(SupportActionId.Prsm, 108f), ArmStep.Fire, "the boundary second press fires once");
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

        private static void Near(float actual, float expected, string message) =>
            TestAssert.That(Math.Abs(actual - expected) < 0.01f, message + " (got " + actual + ", want " + expected + ")");

        private static void Eq<T>(T actual, T expected, string message) =>
            TestAssert.That(Equals(actual, expected), message + " (got " + actual + ", want " + expected + ")");
    }
}
