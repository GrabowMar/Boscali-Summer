using System;
using System.Collections.Generic;
using BoscaliSummer.Modules.TheaterOps.Domain;
using BoscaliSummer.Core.Contracts;

namespace BoscaliSummer.Tests.Features.TheaterOps
{
    /// <summary>The staff's eligibility rules and ranking (annex 01 E3): who may be offered what, and why a front was not.</summary>
    internal static class LivingWarRulesTests
    {
        public static void Run()
        {
            ALeadingHeldFrontAndMissionFrontsRank();
            AssaultAndReconNeedACarrier();
            DefendNeedsNoCarrierAndABaseIsItsOwnPresence();
            SteeringOffRemovesAssaultAndRecon();
            GoneAndVetoedFrontsScoreNothing();
            AssaultNeedsTheRatioTheCaptionPrints();
            UnobservedFrontsAreReconAndTalliesCountEveryReject();
            ChooseIsBoundedAndRanked();
            RadiiAndReachAreTheFrozenConstants();
            UrgentDefenceIsTheBestUncardedBaseDefence();
            FocusRolesFollowTheOperationKind();
        }

        private static readonly WarCapability Steering = new WarCapability(true);
        private static readonly WarCapability NoSteering = new WarCapability(false);

        // key, source, held, observed, live, pressure, near friendly, near hostile, hostile air near, reach ground, reach other, reach hostile, carriers.
        private static WarFrontRead Read(string key, FrontSource source, bool held, bool observed, bool live, float pressure,
            int nearFriendly, int nearHostile, int hostileAir, int reachGround, int reachOther, int reachHostile, int carriers) =>
            new WarFrontRead(key, key + " place", 100f, 100f, pressure, nearFriendly, nearHostile, hostileAir, reachGround, reachOther,
                reachHostile, carriers, source, held, observed, live);

        private static List<WarOffer> Ranked(IReadOnlyList<WarFrontRead> fronts, TheaterWarPosture posture, WarCapability cap,
            Func<WarFrontRead, bool> vetoed, out RejectTally tally)
        {
            var into = new List<WarOffer>();
            LivingWarRules.Choose(fronts, posture, cap, vetoed, into, out tally);
            return into;
        }

        private static void ALeadingHeldFrontAndMissionFrontsRank()
        {
            var fronts = new List<WarFrontRead>
            {
                Read("held", FrontSource.Objective, true, true, true, .8f, 3, 5, 0, 3, 0, 5, 3),
                Read("opening", FrontSource.Sector, false, true, true, .3f, 4, 1, 0, 4, 0, 1, 2),
                Read("mission", FrontSource.Objective, false, true, true, .3f, 4, 1, 0, 4, 0, 1, 2),
                Read("empty", FrontSource.Objective, false, false, true, 0f, 0, 0, 0, 0, 0, 0, 0),
            };
            List<WarOffer> offers = Ranked(fronts, TheaterWarPosture.Steady, Steering, null, out RejectTally tally);
            TestAssert.That(offers.Count == 3 && offers[0].Kind == TheaterOpKind.Defend && offers[0].Front.Key == "held",
                "a threatened held objective leads the staff choices");
            TestAssert.That(offers[1].Front.Key == "mission" && offers[2].Front.Key == "opening",
                "mission objectives lead equivalent sector attacks");
            TestAssert.That(tally.Candidates == 4 && tally.NoPresence == 1, "the empty front is a candidate rejected for presence");
        }

        private static void AssaultAndReconNeedACarrier()
        {
            // Ten friendly ships and aircraft are presence but no carrier: ASSAULT and RECON are not offered.
            WarFrontRead assault = Read("a", FrontSource.Sector, false, true, true, .3f, 0, 1, 0, 0, 10, 1, 0);
            WarFrontRead recon = Read("r", FrontSource.Sector, false, false, true, .3f, 0, 0, 0, 0, 10, 0, 0);
            List<WarOffer> offers = Ranked(new[] { assault, recon }, TheaterWarPosture.Steady, Steering, null, out RejectTally tally);
            TestAssert.That(offers.Count == 0, "ten friendly ships and aircraft are not a ground force");
            TestAssert.That(tally.NoCarrier == 2, "both are rejected for the missing carrier");
            WarFrontRead carried = Read("c", FrontSource.Sector, false, true, true, .3f, 0, 1, 0, 2, 0, 1, 2);
            offers = Ranked(new[] { carried }, TheaterWarPosture.Steady, Steering, null, out _);
            TestAssert.That(offers.Count == 1 && offers[0].Kind == TheaterOpKind.Assault, "a carrier makes the same front an assault");
        }

