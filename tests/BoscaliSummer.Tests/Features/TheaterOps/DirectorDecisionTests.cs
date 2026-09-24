using System.Collections.Generic;
using BoscaliSummer.Features.TheaterOps.Domain;

namespace BoscaliSummer.Tests.Features.TheaterOps
{
    internal static class DirectorDecisionTests
    {
        private const float Overhead = 2f;
        private const float Wave = 3f;

        public static void Run()
        {
            WeaknessWinsTheAttack();
            SizingGrowsWithTheDefense();
            FieldworksChangeTargetAndWaveBudget();
            TheCommittedTargetSurvivesBetterOffers();
            HoldReserveAndCapacityShowInTheLog();
            ChestCapBlocksTheOpening();
            ThreatOwnsTheEffortAtLowStance();
            OpportunityKeepsItAtHighStance();
            AnEmptyTheaterStaysQuiet();
            GroundUnderAttackIsNotAttackedTwice();
            EffortNamesTheLabelNotTheKey();
            OutcomeWordsMatchTheBoard();
        }

        private static DirectorAssessment Assess(
            List<ObjectiveRead> objectives, InfluenceState influence, float funds = 50f,
            int active = 0, string committed = null, int held = 9,
            string launched = null, string effort = null, List<string> running = null) =>
            new DirectorAssessment(
                objectives, influence, funds, Overhead, Wave, active, 2,
                committed, held, launched, effort, running);

        private static void GroundUnderAttackIsNotAttackedTwice()
        {
            var influence = new InfluenceState();
            var objectives = new List<ObjectiveRead>
            {
                new ObjectiveRead("alpha", "Alpha", 1, 0),
                new ObjectiveRead("beta", "Beta", 2, 0),
            };
            var running = new List<string> { "alpha" };
            DirectorOrders orders = DirectorDecision.Review(
                Assess(objectives, influence, running: running));
            TestAssert.That(orders.TargetKey == "beta", "a running target is skipped for the opening");
        }

        private static void WeaknessWinsTheAttack()
        {
            var influence = new InfluenceState();
            var objectives = new List<ObjectiveRead>
            {
                new ObjectiveRead("stronghold", "Stronghold", 12, 0),
                new ObjectiveRead("outpost", "Outpost", 2, 0),
            };
            DirectorOrders orders = DirectorDecision.Review(Assess(objectives, influence));
            TestAssert.That(orders.OpenOffensive, "weakness opens an offensive");
            TestAssert.That(orders.TargetKey == "outpost", "the weakest objective is the target");
        }

        private static void SizingGrowsWithTheDefense()
        {
            TestAssert.That(DirectorDecision.SizeWaves(0) == 1, "an empty objective rates one wave");
            TestAssert.That(DirectorDecision.SizeWaves(3) == 2, "three defenders rate two waves");
            TestAssert.That(DirectorDecision.SizeWaves(15) == 6, "fifteen defenders rate the ceiling");
            TestAssert.That(DirectorDecision.SizeWaves(99) == 6, "sizing never leaves the ceiling");
            TestAssert.That(DirectorDecision.SizeWaves(-4) == 1, "a negative count rates the floor");
        }

        private static void FieldworksChangeTargetAndWaveBudget()
        {
            var objectives = new List<ObjectiveRead>
            {
                new ObjectiveRead("fort", "Fort", 0, 0, hostileFieldworks: 4),
                new ObjectiveRead("road", "Road", 2, 0),
            };
            DirectorOrders safer = DirectorDecision.Review(Assess(objectives, new InfluenceState()));
            TestAssert.That(safer.TargetKey == "road", "observed trench defenders make the open road preferable");

            objectives.RemoveAt(1);
            DirectorOrders assault = DirectorDecision.Review(Assess(objectives, new InfluenceState()));
            TestAssert.That(assault.TargetKey == "fort" && assault.Waves == 3,
                "a fortified objective takes extra funded waves within the ceiling");
            TestAssert.That(Join(assault).Contains("FORTIFIED APPROACH"),
                "the staff explains why the fortified attack needs more waves");

            var defense = new List<ObjectiveRead>
            {
                new ObjectiveRead("home", "Home", 0, 0, friendlyFieldworks: 2,
                    suppressedFieldworks: 2),
            };
            DirectorOrders underFire = DirectorDecision.Review(Assess(defense, new InfluenceState()));
            TestAssert.That(underFire.DefenseKey == null,
                "damage alone without a credible raid stays below the defense threshold");
            defense[0] = new ObjectiveRead("home", "Home", 0, 0, friendlyFieldworks: 8,
                suppressedFieldworks: 4);
            DirectorOrders bombardment = DirectorDecision.Review(Assess(defense, new InfluenceState()));
            TestAssert.That(bombardment.DefenseKey == "home",
                "a suppressed trench stays a defense even when its surviving garrison outnumbers contact");
        }

        private static void TheCommittedTargetSurvivesBetterOffers()
        {
            var influence = new InfluenceState();
            var objectives = new List<ObjectiveRead>
            {
                new ObjectiveRead("alpha", "Alpha", 4, 0),
                new ObjectiveRead("beta", "Beta", 1, 0),
            };
            DirectorOrders fresh = DirectorDecision.Review(Assess(objectives, influence, committed: "alpha", held: 0));
            TestAssert.That(fresh.TargetKey == "alpha", "a fresh commitment survives a weaker offer");
            DirectorOrders stale = DirectorDecision.Review(Assess(objectives, influence, committed: "alpha", held: 2));
            TestAssert.That(stale.TargetKey == "beta", "an expired commitment yields to weakness");
        }

