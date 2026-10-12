using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Modules.TheaterOps.Domain;

namespace BoscaliSummer.Tests.Features.TheaterOps
{
    /// <summary>Operation endings that can actually happen, in the order the staff tests them (annex 01 E5).</summary>
    internal static class OutcomeRulesTests
    {
        public static void Run()
        {
            GainAndLossThresholds();
            SectorAssaultSustainsBeforeItEnds();
            DefendClearTimeBoundary();
            DefendLostBeatsClearAndNamesTheCause();
            ReconNeedsObservationAndTime();
            CarriersGoneEndsAssaultAndRecon();
            LostForcesEndANonBaseDefend();
            NoForcesNeedsEveryPrecondition();
            ALostFrontEndsAfterItsGrace();
            TheTimeLimitIsTheLastResort();
            ObjectiveStatusComesFirst();
            ReadsAreIgnoredWhenTheFrontIsNotLive();
        }

        private static OutcomeInput Running(TheaterOpKind kind, FrontSource source) => new OutcomeInput
        {
            Kind = kind, Source = source, Found = true, Live = true, Steering = true, HaveReport = true, HasHold = true, CarrierBased = true,
            HoldAtOrigin = 0.1f, InitialHold = 0.1f, Elapsed = 100f, NearFriendly = 2, ReachFriendlyGround = 3, ReachFriendly = 3, InitialGround = 3, Groups = 2,
        };

        private static void Is(OpEnd end, TheaterOutcome outcome, TheaterWhy why, string label)
        {
            TestAssert.That(end.Outcome == outcome && end.Why == why, label + " (got " + end.Outcome + "/" + end.Why + ", want " + outcome + "/" + why + ")");
        }

        private static void GainAndLossThresholds()
        {
            TestAssert.That(OutcomeRules.GainHolds(true, 0.5f, 0.25f), "a gain of exactly 0.25 to 0.5 holds");
            TestAssert.That(!OutcomeRules.GainHolds(true, 0.49f, 0.25f), "a gain of 0.24 does not");
            TestAssert.That(OutcomeRules.GainHolds(true, OutcomeRules.GainHold, 0f), "reaching the hold level from nothing holds");
            TestAssert.That(!OutcomeRules.GainHolds(true, 0.34f, 0f), "below the hold level it does not, however large the gain");
            TestAssert.That(!OutcomeRules.GainHolds(false, 0.9f, 0f), "no hold reading never gains");
            TestAssert.That(!OutcomeRules.GainHolds(true, float.NaN, 0f), "a NaN hold never gains");
            TestAssert.That(OutcomeRules.LossHolds(true, 0.1f, 0.5f), "a fall of 0.4 holds as a loss");
            TestAssert.That(!OutcomeRules.LossHolds(true, 0.2f, 0.5f), "a fall of 0.3 does not");
            TestAssert.That(OutcomeRules.LossHolds(true, -0.5f, 0f), "going 0.5 under the start holds");
            TestAssert.That(!OutcomeRules.LossHolds(false, -0.9f, 0f), "no hold reading never loses");
            TestAssert.That(!OutcomeRules.LossHolds(true, float.NaN, 0f), "a NaN hold never loses");
        }

        private static void SectorAssaultSustainsBeforeItEnds()
        {
            OutcomeInput i = Running(TheaterOpKind.Assault, FrontSource.Sector);
            i.HoldAtOrigin = 0.4f; i.InitialHold = 0.1f;
            i.GainSeconds = 19f;
            Is(OutcomeRules.Evaluate(i), TheaterOutcome.None, TheaterWhy.None, "a gain held 19 s keeps running");
            i.GainSeconds = 0f;
            Is(OutcomeRules.Evaluate(i), TheaterOutcome.None, TheaterWhy.None, "a one-review blip never ends it");
            i.GainSeconds = 20f;
            Is(OutcomeRules.Evaluate(i), TheaterOutcome.Secured, TheaterWhy.FrontAdvanced, "a gain held 20 s secures");
            i.HasHold = false;
            Is(OutcomeRules.Evaluate(i), TheaterOutcome.None, TheaterWhy.None, "without a hold reading the timer means nothing");
            OutcomeInput loss = Running(TheaterOpKind.Assault, FrontSource.Sector);
            loss.LossSeconds = 19f;
            Is(OutcomeRules.Evaluate(loss), TheaterOutcome.None, TheaterWhy.None, "a loss held 19 s keeps running");
            loss.LossSeconds = 20f;
            Is(OutcomeRules.Evaluate(loss), TheaterOutcome.Repulsed, TheaterWhy.FrontPushedBack, "a loss held 20 s repulses");
            OutcomeInput objective = Running(TheaterOpKind.Assault, FrontSource.Objective);
            objective.GainSeconds = 60f;
            Is(OutcomeRules.Evaluate(objective), TheaterOutcome.None, TheaterWhy.None, "an objective front is never measured on the contour");
            OutcomeInput recon = Running(TheaterOpKind.Recon, FrontSource.Sector);
            recon.GainSeconds = 60f;
            Is(OutcomeRules.Evaluate(recon), TheaterOutcome.None, TheaterWhy.None, "only ASSAULT reads the gain");
        }