        private static void DefendNeedsNoCarrierAndABaseIsItsOwnPresence()
        {
            WarFrontRead heldNoCarrier = Read("held", FrontSource.Sector, true, true, true, .5f, 2, 3, 0, 0, 2, 3, 0);
            List<WarOffer> offers = Ranked(new[] { heldNoCarrier }, TheaterWarPosture.Steady, Steering, null, out _);
            TestAssert.That(offers.Count == 1 && offers[0].Kind == TheaterOpKind.Defend, "DEFEND is offered with no carriers");
            WarFrontRead ownBase = Read("#b", FrontSource.Base, true, true, true, 1f, 0, 0, 2, 0, 0, 0, 0);
            offers = Ranked(new[] { ownBase }, TheaterWarPosture.Cautious, Steering, null, out RejectTally tally);
            TestAssert.That(offers.Count == 1 && offers[0].Kind == TheaterOpKind.Defend, "a base with a hostile near and no friendly is offered DEFEND");
            TestAssert.That(tally.NoPresence == 0, "the base itself is the presence");
            TestAssert.Near(offers[0].Score, 6f + 2f + 4f + 1f, "a base defence scores 6 + hostile + pressure x 4 + 1 for being a base");
            WarFrontRead quiet = Read("#q", FrontSource.Base, true, true, true, 0f, 0, 0, 0, 0, 0, 0, 0);
            offers = Ranked(new[] { quiet }, TheaterWarPosture.Steady, Steering, null, out tally);
            TestAssert.That(offers.Count == 0 && tally.HeldQuiet == 1, "a held base with nobody near is quiet, not an opening");
        }

        private static void SteeringOffRemovesAssaultAndRecon()
        {
            var fronts = new List<WarFrontRead>
            {
                Read("a", FrontSource.Sector, false, true, true, .3f, 2, 1, 0, 4, 0, 1, 2),
                Read("r", FrontSource.Objective, false, false, true, .3f, 2, 0, 0, 4, 0, 0, 2),
                Read("d", FrontSource.Sector, true, true, true, .3f, 2, 2, 0, 0, 1, 2, 0),
            };
            List<WarOffer> offers = Ranked(fronts, TheaterWarPosture.Steady, NoSteering, null, out RejectTally tally);
            TestAssert.That(offers.Count == 1 && offers[0].Kind == TheaterOpKind.Defend, "without steering only the defence remains");
            TestAssert.That(tally.NoSteering == 2, "and both attacks tally NoSteering");
        }

        private static void GoneAndVetoedFrontsScoreNothing()
        {
            WarFrontRead gone = Read("g", FrontSource.Sector, false, true, false, .3f, 2, 1, 0, 4, 0, 1, 2);
            WarFrontRead vetoed = Read("v", FrontSource.Sector, false, true, true, .3f, 2, 1, 0, 4, 0, 1, 2);
            WarFrontRead bad = Read("b", FrontSource.Sector, false, true, true, 1.5f, 2, 1, 0, 4, 0, 1, 2);
            var nan = new WarFrontRead("n", "n", float.NaN, 0f, .3f, 2, 1, 0, 4, 0, 1, 2, FrontSource.Sector, false, true, true);
            List<WarOffer> offers = Ranked(new[] { gone, vetoed, bad, nan }, TheaterWarPosture.Steady, Steering, f => f.Key == "v", out RejectTally tally);
            TestAssert.That(offers.Count == 0, "gone, vetoed and malformed fronts are never offered");
            TestAssert.That(tally.Gone == 1 && tally.Vetoed == 1 && tally.BadFix == 2, "each is counted under its own reject");
            float score = LivingWarRules.Evaluate(gone, TheaterWarPosture.Steady, Steering, false, out _, out WarReject reject);
            TestAssert.That(score <= 0f && reject == WarReject.Gone, "Evaluate names the reject");
            score = LivingWarRules.Evaluate(vetoed, TheaterWarPosture.Steady, Steering, true, out _, out reject);
            TestAssert.That(score <= 0f && reject == WarReject.Vetoed, "and a veto outranks a good front");
        }

        // TheaterWords.Posture prints PostureRule.Ratio; the assault rule must apply that same number (4 hostile, so ceil(4 x ratio) friendly).
        private static void AssaultNeedsTheRatioTheCaptionPrints()
        {
            foreach (TheaterWarPosture posture in new[] { TheaterWarPosture.Cautious, TheaterWarPosture.Steady, TheaterWarPosture.Bold })
            {
                int needed = (int)Math.Ceiling(4f * PostureRule.Ratio(posture));
                TestAssert.That(Offered(posture, needed - 1) == 0, posture + " refuses an assault one friendly short of its ratio");
                TestAssert.That(Offered(posture, needed) == 1, posture + " assaults at exactly its ratio");
            }
        }

        private static int Offered(TheaterWarPosture posture, int friendly)
        {
            WarFrontRead front = Read("sector", FrontSource.Sector, false, true, true, .3f, friendly, 4, 0, friendly, 0, 4, 1);
            return Ranked(new[] { front }, posture, Steering, null, out _).Count;
        }