        private static void HoldReserveAndCapacityShowInTheLog()
        {
            var objectives = new List<ObjectiveRead> { new ObjectiveRead("outpost", "Outpost", 2, 0) };
            var held = new InfluenceState();
            held.SetHold(true, "VIPER-1");
            DirectorOrders hold = DirectorDecision.Review(Assess(objectives, held));
            TestAssert.That(!hold.OpenOffensive && Join(hold).Contains("STANDING ORDER"), "a hold names its order");

            var poor = new InfluenceState();
            DirectorOrders broke = DirectorDecision.Review(Assess(objectives, poor, funds: 1f));
            TestAssert.That(!broke.OpenOffensive && Join(broke).Contains("WAR CHEST"), "an empty chest names itself");

            DirectorOrders full = DirectorDecision.Review(Assess(objectives, new InfluenceState(), active: 2));
            TestAssert.That(!full.OpenOffensive && Join(full).Contains("STAFF AT CAPACITY"), "a full staff says so");
        }

        private static void ChestCapBlocksTheOpening()
        {
            var objectives = new List<ObjectiveRead> { new ObjectiveRead("outpost", "Outpost", 2, 0) };
            var capped = new InfluenceState();
            capped.SetChest(1f, 0f, "VIPER-1");
            DirectorOrders narrow = DirectorDecision.Review(Assess(objectives, capped));
            TestAssert.That(!narrow.OpenOffensive && Join(narrow).Contains("CHEST CAP"),
                "a chest below the opening charge holds the offensive and says so");
        }

        private static void ThreatOwnsTheEffortAtLowStance()
        {
            var influence = new InfluenceState();
            influence.SetStance(0f, "VIPER-1");
            var objectives = new List<ObjectiveRead>
            {
                new ObjectiveRead("home", "Home", 8, 6),
                new ObjectiveRead("far", "Far", 1, 0),
            };
            DirectorOrders orders = DirectorDecision.Review(
                Assess(objectives, influence, launched: "far", effort: "far"));
            TestAssert.That(orders.DefenseKey == "home", "the raid is the defense");
            TestAssert.That(orders.EffortKey == "home" && orders.EffortIsDefense, "threat takes the effort at full defense");
        }

        private static void OpportunityKeepsItAtHighStance()
        {
            var influence = new InfluenceState();
            influence.SetStance(1f, "VIPER-1");
            var objectives = new List<ObjectiveRead>
            {
                new ObjectiveRead("home", "Home", 4, 4),
                new ObjectiveRead("far", "Far", 0, 0),
            };
            DirectorOrders orders = DirectorDecision.Review(
                Assess(objectives, influence, launched: "far", effort: "far"));
            TestAssert.That(orders.EffortKey == "far" && !orders.EffortIsDefense, "opportunity keeps the effort at full attack");
        }

        private static void AnEmptyTheaterStaysQuiet()
        {
            DirectorOrders orders = DirectorDecision.Review(
                Assess(new List<ObjectiveRead>(), new InfluenceState(), effort: "far"));
            TestAssert.That(!orders.OpenOffensive && !orders.ClearEffort && orders.Log.Length == 0,
                "an uninspectable theater changes nothing and says nothing");
        }

        private static void EffortNamesTheLabelNotTheKey()
        {
            var influence = new InfluenceState();
            influence.SetStance(0f, "VIPER-1");
            var objectives = new List<ObjectiveRead>
            {
                new ObjectiveRead("home_depot", "Home Depot", 8, 6),
                new ObjectiveRead("far", "Far", 1, 0),
            };
            DirectorOrders orders = DirectorDecision.Review(
                Assess(objectives, influence, launched: "far", effort: "far"));
            TestAssert.That(orders.EffortKey == "home_depot", "threat takes the effort at full defense");
            TestAssert.That(Join(orders).Contains("EFFORT → HOME DEPOT"), "the effort line names the label, not the key");
        }

        private static void OutcomeWordsMatchTheBoard()
        {
            TestAssert.That(DirectorWords.OutcomeWord(BoscaliSummer.Framework.Contracts.TheaterOperationOutcome.ObjectiveSecured) == "SECURED", "a secured objective reads secured");
            TestAssert.That(DirectorWords.OutcomeWord(BoscaliSummer.Framework.Contracts.TheaterOperationOutcome.ObjectiveLost) == "OBJECTIVE CLOSED", "a lost objective reads closed");
            TestAssert.That(DirectorWords.OutcomeWord(BoscaliSummer.Framework.Contracts.TheaterOperationOutcome.Stalled) == "STALLED", "a stall reads stalled");
            TestAssert.That(DirectorWords.OutcomeWord(BoscaliSummer.Framework.Contracts.TheaterOperationOutcome.CommitmentSpent) == "COMMITMENT SPENT", "spent waves read spent");
            TestAssert.That(DirectorWords.OutcomeWord(BoscaliSummer.Framework.Contracts.TheaterOperationOutcome.Cancelled) == "CANCELLED", "a stand-down reads cancelled");
            TestAssert.That(DirectorWords.OutcomeWord(BoscaliSummer.Framework.Contracts.TheaterOperationOutcome.None) == "CONCLUDED", "an unmarked end reads concluded");
        }

        private static string Join(DirectorOrders orders) => string.Join(" ", orders.Log);
    }
}
