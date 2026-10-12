using System;
using System.Collections.Generic;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Modules.TheaterOps.Domain;

namespace BoscaliSummer.Tests.Features.TheaterOps
{
    /// <summary>Reason, contact, stall, slot and ledger rules of the ground steering (annex 02 G4, G5, G6, G7, G9, tables of 6.6).</summary>
    internal static class GroundRulesTests
    {
        public static void Run()
        {
            ReasonsComeInPriorityOrder();
            PendingNeverClaimsNoVehiclesOrNoFront();
            TokensAreUniqueAndFixed();
            ContactConstantsAreTheSpecifiedOnes();
            FreshnessWindow();
            ContinuousSightingReleasesAtTheCeiling();
            TheTailHoldsThenReleases();
            TheTailIsCappedAtTheCeiling();
            NoSightingNoHalt();
            FlickeringSightingsCannotEvadeTheCeiling();
            ANonFiniteClockLeavesTheContactClockAlone();
            ApproachArmsOnFirstSight();
            ApproachStallsAfterTheWindow();
            AGainResetsTheWindow();
            SlowMoversThatGainAreNotStalled();
            ArrivedIsNeverStalled();
            ANonFiniteDistanceDoesNotResetTheApproach();
            ResetRearmsAfterAStageChange();
            EtaIsUnknownUntilThereIsARate();
            ClosingNeedsEvidence();
            MeasuredLeadsFromSliceZero();
            FrozenClockStallsAfterTwoMinutesAndAPlacedPassClearsIt();
            FrozenClockIgnoresAClockItCannotRead();
            SlotSearchOffsets();
            SlotPullWalksTowardTheGroupAndStopsAtIt();
            SlotPullRefusesWhatItCannotMeasure();
            FrontSideProbeWinsThenHistoryThenCentroid();
            FrontSideNeverGuessesWhenNothingIsKnown();
            LedgerCountsOnlyTakenOverVehiclesAsGone();
            AnUnavailableReportIsUnknownEverywhere();
            MarksKeepWhatTheyAreGiven();
        }

        // ---- GroundReasonRule

        private static void ReasonsComeInPriorityOrder()
        {
            TestAssert.Eq(GroundReasonRule.Decide(true, true, true, false, 8, 2), GroundReason.Ready, "everything in place is READY");
            // Every cause below is true at once; switching the first one off exposes the next.
            TestAssert.Eq(GroundReasonRule.Decide(false, false, false, true, -1, -1), GroundReason.NotHost, "a client is NOT_HOST before anything else");
            TestAssert.Eq(GroundReasonRule.Decide(true, false, false, true, -1, -1), GroundReason.Unsupported, "a broken vanilla field is UNSUPPORTED before tactics");
            TestAssert.Eq(GroundReasonRule.Decide(true, true, false, true, -1, -1), GroundReason.TacticsOff, "tactics off is TACTICS_OFF before the RTS");
            TestAssert.Eq(GroundReasonRule.Decide(true, true, true, true, -1, -1), GroundReason.RtsOwnsOrders, "the RTS is named before the pending sweep");
            TestAssert.Eq(GroundReasonRule.Decide(true, true, true, false, -1, -1), GroundReason.Pending, "an unfinished sweep is PENDING before NO_VEHICLES");
            TestAssert.Eq(GroundReasonRule.Decide(true, true, true, false, 0, 0), GroundReason.NoVehicles, "no vehicles is named before no front");
            TestAssert.Eq(GroundReasonRule.Decide(true, true, true, false, 4, 0), GroundReason.NoFront, "vehicles but no front trace");
        }

        private static void PendingNeverClaimsNoVehiclesOrNoFront()
        {
            TestAssert.Eq(GroundReasonRule.Decide(true, true, true, false, -1, 3), GroundReason.Pending, "an unknown eligible count is PENDING");
            TestAssert.Eq(GroundReasonRule.Decide(true, true, true, false, 5, -1), GroundReason.Pending, "an unknown trace count is PENDING");
            TestAssert.Eq(GroundReasonRule.Decide(true, true, true, false, 0, -1), GroundReason.Pending, "zero vehicles is not claimed before the first trace copy");
            TestAssert.Eq(GroundReasonRule.Decide(true, true, true, false, -1, 0), GroundReason.Pending, "zero fronts is not claimed before the first sweep");
            TestAssert.Eq(GroundReasonRule.Decide(true, true, true, false, 0, 3), GroundReason.NoVehicles, "a finished sweep that found none is NO_VEHICLES");
        }