        private static void DefendClearTimeBoundary()
        {
            OutcomeInput i = Running(TheaterOpKind.Defend, FrontSource.Base);
            i.Held = true; i.NearFriendly = 0; i.HostileNear = 0;
            i.ClearSeconds = 29.9f;
            Is(OutcomeRules.Evaluate(i), TheaterOutcome.None, TheaterWhy.None, "29.9 s of quiet is not enough");
            i.ClearSeconds = 30f;
            Is(OutcomeRules.Evaluate(i), TheaterOutcome.Secured, TheaterWhy.HeldClear, "30.0 s of quiet secures, and a base DEFEND needs no friendly nearby");
            i.Elapsed = 45f;
            Is(OutcomeRules.Evaluate(i), TheaterOutcome.None, TheaterWhy.None, "at exactly 45 s elapsed it is still too early");
            i.Elapsed = 45.1f;
            Is(OutcomeRules.Evaluate(i), TheaterOutcome.Secured, TheaterWhy.HeldClear, "just past 45 s it may secure");
            i.HostileNear = 1;
            Is(OutcomeRules.Evaluate(i), TheaterOutcome.None, TheaterWhy.None, "a hostile in the near ring blocks it whatever the timer says");
            i.HostileNear = 0; i.Held = false;
            Is(OutcomeRules.Evaluate(i), TheaterOutcome.None, TheaterWhy.None, "a front that is no longer held is not secured by quiet");
            OutcomeInput sector = Running(TheaterOpKind.Defend, FrontSource.Sector);
            sector.Held = true; sector.NearFriendly = 0; sector.ClearSeconds = 40f; sector.ReachFriendly = 2;
            Is(OutcomeRules.Evaluate(sector), TheaterOutcome.None, TheaterWhy.None, "a sector DEFEND needs a friendly presence");
            sector.NearFriendly = 1;
            Is(OutcomeRules.Evaluate(sector), TheaterOutcome.Secured, TheaterWhy.HeldClear, "with a friendly present it secures");
        }

        private static void DefendLostBeatsClearAndNamesTheCause()
        {
            OutcomeInput b = Running(TheaterOpKind.Defend, FrontSource.Base);
            b.Held = true; b.ClearSeconds = 40f; b.LostDefense = true;
            Is(OutcomeRules.Evaluate(b), TheaterOutcome.Repulsed, TheaterWhy.BaseLost, "a lost base is BaseLost, and loss beats the clear time");
            b.Found = false; b.Live = false;
            Is(OutcomeRules.Evaluate(b), TheaterOutcome.Repulsed, TheaterWhy.BaseLost, "a base that vanished is lost even though its read is gone");
            OutcomeInput o = Running(TheaterOpKind.Defend, FrontSource.Objective);
            o.LostDefense = true;
            Is(OutcomeRules.Evaluate(o), TheaterOutcome.Repulsed, TheaterWhy.ObjectiveLost, "a lost objective");
            OutcomeInput s = Running(TheaterOpKind.Defend, FrontSource.Sector);
            s.LostDefense = true;
            Is(OutcomeRules.Evaluate(s), TheaterOutcome.Repulsed, TheaterWhy.FrontPushedBack, "a sector no longer held");
            OutcomeInput a = Running(TheaterOpKind.Assault, FrontSource.Sector);
            a.LostDefense = true;
            Is(OutcomeRules.Evaluate(a), TheaterOutcome.None, TheaterWhy.None, "lost defence is a DEFEND rule only");
        }

