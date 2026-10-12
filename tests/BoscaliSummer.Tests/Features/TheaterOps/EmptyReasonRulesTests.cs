using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Modules.TheaterOps.Domain;

namespace BoscaliSummer.Tests.Features.TheaterOps
{
    /// <summary>Why the deck is empty, in the order a pilot can act on it (annex 01 E3, design N-17).</summary>
    internal static class EmptyReasonRulesTests
    {
        public static void Run()
        {
            ACardMeansNoReason();
            EachPriorityStepBeatsTheNext();
            NoLiveCandidateIsNoFronts();
            SteeringRejectsNameTheGroundReason();
            UnmappedGroundReasonsFallThrough();
            AnOperationExplainsAnOtherwiseEmptyDeck();
            TheDominantRejectNamesTheReason();
            TiesFollowTheListedOrder();
            VetoCarriesItsCountdown();
        }

        private static EmptyInputs Quiet() => new EmptyInputs
        {
            Healthy = true, HasTerritory = true, OperationLive = false, MissionHasGroundWar = true, SecondsToFirstDeal = 0f,
            GroundReason = GroundReason.Ready, DeckCount = 0, Tally = new RejectTally { Candidates = 4 },
        };

        private static void Is(EmptyInputs i, TheaterEmptyReason want, string label, float veto = 0f, float seconds = 0f)
        {
            TheaterEmptyReason got = EmptyReasonRules.Decide(i, veto, out float gotSeconds);
            TestAssert.That(got == want, label + " (got " + got + ", want " + want + ")");
            TestAssert.Near(gotSeconds, seconds, label + " seconds");
        }

        private static void ACardMeansNoReason()
        {
            EmptyInputs i = Quiet();
            i.DeckCount = 1; i.Healthy = false; i.SecondsToFirstDeal = 5f;
            Is(i, TheaterEmptyReason.None, "a deck with a card needs no reason, however sick the rest looks");
        }

        private static void EachPriorityStepBeatsTheNext()
        {
            EmptyInputs i = Quiet();
            i.Healthy = false; i.SecondsToFirstDeal = 9f; i.HasTerritory = false; i.MissionHasGroundWar = false;
            Is(i, TheaterEmptyReason.StaffFault, "a faulting staff is named first");
            i.Healthy = true;
            Is(i, TheaterEmptyReason.FirstReview, "then the first review, with its countdown", 0f, 9f);
            i.SecondsToFirstDeal = 0f;
            Is(i, TheaterEmptyReason.NoTerritoryField, "then a missing control field");
            i.HasTerritory = true;
            Is(i, TheaterEmptyReason.NoGroundWar, "then a mission with no ground war");
            i.MissionHasGroundWar = true;
            Is(i, TheaterEmptyReason.NoFronts, "with nothing else wrong and no rejects the answer is no fronts");
        }

        private static void NoLiveCandidateIsNoFronts()
        {
            EmptyInputs i = Quiet();
            i.Tally = new RejectTally { Candidates = 0 };
            Is(i, TheaterEmptyReason.NoFronts, "no candidate at all");
            i.Tally = new RejectTally { Candidates = 3, Gone = 2, BadFix = 1, Vetoed = 5 };
            Is(i, TheaterEmptyReason.NoFronts, "every candidate gone or malformed is no fronts, whatever else was tallied");
            i.Tally = new RejectTally { Candidates = 3, Gone = 2, Vetoed = 1 };
            Is(i, TheaterEmptyReason.CalledOff, "one live candidate that was vetoed is a called-off front");
        }

        private static void SteeringRejectsNameTheGroundReason()
        {
            EmptyInputs i = Quiet();
            i.Tally = new RejectTally { Candidates = 4, NoSteering = 3, HeldQuiet = 1 };
            i.GroundReason = GroundReason.TacticsOff;
            Is(i, TheaterEmptyReason.TacticsOff, "frontline tactics off");
            i.GroundReason = GroundReason.RtsOwnsOrders;
            Is(i, TheaterEmptyReason.RtsOwnsOrders, "Ground Control RTS owns the orders");
            i.GroundReason = GroundReason.Unsupported;
            Is(i, TheaterEmptyReason.NoGroundWar, "an unsupported mission is no ground war");
            i.GroundReason = GroundReason.NoVehicles;
            Is(i, TheaterEmptyReason.NoGroundWar, "no vehicles is no ground war");
            i.GroundReason = GroundReason.NoFront;
            Is(i, TheaterEmptyReason.NoFronts, "no front is no fronts");
        }