        private static void TokensAreUniqueAndFixed()
        {
            var seen = new HashSet<string>();
            foreach (GroundReason reason in Enum.GetValues(typeof(GroundReason)))
            {
                string token = GroundReasonRule.Token(reason);
                TestAssert.That(!string.IsNullOrEmpty(token), "every reason has a token: " + reason);
                TestAssert.That(seen.Add(token), "tokens are unique: " + token);
            }
            TestAssert.Eq(GroundReasonRule.Token(GroundReason.Ready), "READY", "READY token");
            TestAssert.Eq(GroundReasonRule.Token(GroundReason.TacticsOff), "TACTICS_OFF", "TACTICS_OFF token");
            TestAssert.Eq(GroundReasonRule.Token(GroundReason.RtsOwnsOrders), "RTS_OWNS_ORDERS", "RTS_OWNS_ORDERS token");
            TestAssert.Eq(GroundReasonRule.Token(GroundReason.Unsupported), "UNSUPPORTED", "UNSUPPORTED token");
            TestAssert.Eq(GroundReasonRule.Token(GroundReason.NotHost), "NOT_HOST", "NOT_HOST token");
            TestAssert.Eq(GroundReasonRule.Token(GroundReason.NoVehicles), "NO_VEHICLES", "NO_VEHICLES token");
            TestAssert.Eq(GroundReasonRule.Token(GroundReason.NoFront), "NO_FRONT", "NO_FRONT token");
            TestAssert.Eq(GroundReasonRule.Token(GroundReason.Pending), "PENDING", "PENDING token");
            TestAssert.Eq(GroundReasonRule.Token(GroundReason.Unknown), "UNKNOWN", "UNKNOWN token");
            TestAssert.Eq(GroundReasonRule.Token((GroundReason)99), "UNKNOWN", "a value outside the enum reads UNKNOWN");
        }

        // ---- ContactRule

        private static void ContactConstantsAreTheSpecifiedOnes()
        {
            TestAssert.Near(ContactRule.RadiusMeters, 700f, "a sighting halts a group within 700 m of its lead");
            TestAssert.Near(ContactRule.MaxHoldSeconds, 90f, "the whole halt is at most 90 s");
            TestAssert.Near(ContactRule.GraceSeconds, 60f, "a ceiling release ignores sightings for 60 s");
            TestAssert.Near(ContactRule.TailSeconds, 25f, "the halt lingers 25 s after the last sighting");
            TestAssert.Near(ContactRule.FreshSeconds, ContactWindow.FreshSeconds, "the fresh window is the one shared constant (N-10)");
        }

        private static void FreshnessWindow()
        {
            TestAssert.That(ContactRule.Fresh(0f), "spotted just now is fresh");
            TestAssert.That(ContactRule.Fresh(30f), "spotted 30 s ago is still fresh");
            TestAssert.That(!ContactRule.Fresh(30.1f), "spotted 30.1 s ago is not");
            TestAssert.That(!ContactRule.Fresh(-1f), "a negative age is not a sighting");
            TestAssert.That(!ContactRule.Fresh(float.NaN), "a NaN age is not a sighting");
            TestAssert.That(!ContactRule.Fresh(float.PositiveInfinity), "an infinite age is not a sighting");
        }

        private static void ContinuousSightingReleasesAtTheCeiling()
        {
            var clock = default(ContactClock);
            TestAssert.That(ContactRule.Step(ref clock, true, 100f), "a sighting at 100 halts the group");
            TestAssert.Near(clock.Since, 100f, "the halt began at 100");
            for (float now = 102f; now <= 190f; now += 2f)
                TestAssert.That(ContactRule.Step(ref clock, true, now), "still halted at " + now);
            TestAssert.That(!ContactRule.Step(ref clock, true, 192f), "released at 192, past the 90 s ceiling");
            TestAssert.Near(clock.GraceUntil, 252f, "the grace runs 60 s from the release");
            TestAssert.Near(clock.Since, 0f, "no halt is running");
            TestAssert.Near(clock.TailUntil, 0f, "a ceiling release has no tail");
            TestAssert.That(!ContactRule.Step(ref clock, true, 200f), "a sighting at 200 is ignored");
            TestAssert.That(!ContactRule.Step(ref clock, true, 250f), "a sighting at 250 is ignored");
            TestAssert.That(!ContactRule.Step(ref clock, true, 251.9f), "a sighting just before the grace ends is ignored");
            TestAssert.That(ContactRule.Step(ref clock, true, 253f), "a sighting at 253 starts a new halt");
            TestAssert.Near(clock.Since, 253f, "with a new clock");
        }