        private static void UnobservedFrontsAreReconAndTalliesCountEveryReject()
        {
            WarFrontRead dark = Read("dark", FrontSource.Objective, false, false, true, .25f, 1, 0, 0, 2, 0, 0, 1);
            List<WarOffer> offers = Ranked(new[] { dark }, TheaterWarPosture.Steady, Steering, null, out _);
            TestAssert.That(offers.Count == 1 && offers[0].Kind == TheaterOpKind.Recon, "an unobserved front is a recon");
            TestAssert.Near(offers[0].Score, 1f + 1f + .25f, "worth 1 + 1 for an objective + its pressure");
            WarFrontRead heavy = Read("heavy", FrontSource.Sector, false, true, true, .1f, 1, 9, 0, 1, 0, 9, 1);
            LivingWarRules.Evaluate(heavy, TheaterWarPosture.Steady, Steering, false, out _, out WarReject reject);
            TestAssert.That(reject == WarReject.PostureRatio, "outnumbered is a posture reject");
        }

        private static void ChooseIsBoundedAndRanked()
        {
            var fronts = new List<WarFrontRead>();
            for (int i = 0; i < 12; i++)
                fronts.Add(Read("k" + i.ToString("00"), FrontSource.Sector, false, true, true, .1f, 1, 1, 0, 3, 0, 1, 1));
            List<WarOffer> offers = Ranked(fronts, TheaterWarPosture.Steady, Steering, null, out RejectTally tally);
            TestAssert.That(offers.Count == LivingWarRules.MaximumFronts, "the ranking holds at most MaximumFronts (the deck takes three)");
            TestAssert.That(tally.Candidates == LivingWarRules.MaximumFronts, "and only the first eight fronts are candidates");
            for (int i = 1; i < offers.Count; i++)
                TestAssert.That(offers[i - 1].Score > offers[i].Score || offers[i - 1].Score == offers[i].Score &&
                    string.CompareOrdinal(offers[i - 1].Front.Key, offers[i].Front.Key) < 0, "best first, ties by key");
            LivingWarRules.Choose(null, TheaterWarPosture.Steady, Steering, null, offers, out tally);
            TestAssert.That(offers.Count == 0 && tally.Candidates == 0, "no fronts is an empty ranking");
        }

        private static void UrgentDefenceIsTheBestUncardedBaseDefence()
        {
            var ranked = new List<WarOffer>
            {
                new WarOffer(TheaterOpKind.Assault, Read("a", FrontSource.Sector, false, true, true, .3f, 3, 1, 0, 3, 0, 1, 1), 9f),
                new WarOffer(TheaterOpKind.Defend, Read("#cardless", FrontSource.Base, true, true, true, .5f, 0, 2, 0, 0, 0, 0, 0), 8f),
                new WarOffer(TheaterOpKind.Defend, Read("#carded", FrontSource.Base, true, true, true, .5f, 0, 2, 0, 0, 0, 0, 0), 7f),
                new WarOffer(TheaterOpKind.Defend, Read("held", FrontSource.Sector, true, true, true, .5f, 2, 2, 0, 0, 2, 2, 0), 6f),
            };
            var deck = new List<DeckCard> { new DeckCard { Key = "#carded", Kind = TheaterOpKind.Defend } };
            TestAssert.Eq(LivingWarRules.UrgentDefence(ranked, deck, null), 1, "the best base defence without a card is the urgent one");
            TestAssert.Eq(LivingWarRules.UrgentDefence(ranked, deck, "#cardless"), -1, "not the front of the live operation, and a sector defence is never urgent");
            deck.Add(new DeckCard { Key = "#cardless", Kind = TheaterOpKind.Defend });
            TestAssert.Eq(LivingWarRules.UrgentDefence(ranked, deck, null), -1, "none once every base defence has a card");
            TestAssert.Eq(LivingWarRules.UrgentDefence(null, deck, null), -1, "no ranking is none");
            TestAssert.Eq(LivingWarRules.UrgentDefence(ranked, null, null), 1, "no deck means every base defence is cardless");
        }

        private static void FocusRolesFollowTheOperationKind()
        {
            TestAssert.That(LivingWarRules.RoleOf(TheaterOpKind.Assault) == FocusRole.Assault && LivingWarRules.RoleOf(TheaterOpKind.Defend) == FocusRole.Defend &&
                LivingWarRules.RoleOf(TheaterOpKind.Recon) == FocusRole.Recon, "each operation kind asks its own role of a sortie");
            TestAssert.That(LivingWarRules.RoleOf((TheaterOpKind)9) == FocusRole.None, "an unknown kind asks nothing");
        }

        private static void RadiiAndReachAreTheFrozenConstants()
        {
            TestAssert.Near(LivingWarRules.RadiusOf(TheaterOpKind.Assault), 8000f, "assault zone");
            TestAssert.Near(LivingWarRules.RadiusOf(TheaterOpKind.Defend), 6000f, "defend zone");
            TestAssert.Near(LivingWarRules.RadiusOf(TheaterOpKind.Recon), 10000f, "recon zone");
            TestAssert.Near(LivingWarRules.ReachRadius, GroundTasking.ReachMeters, "the carrier ring is ground's reach constant");
            TestAssert.That(LivingWarRules.MaximumOffers == 3 && LivingWarRules.OperationLimitSeconds == 600f &&
                LivingWarRules.SnapshotFreshSeconds == 15f, "the unchanged constants stay");
        }
    }
}