        private static void ReconNeedsObservationAndTime()
        {
            OutcomeInput i = Running(TheaterOpKind.Recon, FrontSource.Sector);
            i.Observed = true; i.Elapsed = 44f;
            Is(OutcomeRules.Evaluate(i), TheaterOutcome.None, TheaterWhy.None, "observed before 45 s keeps running");
            i.Elapsed = 45.1f;
            Is(OutcomeRules.Evaluate(i), TheaterOutcome.Secured, TheaterWhy.Observed, "observed after 45 s secures");
            i.Observed = false;
            Is(OutcomeRules.Evaluate(i), TheaterOutcome.None, TheaterWhy.None, "not observed keeps running");
        }

        private static void CarriersGoneEndsAssaultAndRecon()
        {
            OutcomeInput i = Running(TheaterOpKind.Assault, FrontSource.Sector);
            i.ReachFriendlyGround = 0; i.Elapsed = 45f;
            Is(OutcomeRules.Evaluate(i), TheaterOutcome.None, TheaterWhy.None, "no carriers at exactly 45 s is still too early");
            i.Elapsed = 46f;
            Is(OutcomeRules.Evaluate(i), TheaterOutcome.Repulsed, TheaterWhy.ForcesLost, "an assault that lost every ground vehicle in reach is repulsed");
            i.InitialGround = 0;
            Is(OutcomeRules.Evaluate(i), TheaterOutcome.None, TheaterWhy.None, "an assault that never had ground to lose is not repulsed for it");
            OutcomeInput r = Running(TheaterOpKind.Recon, FrontSource.Sector);
            r.ReachFriendlyGround = 0;
            Is(OutcomeRules.Evaluate(r), TheaterOutcome.Repulsed, TheaterWhy.ForcesLost, "the same for a recon");
        }

        private static void LostForcesEndANonBaseDefend()
        {
            OutcomeInput s = Running(TheaterOpKind.Defend, FrontSource.Sector);
            s.Held = true; s.ReachFriendly = 0; s.NearFriendly = 0;
            Is(OutcomeRules.Evaluate(s), TheaterOutcome.Repulsed, TheaterWhy.ForcesLost, "a sector DEFEND with no friendly at all is lost");
            s.ReachFriendly = 1;
            Is(OutcomeRules.Evaluate(s), TheaterOutcome.None, TheaterWhy.None, "one friendly in reach keeps it running");
            OutcomeInput b = Running(TheaterOpKind.Defend, FrontSource.Base);
            b.Held = true; b.ReachFriendly = 0; b.NearFriendly = 0;
            Is(OutcomeRules.Evaluate(b), TheaterOutcome.None, TheaterWhy.None, "a base is its own presence");
            s.ReachFriendly = 0; s.Elapsed = 30f;
            Is(OutcomeRules.Evaluate(s), TheaterOutcome.None, TheaterWhy.None, "nothing fires before the 45 s minimum");
        }

        private static void NoForcesNeedsEveryPrecondition()
        {
            OutcomeInput i = Running(TheaterOpKind.Assault, FrontSource.Objective);
            i.Groups = 0; i.NoForcesSeconds = 119.9f;
            Is(OutcomeRules.Evaluate(i), TheaterOutcome.None, TheaterWhy.None, "119.9 s without a group keeps running");
            i.NoForcesSeconds = 120f;
            Is(OutcomeRules.Evaluate(i), TheaterOutcome.Stalled, TheaterWhy.NoForces, "120 s without a group stalls");
            OutcomeInput noReport = i; noReport.HaveReport = false;
            Is(OutcomeRules.Evaluate(noReport), TheaterOutcome.None, TheaterWhy.None, "no ground report never stalls an operation");
            OutcomeInput noSteer = i; noSteer.Steering = false;
            Is(OutcomeRules.Evaluate(noSteer), TheaterOutcome.None, TheaterWhy.None, "steering off never stalls an operation");
            OutcomeInput air = i; air.CarrierBased = false;
            Is(OutcomeRules.Evaluate(air), TheaterOutcome.None, TheaterWhy.None, "an operation picked without a carrier is not stalled for lacking groups");
            OutcomeInput someGroup = i; someGroup.Groups = 1;
            Is(OutcomeRules.Evaluate(someGroup), TheaterOutcome.None, TheaterWhy.None, "one group is enough");
        }