        private static void TheTailHoldsThenReleases()
        {
            var clock = default(ContactClock);
            ContactRule.Step(ref clock, true, 100f);
            ContactRule.Step(ref clock, true, 120f);
            TestAssert.Near(clock.TailUntil, 145f, "the tail runs 25 s after the last sighting");
            TestAssert.That(ContactRule.Step(ref clock, false, 122f), "the tail holds the group");
            TestAssert.That(ContactRule.Step(ref clock, false, 144f), "still held at 144");
            TestAssert.That(!ContactRule.Step(ref clock, false, 145f), "released at 145");
            TestAssert.Near(clock.Since, 0f, "the halt is over");
            TestAssert.Near(clock.GraceUntil, 0f, "an ordinary tail expiry earns no grace");
            TestAssert.That(ContactRule.Step(ref clock, true, 146f), "a new sighting right after halts again");
        }

        private static void TheTailIsCappedAtTheCeiling()
        {
            var clock = default(ContactClock);
            ContactRule.Step(ref clock, true, 100f);
            TestAssert.That(ContactRule.Step(ref clock, true, 189f), "a sighting at 189 holds");
            TestAssert.Near(clock.TailUntil, 190f, "the tail is capped at the ceiling, Since + 90");
            TestAssert.That(ContactRule.Step(ref clock, false, 189.5f), "held just before the ceiling");
            TestAssert.That(!ContactRule.Step(ref clock, false, 190f), "released at 190");
            // The tail expired at the ceiling, so the ceiling's grace applies: a sighting at once does not start another 90 s halt.
            TestAssert.Near(clock.GraceUntil, 250f, "a release at the ceiling earns the grace");
            TestAssert.That(!ContactRule.Step(ref clock, true, 191f), "a sighting at 191 is ignored");
            TestAssert.That(ContactRule.Step(ref clock, true, 250f), "a sighting at 250 starts a new halt");
        }

        private static void NoSightingNoHalt()
        {
            var clock = default(ContactClock);
            TestAssert.That(!ContactRule.Step(ref clock, false, 100f), "no sighting, no halt");
            TestAssert.Near(clock.Since, 0f, "nothing started");
            TestAssert.That(!ContactRule.Step(ref clock, false, 500f), "still none");
        }

        private static void FlickeringSightingsCannotEvadeTheCeiling()
        {
            Flicker(k => true, "continuous");
            Flicker(k => k % 2 == 0, "every second pass");
            Flicker(k => k % 3 != 2, "two passes in three");
            Flicker(k => k % 7 == 0, "one pass in seven");
        }

        private static void Flicker(Func<int, bool> sighted, string name)
        {
            var clock = default(ContactClock);
            var runs = new List<float[]>();
            float start = -1f, last = -1f;
            for (int pass = 0; pass <= 450; pass++)
            {
                float now = 100f + 2f * pass;
                if (ContactRule.Step(ref clock, sighted(pass), now))
                {
                    if (start < 0f) start = now;
                    last = now;
                }
                else if (start >= 0f)
                {
                    runs.Add(new[] { start, last });
                    start = -1f;
                }
            }
            if (start >= 0f) runs.Add(new[] { start, last });
            TestAssert.That(runs.Count >= 3, name + ": the halt repeats, so the window is not vacuous");
            for (int i = 0; i < runs.Count; i++)
            {
                TestAssert.That(runs[i][1] - runs[i][0] <= ContactRule.MaxHoldSeconds, name + ": a halt never lasts more than the ceiling (" + runs[i][0] + " to " + runs[i][1] + ")");
                if (i > 0)
                    TestAssert.That(runs[i][0] - runs[i - 1][1] >= ContactRule.GraceSeconds, name + ": the group moves for at least the grace between halts (" + runs[i - 1][1] + " to " + runs[i][0] + ")");
            }
        }

        private static void ANonFiniteClockLeavesTheContactClockAlone()
        {
            var clock = default(ContactClock);
            ContactRule.Step(ref clock, true, 100f);
            TestAssert.That(!ContactRule.Step(ref clock, true, float.NaN), "a NaN clock reads as no contact");
            TestAssert.That(!ContactRule.Step(ref clock, false, float.PositiveInfinity), "an infinite clock reads as no contact");
            TestAssert.Near(clock.Since, 100f, "the running halt is untouched");
            TestAssert.That(float.IsFinite(clock.TailUntil), "the tail is still a number");
            TestAssert.That(ContactRule.Step(ref clock, true, 150f), "the halt carries on afterwards");
            TestAssert.That(!ContactRule.Step(ref clock, true, 192f), "and the ceiling still fires at 192");
        }