        private static void UnmappedGroundReasonsFallThrough()
        {
            EmptyInputs i = Quiet();
            i.Tally = new RejectTally { Candidates = 4, NoSteering = 3, HeldQuiet = 1 };
            foreach (GroundReason reason in new[] { GroundReason.Ready, GroundReason.Pending, GroundReason.NotHost, GroundReason.Unknown })
            {
                i.GroundReason = reason;
                Is(i, TheaterEmptyReason.FrontsQuiet, "ground reason " + reason + " says nothing, so the next reject speaks");
            }
            i.Tally = new RejectTally { Candidates = 4, NoSteering = 3 };
            i.GroundReason = GroundReason.Pending;
            Is(i, TheaterEmptyReason.NoFronts, "nothing else to say is no fronts");
            i.Tally = new RejectTally { Candidates = 4, NoSteering = 2, Vetoed = 3 };
            i.GroundReason = GroundReason.TacticsOff;
            Is(i, TheaterEmptyReason.CalledOff, "steering rejects that do not dominate are not blamed", 12f, 12f);
        }

        private static void AnOperationExplainsAnOtherwiseEmptyDeck()
        {
            EmptyInputs i = Quiet();
            i.OperationLive = true;
            Is(i, TheaterEmptyReason.OperationLive, "a live operation with nothing else on offer");
            i.Tally = new RejectTally { Candidates = 4, HeldQuiet = 2 };
            Is(i, TheaterEmptyReason.OperationLive, "a live operation is named before the dominant reject");
            i.Tally = new RejectTally { Candidates = 4, NoSteering = 2 };
            i.GroundReason = GroundReason.TacticsOff;
            Is(i, TheaterEmptyReason.TacticsOff, "steering that dominates is named before the operation");
            i.Tally = new RejectTally { Candidates = 0 };
            Is(i, TheaterEmptyReason.NoFronts, "no live front at all is named before the operation");
        }

        private static void TheDominantRejectNamesTheReason()
        {
            EmptyInputs i = Quiet();
            i.Tally = new RejectTally { Candidates = 9, Vetoed = 2, NoCarrier = 1, NoPresence = 1, PostureRatio = 1, HeldQuiet = 1 };
            Is(i, TheaterEmptyReason.CalledOff, "vetoes dominate", 30f, 30f);
            i.Tally = new RejectTally { Candidates = 9, Vetoed = 1, NoCarrier = 2, PostureRatio = 1, HeldQuiet = 1 };
            Is(i, TheaterEmptyReason.NoFriendlyGroundNearFront, "no carrier dominates");
            i.Tally = new RejectTally { Candidates = 9, Vetoed = 1, NoPresence = 2, PostureRatio = 1, HeldQuiet = 1 };
            Is(i, TheaterEmptyReason.NoFriendlyGroundNearFront, "no presence dominates");
            i.Tally = new RejectTally { Candidates = 9, Vetoed = 1, NoCarrier = 1, NoPresence = 1, PostureRatio = 1, HeldQuiet = 1 };
            Is(i, TheaterEmptyReason.NoFriendlyGroundNearFront, "no carrier and no presence together outweigh single rejects");
            i.Tally = new RejectTally { Candidates = 9, PostureRatio = 3, HeldQuiet = 2 };
            Is(i, TheaterEmptyReason.PostureTooCautious, "the posture ratio dominates");
            i.Tally = new RejectTally { Candidates = 9, PostureRatio = 1, HeldQuiet = 2 };
            Is(i, TheaterEmptyReason.FrontsQuiet, "quiet held fronts dominate");
        }

        private static void TiesFollowTheListedOrder()
        {
            EmptyInputs i = Quiet();
            i.Tally = new RejectTally { Candidates = 9, Vetoed = 1, NoCarrier = 1, PostureRatio = 1, HeldQuiet = 1 };
            Is(i, TheaterEmptyReason.CalledOff, "a four-way tie goes to the veto", 20f, 20f);
            i.Tally = new RejectTally { Candidates = 9, NoCarrier = 1, PostureRatio = 1, HeldQuiet = 1 };
            Is(i, TheaterEmptyReason.NoFriendlyGroundNearFront, "then to the missing ground");
            i.Tally = new RejectTally { Candidates = 9, PostureRatio = 1, HeldQuiet = 1 };
            Is(i, TheaterEmptyReason.PostureTooCautious, "then to the posture");
            i.Tally = new RejectTally { Candidates = 9, NoSteering = 1, Vetoed = 1 };
            i.GroundReason = GroundReason.RtsOwnsOrders;
            Is(i, TheaterEmptyReason.RtsOwnsOrders, "a tie between steering and the rest goes to steering");
        }

        private static void VetoCarriesItsCountdown()
        {
            EmptyInputs i = Quiet();
            i.Tally = new RejectTally { Candidates = 2, Vetoed = 2 };
            Is(i, TheaterEmptyReason.CalledOff, "called off with the seconds left", 45.5f, 45.5f);
            Is(i, TheaterEmptyReason.CalledOff, "a negative veto countdown reads 0", -3f, 0f);
            Is(i, TheaterEmptyReason.CalledOff, "a NaN veto countdown reads 0", float.NaN, 0f);
            i.SecondsToFirstDeal = float.NaN;
            Is(i, TheaterEmptyReason.CalledOff, "a NaN first-deal countdown is not a first review", 5f, 5f);
        }
    }
}