        private static void ALostFrontEndsAfterItsGrace()
        {
            OutcomeInput i = Running(TheaterOpKind.Assault, FrontSource.Sector);
            i.Found = false; i.Live = false; i.NotLiveSeconds = 59.9f;
            Is(OutcomeRules.Evaluate(i), TheaterOutcome.None, TheaterWhy.None, "a front gone 59.9 s is still waited for");
            i.NotLiveSeconds = 60f;
            Is(OutcomeRules.Evaluate(i), TheaterOutcome.Stalled, TheaterWhy.FrontVanished, "a front gone 60 s with no gain stalls");
            i.HoldAtOrigin = 0.5f; i.InitialHold = 0.1f;
            Is(OutcomeRules.Evaluate(i), TheaterOutcome.Secured, TheaterWhy.FrontAdvanced, "a front gone whose hold already satisfies the gain secures");
            OutcomeInput recon = Running(TheaterOpKind.Recon, FrontSource.Sector);
            recon.Found = false; recon.Live = false; recon.NotLiveSeconds = 60f; recon.HoldAtOrigin = 0.5f;
            Is(OutcomeRules.Evaluate(recon), TheaterOutcome.Stalled, TheaterWhy.FrontVanished, "only a sector ASSAULT can be secured by the gain test");
        }

        private static void TheTimeLimitIsTheLastResort()
        {
            OutcomeInput i = Running(TheaterOpKind.Assault, FrontSource.Sector);
            i.Elapsed = 599.9f;
            Is(OutcomeRules.Evaluate(i), TheaterOutcome.None, TheaterWhy.None, "599.9 s is still running");
            i.Elapsed = 600f;
            Is(OutcomeRules.Evaluate(i), TheaterOutcome.Stalled, TheaterWhy.TimeLimit, "600 s is the ceiling");
            i.HoldAtOrigin = 0.5f; i.GainSeconds = 20f;
            Is(OutcomeRules.Evaluate(i), TheaterOutcome.Secured, TheaterWhy.FrontAdvanced, "a sustained gain beats the time limit");
            OutcomeInput d = Running(TheaterOpKind.Defend, FrontSource.Base);
            d.Held = true; d.Elapsed = 700f; d.ClearSeconds = 40f;
            Is(OutcomeRules.Evaluate(d), TheaterOutcome.Secured, TheaterWhy.HeldClear, "a held base beats the time limit");
        }

        private static void ObjectiveStatusComesFirst()
        {
            OutcomeInput i = Running(TheaterOpKind.Assault, FrontSource.Objective);
            i.Elapsed = 700f; i.ObjectiveSecured = true;
            Is(OutcomeRules.Evaluate(i), TheaterOutcome.Secured, TheaterWhy.ObjectiveCaptured, "a completed objective beats the time limit");
            i.ObjectiveClosed = true;
            Is(OutcomeRules.Evaluate(i), TheaterOutcome.Secured, TheaterWhy.ObjectiveCaptured, "complete wins over closed");
            i.ObjectiveSecured = false;
            Is(OutcomeRules.Evaluate(i), TheaterOutcome.Repulsed, TheaterWhy.ObjectiveLost, "an objective gone from the active list without completing is lost");
            OutcomeInput sector = Running(TheaterOpKind.Assault, FrontSource.Sector);
            sector.ObjectiveSecured = true; sector.ObjectiveClosed = true;
            Is(OutcomeRules.Evaluate(sector), TheaterOutcome.None, TheaterWhy.None, "objective flags mean nothing on a sector front");
        }

        private static void ReadsAreIgnoredWhenTheFrontIsNotLive()
        {
            OutcomeInput recon = Running(TheaterOpKind.Recon, FrontSource.Sector);
            recon.Observed = true; recon.Live = false;
            Is(OutcomeRules.Evaluate(recon), TheaterOutcome.None, TheaterWhy.None, "an observation from a front that is no longer live is not trusted");
            OutcomeInput d = Running(TheaterOpKind.Defend, FrontSource.Base);
            d.Held = true; d.ClearSeconds = 99f; d.Found = false;
            Is(OutcomeRules.Evaluate(d), TheaterOutcome.None, TheaterWhy.None, "a clear time without a read of the front is not trusted");
            OutcomeInput a = Running(TheaterOpKind.Assault, FrontSource.Sector);
            a.Found = false; a.ReachFriendlyGround = 0;
            Is(OutcomeRules.Evaluate(a), TheaterOutcome.None, TheaterWhy.None, "zero counts from a missing read are not carriers gone");
        }
    }
}