        // ---- ApproachProgress

        private static Approach Armed(float distance, float now)
        {
            var approach = default(Approach);
            ApproachProgress.Reset(ref approach, distance, now);
            return approach;
        }

        private static void ApproachArmsOnFirstSight()
        {
            var approach = default(Approach);
            TestAssert.That(!approach.Active, "a new approach is not active");
            TestAssert.That(!ApproachProgress.Stalled(ref approach, 5000f, 100f), "the first call arms and is not stalled");
            TestAssert.That(approach.Active, "now armed");
            TestAssert.Near(approach.Best, 5000f, "at the current distance");
            TestAssert.Near(approach.StartDistance, 5000f, "which is also where it started");
            TestAssert.Near(approach.Since, 100f, "at the current second");
            TestAssert.Near(approach.StartAt, 100f, "which is also when it started");
        }

        private static void ApproachStallsAfterTheWindow()
        {
            var approach = Armed(5000f, 100f);
            TestAssert.That(!ApproachProgress.Stalled(ref approach, 5000f, 100f + 179f), "no gain for 179 s is not stalled");
            TestAssert.That(ApproachProgress.Stalled(ref approach, 5000f, 100f + 180f), "no gain for 180 s is stalled");
            var creeping = Armed(5000f, 100f);
            TestAssert.That(ApproachProgress.Stalled(ref creeping, 4851f, 280f), "a gain of 149 m is not progress");
            TestAssert.Near(creeping.Best, 5000f, "the best distance is untouched by a gain below the minimum");
            TestAssert.Near(ApproachProgress.WindowSeconds, 180f, "the window is 180 s");
            TestAssert.Near(ApproachProgress.MinGainMeters, 150f, "the minimum gain is 150 m");
        }

        private static void AGainResetsTheWindow()
        {
            var approach = Armed(5000f, 100f);
            TestAssert.That(!ApproachProgress.Stalled(ref approach, 4849f, 200f), "a gain of 151 m is progress");
            TestAssert.Near(approach.Best, 4849f, "the best distance follows");
            TestAssert.Near(approach.Since, 200f, "and the window restarts");
            TestAssert.That(!ApproachProgress.Stalled(ref approach, 4849f, 200f + 179f), "179 s after the gain is not stalled");
            TestAssert.That(ApproachProgress.Stalled(ref approach, 4849f, 200f + 180f), "180 s after the gain is");
            var exact = Armed(5000f, 100f);
            TestAssert.That(!ApproachProgress.Stalled(ref exact, 4850f, 200f), "a gain of exactly 150 m is progress");
            TestAssert.Near(exact.Since, 200f, "and restarts the window");
        }

        private static void SlowMoversThatGainAreNotStalled()
        {
            // The gain is measured against the best distance, not step by step: 151 m in 179 s is a slow mover, not a stall.
            var approach = Armed(5000f, 100f);
            TestAssert.That(!ApproachProgress.Stalled(ref approach, 4950f, 190f), "100 m by 190 s: nothing yet");
            TestAssert.That(!ApproachProgress.Stalled(ref approach, 4849f, 279f), "151 m by 279 s: progress, not stalled");
            TestAssert.That(!ApproachProgress.Stalled(ref approach, 4849f, 279f + 179f), "and a fresh window from there");
            TestAssert.That(ApproachProgress.Stalled(ref approach, 4849f, 279f + 180f), "which runs out");
        }

        private static void ArrivedIsNeverStalled()
        {
            var approach = Armed(390f, 100f);
            TestAssert.That(!ApproachProgress.Stalled(ref approach, 390f, 100000f), "a group within 400 m is never stalled");
            var closing = Armed(5000f, 100f);
            TestAssert.That(!ApproachProgress.Stalled(ref closing, 400f, 150f), "exactly 400 m is arrived");
            TestAssert.That(!ApproachProgress.Stalled(ref closing, 400f, 100000f), "and stays arrived however long it sits");
            TestAssert.Near(ApproachProgress.ArrivedMeters, 400f, "arrived is 400 m");
        }

        private static void ANonFiniteDistanceDoesNotResetTheApproach()
        {
            var cold = default(Approach);
            TestAssert.That(!ApproachProgress.Stalled(ref cold, float.NaN, 100f), "a NaN distance is not stalled");
            TestAssert.That(!cold.Active, "and does not arm an approach at a made-up distance");
            var approach = Armed(5000f, 100f);
            TestAssert.That(!ApproachProgress.Stalled(ref approach, float.NaN, 150f), "a NaN distance on a running approach is not stalled");
            TestAssert.That(!ApproachProgress.Stalled(ref approach, float.PositiveInfinity, 160f), "nor an infinite one");
            TestAssert.Near(approach.Best, 5000f, "the best distance is not wiped");
            TestAssert.Near(approach.Since, 100f, "the window is not restarted");
            TestAssert.That(ApproachProgress.Stalled(ref approach, 5000f, 280f), "the group is still stalled when its window runs out");
            var moving = Armed(5000f, 100f);
            ApproachProgress.Stalled(ref moving, float.NaN, 150f);
            TestAssert.That(!ApproachProgress.Stalled(ref moving, 4000f, 160f), "a real gain after the blip still counts");
            TestAssert.Near(moving.Best, 4000f, "the best distance follows the real distance");
        }

        private static void ResetRearmsAfterAStageChange()
        {
            var approach = Armed(5000f, 100f);
            TestAssert.That(ApproachProgress.Stalled(ref approach, 5000f, 300f), "stalled by 300 s");
            ApproachProgress.Reset(ref approach, 5000f, 300f);
            TestAssert.That(!ApproachProgress.Stalled(ref approach, 5000f, 300f + 179f), "a stage change restarts the window");
            TestAssert.Near(approach.StartAt, 300f, "and the rate");
        }

        private static void EtaIsUnknownUntilThereIsARate()
        {
            var approach = Armed(5000f, 100f);
            TestAssert.Eq(ApproachProgress.EtaSeconds(approach, 4400f, 129.9f), -1, "unknown before 30 s");
            TestAssert.Eq(ApproachProgress.EtaSeconds(approach, 4400f, 130f), 220, "600 m in 30 s is 20 m/s: 4,400 m takes 220 s");
            TestAssert.Eq(ApproachProgress.EtaSeconds(approach, 4951f, 200f), -1, "unknown below a 50 m gain");
            TestAssert.Eq(ApproachProgress.EtaSeconds(approach, 4950f, 200f), 3600, "a 50 m gain is a rate, capped at an hour");
            TestAssert.Eq(ApproachProgress.EtaSeconds(approach, 0f, 400f), 0, "arrived reads zero seconds, a true zero");
            TestAssert.Eq(ApproachProgress.EtaSeconds(approach, -5000f, 400f), 0, "a negative distance cannot give a negative ETA");
            TestAssert.Eq(ApproachProgress.EtaSeconds(approach, float.NaN, 400f), -1, "unknown for a NaN distance");
            TestAssert.Eq(ApproachProgress.EtaSeconds(approach, 4000f, float.NaN), -1, "unknown for a NaN clock");
            TestAssert.Eq(ApproachProgress.EtaSeconds(default(Approach), 4000f, 400f), -1, "unknown for an approach never armed");
            TestAssert.Eq(ApproachProgress.EtaSeconds(approach, 6000f, 400f), -1, "a group moving away has no ETA");
        }

        private static void ClosingNeedsEvidence()
        {
            var approach = Armed(5000f, 100f);
            TestAssert.That(!ApproachProgress.Closing(approach, 5000f, 100f), "a group just staged is not closing yet");
            TestAssert.That(!ApproachProgress.Closing(approach, 5000f, 130f), "nor 30 s later if it has not moved");
            TestAssert.That(ApproachProgress.Closing(approach, 600f, 100f), "within 600 m counts as moving");
            TestAssert.That(!ApproachProgress.Closing(approach, 600.1f, 100f), "just outside 600 m does not");
            ApproachProgress.Stalled(ref approach, 4800f, 200f);
            TestAssert.That(ApproachProgress.Closing(approach, 4800f, 200f), "a gain of 200 m counts as closing");
            TestAssert.That(ApproachProgress.Closing(approach, 4800f, 259f), "59 s after the gain it still does");
            TestAssert.That(!ApproachProgress.Closing(approach, 4800f, 260f), "60 s after the gain it does not");
            TestAssert.That(!ApproachProgress.Closing(default(Approach), 100f, 100f), "an approach never armed is not closing");
            TestAssert.That(!ApproachProgress.Closing(approach, float.NaN, 200f), "an unknown distance is not closing");
            TestAssert.Near(ApproachProgress.NearMeters, 600f, "near is 600 m");
            TestAssert.Near(ApproachProgress.ClosingWindowSeconds, 60f, "a gain counts for 60 s");
        }

        private static void MeasuredLeadsFromSliceZero()
        {
            // Terminal Control, Boscali g7: 879 m from its Line point at t=301, 4 m at t=361, then it held and followed a drifting point to 13 m and 28 m.
            var lead = Armed(879f, 301f);
            TestAssert.That(!ApproachProgress.Stalled(ref lead, 4f, 361f), "a lead that reached its line point is not stalled");
            TestAssert.That(ApproachProgress.Closing(lead, 4f, 361f), "and reads as moving");
            TestAssert.Eq(ApproachProgress.EtaSeconds(lead, 4f, 361f), 0, "with nothing left to cover");
            TestAssert.That(!ApproachProgress.Stalled(ref lead, 28f, 421f), "holding the point 28 m off later is not stalled");
            var idle = Armed(879f, 301f);
            TestAssert.That(!ApproachProgress.Stalled(ref idle, 879f, 480f), "the same group that never moved is not stalled at 179 s");
            TestAssert.That(ApproachProgress.Stalled(ref idle, 879f, 481f), "and is at 180 s");
            // Terminal Control, Boscali g2: the best closer covered 1,282 m and had 3,904 m left.
            var g2 = Armed(5186f, 100f);
            TestAssert.That(!ApproachProgress.Stalled(ref g2, 3904f, 220f), "a convoy that closed 1,282 m in 120 s is not stalled");
            TestAssert.That(ApproachProgress.Closing(g2, 3904f, 220f), "it is closing");
            TestAssert.Eq(ApproachProgress.EtaSeconds(g2, 3904f, 220f), 365, "and would arrive in about six minutes");
        }

        // ---- SlotSearch

        private static void SlotSearchOffsets()
        {
            TestAssert.Eq(SlotSearch.Candidates, 9, "nine candidates");
            var seen = new HashSet<string>();
            for (int i = 0; i < SlotSearch.Candidates; i++)
            {
                TestAssert.That(SlotSearch.Offset(i, out float along, out float back), "candidate " + i + " exists");
                TestAssert.That(back >= 0f, "candidate " + i + " is never forward of the staged point");
                TestAssert.That(Math.Abs(along) <= 240f && back <= 480f, "candidate " + i + " stays within 480 m");
                TestAssert.That(seen.Add(along + "/" + back), "candidate " + i + " is not a repeat");
                if (i == 0) TestAssert.That(along == 0f && back == 0f, "the first candidate is the staged point itself");
            }
            TestAssert.That(seen.Contains("120/0") && seen.Contains("-120/0") && seen.Contains("240/0") && seen.Contains("-240/0"), "along the front by 120 and 240 m, both ways");
            TestAssert.That(seen.Contains("0/120") && seen.Contains("0/240") && seen.Contains("0/360") && seen.Contains("0/480"), "back toward the friendly side by 120 to 480 m");
            TestAssert.That(!SlotSearch.Offset(SlotSearch.Candidates, out float pastAlong, out float pastBack), "index 9 refuses");
            TestAssert.That(pastAlong == 0f && pastBack == 0f, "a refusal returns no offset");
            TestAssert.That(!SlotSearch.Offset(-1, out float lowAlong, out float lowBack), "index -1 refuses");
            TestAssert.That(lowAlong == 0f && lowBack == 0f, "a refusal returns no offset");
        }

        // ---- FrozenClock (a group none of whose members can be placed is stalled after two minutes)

        private static void FrozenClockStallsAfterTwoMinutesAndAPlacedPassClearsIt()
        {
            float since = 0f;
            TestAssert.That(!FrozenClock.Step(ref since, true, 100f), "the first frozen pass only starts the clock");
            TestAssert.Near(since, 100f, "and stamps the second it began");
            TestAssert.That(!FrozenClock.Step(ref since, true, 219.9f), "119.9 s frozen is not yet a stall");
            TestAssert.That(FrozenClock.Step(ref since, true, 220f), "120 s frozen is a stall");
            TestAssert.That(FrozenClock.Step(ref since, true, 400f), "and stays one while the freeze lasts");
            TestAssert.That(!FrozenClock.Step(ref since, false, 401f) && since == 0f, "one pass with a member placed clears the clock");
            TestAssert.That(!FrozenClock.Step(ref since, true, 402f), "a later freeze starts again from that pass");
            TestAssert.Near(since, 402f, "not from the old one");
            TestAssert.Near(FrozenClock.Seconds, 120f, "two minutes");
        }

        private static void FrozenClockIgnoresAClockItCannotRead()
        {
            float since = 50f;
            TestAssert.That(!FrozenClock.Step(ref since, true, float.NaN) && since == 0f, "a NaN clock clears the freeze instead of storing the NaN");
            since = float.NaN;
            TestAssert.That(!FrozenClock.Step(ref since, true, 100f), "a NaN start is a new freeze, not a stall");
            TestAssert.Near(since, 100f, "stamped again");
        }

        // ---- SlotPull (the second tier of the slot search: the line is at water, the group is the only known land)

        private static void SlotPullWalksTowardTheGroupAndStopsAtIt()
        {
            // The group stands 4000 m due east of the staged point.
            for (int step = 0; step < SlotPull.Steps; step++)
            {
                TestAssert.That(SlotPull.Offset(step, 4000f, 0f, out float dx, out float dz), "step " + step + " exists");
                TestAssert.Near(dx, 500f * (step + 1), "step " + step + " is " + 500 * (step + 1) + " m along");
                TestAssert.Near(dz, 0f, "and on the line to the group");
            }
            TestAssert.That(!SlotPull.Offset(SlotPull.Steps, 4000f, 0f, out _, out _), "no step past the last");
            // A group 800 m away at 3-4-5 slope: the second step is clipped to the group itself and there is no third.
            SlotPull.Offset(0, 480f, 640f, out float ax, out float az);
            TestAssert.Near(ax, 300f, "unit vector x times 500"); TestAssert.Near(az, 400f, "unit vector z times 500");
            TestAssert.That(SlotPull.Offset(1, 480f, 640f, out float bx, out float bz), "the clipped step exists");
            TestAssert.Near(bx, 480f, "clipped to the group, x"); TestAssert.Near(bz, 640f, "clipped to the group, z");
            TestAssert.That(!SlotPull.Offset(2, 480f, 640f, out _, out _), "past the group there is nothing to try");
            // A group 300 m away: step 0 is the group itself, and that is all.
            TestAssert.That(SlotPull.Offset(0, 0f, -300f, out float cx, out float cz) && Math.Abs(cx) < 0.01f && Math.Abs(cz + 300f) < 0.01f, "a near group is the one candidate");
            TestAssert.That(!SlotPull.Offset(1, 0f, -300f, out _, out _), "and the only one");
        }

        private static void SlotPullRefusesWhatItCannotMeasure()
        {
            TestAssert.That(!SlotPull.Offset(-1, 4000f, 0f, out _, out _), "step -1 refuses");
            TestAssert.That(!SlotPull.Offset(0, 0.4f, 0.3f, out _, out _), "a group standing on the slot has nowhere to pull to");
            TestAssert.That(!SlotPull.Offset(0, float.NaN, 0f, out _, out _), "NaN refuses");
            TestAssert.That(!SlotPull.Offset(0, float.PositiveInfinity, 0f, out float dx, out float dz) && dx == 0f && dz == 0f, "infinity refuses with no offset");
        }

        // ---- FrontSide (annex 02 G5: an ambiguous side keeps the last good one, then the centroid's, else holds in place)

        private static void FrontSideProbeWinsThenHistoryThenCentroid()
        {
            // The normal is (0, 1); the friendly side was (0, 1) before.
            TestAssert.Near(FrontSide.Decide(1f, true, 0f, -1f, 0f, 1f, true, 0f, -900f), 1f, "a clear probe beats history and the centroid");
            TestAssert.Near(FrontSide.Decide(-1f, false, 0f, 0f, 0f, 1f, true, 0f, 900f), -1f, "a clear probe, minus side");
            TestAssert.Near(FrontSide.Decide(0f, true, 0f, 1f, 0f, 1f, true, 0f, -900f), 1f, "no probe: the last good side wins over the centroid");
            TestAssert.Near(FrontSide.Decide(0f, true, 0f, 1f, 0f, -1f, false, 0f, 0f), -1f, "the front turned round, so the normal flipped: the same physical side is now minus");
            TestAssert.Near(FrontSide.Decide(0f, true, 0.05f, 1f, 1f, 0f, true, 450f, 0f), 1f, "history nearly across the new normal is not used; the centroid is");
            TestAssert.Near(FrontSide.Decide(0f, false, 0f, 0f, 0f, 1f, true, 0f, 400f), 1f, "centroid 400 m on the plus side");
            TestAssert.Near(FrontSide.Decide(0f, false, 0f, 0f, 0f, 1f, true, 0f, -400f), -1f, "centroid 400 m on the minus side");
            TestAssert.Near(FrontSide.Decide(0f, false, 0f, 0f, 0f, 1f, true, 300f, 300f), 1f, "only the part along the normal counts");
        }

        private static void FrontSideNeverGuessesWhenNothingIsKnown()
        {
            TestAssert.Near(FrontSide.Decide(0f, false, 0f, 0f, 0f, 1f, true, 0f, 299f), 0f, "a centroid 299 m from the line says nothing");
            TestAssert.Near(FrontSide.Decide(0f, false, 0f, 0f, 0f, 1f, false, 0f, 5000f), 0f, "a Withdraw never uses the centroid");
            TestAssert.Near(FrontSide.Decide(0f, false, 0f, 0f, 0f, 1f, true, float.NaN, float.NaN), 0f, "a NaN offset says nothing");
            TestAssert.Near(FrontSide.Decide(float.NaN, true, float.NaN, float.NaN, 0f, 1f, true, 0f, 0f), 0f, "NaN everywhere holds in place");
            TestAssert.Near(FrontSide.Decide(0f, true, 0f, 1f, float.NaN, float.NaN, true, 0f, 900f), 0f, "a NaN normal holds in place");
        }

        // ---- GroupLedger

        private static void LedgerCountsOnlyTakenOverVehiclesAsGone()
        {
            TestAssert.Eq(GroupLedger.FormedAfterLoss(6, true), 6, "a destroyed vehicle stays a casualty");
            TestAssert.Eq(GroupLedger.FormedAfterLoss(6, false), 5, "a vehicle taken over leaves the formation");
            TestAssert.Eq(GroupLedger.FormedAfterLoss(0, false), 0, "never below zero");
            TestAssert.Eq(GroupLedger.FormedAfterLoss(-2, false), 0, "a bad count reads zero");
            TestAssert.Eq(GroupLedger.FormedAfterLoss(-2, true), 0, "a bad count reads zero when destroyed too");

            // With FrontlineTactics.ShouldWithdraw: six formed, four of them gone, two alive.
            int formed = 6;
            for (int i = 0; i < 4; i++) formed = GroupLedger.FormedAfterLoss(formed, false);
            TestAssert.That(!FrontlineTactics.ShouldWithdraw(2, formed), "four of six commanded away does not make the other two withdraw");
            formed = 6;
            for (int i = 0; i < 4; i++) formed = GroupLedger.FormedAfterLoss(formed, true);
            TestAssert.That(FrontlineTactics.ShouldWithdraw(2, formed), "four of six destroyed does");
        }

        // ---- GroundOpReport and GroundMark

        private static void AnUnavailableReportIsUnknownEverywhere()
        {
            TestAssert.Eq(GroundOpReport.Unknown, -1, "unknown is -1");
            int checkedReasons = 0;
            foreach (GroundReason reason in Enum.GetValues(typeof(GroundReason)))
            {
                if (reason == GroundReason.Ready) continue;
                GroundOpReport report = GroundOpReport.Unavailable(reason);
                TestAssert.Eq(report.Reason, reason, "the reason is kept: " + reason);
                TestAssert.That(!report.Steering, "not steering: " + reason);
                bool allUnknown = report.Groups == -1 && report.Vehicles == -1 && report.Formed == -1 &&
                    report.Rally == -1 && report.Line == -1 && report.Assault == -1 && report.Contact == -1 && report.Withdraw == -1 &&
                    report.Moving == -1 && report.Steered == -1 && report.OnSlot == -1 && report.Stalled == -1 && report.EtaSeconds == -1 &&
                    report.NearestToFixMeters == -1f;
                TestAssert.That(allUnknown, "every number is -1, never a confident zero: " + reason);
                checkedReasons++;
            }
            TestAssert.Eq(checkedReasons, 8, "all eight non-ready reasons were checked");
            var ready = new GroundOpReport { Reason = GroundReason.Ready, Groups = 2 };
            TestAssert.That(ready.Steering, "READY is steering");
        }

        private static void MarksKeepWhatTheyAreGiven()
        {
            var mark = new GroundMark(1500f, -2500f, GroundStage.Assault, 4);
            TestAssert.Near(mark.X, 1500f, "x");
            TestAssert.Near(mark.Z, -2500f, "z");
            TestAssert.Eq(mark.Stage, GroundStage.Assault, "stage");
            TestAssert.Eq(mark.Alive, (byte)4, "alive");
        }
    }
}
